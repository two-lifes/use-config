using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using UseConfig.Catalog;

namespace UseConfig.Storage;

public enum CatalogDocumentKind { Catalog, Topic, Entry }

public sealed class ParsedCatalogDocument(CatalogDocumentKind kind, IDictionary<string, string> fields, string description)
{
    public CatalogDocumentKind Kind { get; } = kind;
    public IReadOnlyDictionary<string, string> Fields { get; } =
        new System.Collections.ObjectModel.ReadOnlyDictionary<string, string>(new Dictionary<string, string>(fields));
    public string Description { get; } = description;
}

public static class MarkdownCatalogCodec
{
    private static readonly UTF8Encoding Utf8 = new(false, true);
    private static readonly string[] EntryKeys = ["id", "name", "url", "username", "password", "additional_url", "two_factor_note"];

    public static OperationResult<ParsedCatalogDocument> Read(byte[] bytes, CatalogDocumentKind kind)
    {
        try
        {
            return new OperationResult<ParsedCatalogDocument>.Success(Parse(bytes, kind));
        }
        catch (FormatException exception)
        {
            return new OperationResult<ParsedCatalogDocument>.Failure(exception is VersionException
                ? ErrorCode.UnsupportedFormat : ErrorCode.MalformedContent);
        }
        catch (DecoderFallbackException) { return new OperationResult<ParsedCatalogDocument>.Failure(ErrorCode.MalformedContent); }
        catch (EncoderFallbackException) { return new OperationResult<ParsedCatalogDocument>.Failure(ErrorCode.MalformedContent); }
    }

    private static ParsedCatalogDocument Parse(byte[] bytes, CatalogDocumentKind kind)
    {
        var text = Utf8.GetString(bytes);
        if (text.StartsWith('\uFEFF')) text = text[1..];
        text = Normalize(text);
        if (!text.StartsWith("---\n", StringComparison.Ordinal)) throw new FormatException();
        var end = text.IndexOf("\n---", 3, StringComparison.Ordinal);
        while (end >= 0 && end + 4 < text.Length && text[end + 4] != '\n')
            end = text.IndexOf("\n---", end + 4, StringComparison.Ordinal);
        if (end < 0) throw new FormatException();
        var body = end + 4 == text.Length ? "" : text[(end + 5)..];
        if (kind != CatalogDocumentKind.Entry && body.Length != 0) throw new FormatException();
        var lines = text[4..end].Split('\n');
        var keys = Keys(kind);
        var fields = new Dictionary<string, string>(StringComparer.Ordinal);
        var versionSeen = false;
        for (var i = 0; i < lines.Length; i++)
        {
            var separator = lines[i].IndexOf(": ", StringComparison.Ordinal);
            if (separator < 1) throw new FormatException();
            var key = lines[i][..separator];
            var source = lines[i][(separator + 2)..];
            if (key == "format_version")
            {
                if (versionSeen || !Regex.IsMatch(source, "\\A[0-9]+\\z")) throw new FormatException();
                versionSeen = true;
                if (source != "1") throw new VersionException();
                continue;
            }
            if (!keys.Contains(key) || fields.ContainsKey(key)) throw new FormatException();
            string value;
            if (source is "|" or "|-" or "|+")
            {
                var literal = new StringBuilder();
                while (i + 1 < lines.Length && (lines[i + 1].Length == 0 || lines[i + 1].StartsWith(' ')))
                {
                    var line = lines[++i];
                    if (line.Length != 0 && !line.StartsWith("  ", StringComparison.Ordinal)) throw new FormatException();
                    literal.Append(line.Length == 0 ? "" : line[2..]).Append('\n');
                }
                value = literal.ToString();
                if (source == "|-") value = value.TrimEnd('\n');
                else if (source == "|" && value.Length > 0) value = value.TrimEnd('\n') + "\n";
            }
            else value = DecodeQuoted(source);
            fields.Add(key, Normalize(value));
        }
        if (!versionSeen) throw new FormatException();
        if (kind != CatalogDocumentKind.Catalog)
        {
            if (!fields.TryGetValue("id", out var id) || id.Length != 36
                || !Guid.TryParseExact(id, "D", out var parsed) || parsed == Guid.Empty) throw new FormatException();
            fields["id"] = parsed.ToString("D");
        }
        if (kind == CatalogDocumentKind.Entry)
        {
            if (!fields.TryGetValue("name", out var name) || string.IsNullOrWhiteSpace(name) || name.Contains('\n'))
                throw new FormatException();
            if (!fields.TryGetValue("url", out var url) || !ValidUrl(url)) throw new FormatException();
            foreach (var key in EntryKeys) fields.TryAdd(key, "");
            if (fields["additional_url"].Length != 0 && !ValidUrl(fields["additional_url"])) throw new FormatException();
        }
        return new ParsedCatalogDocument(kind, fields, body);
    }

    private static string DecodeQuoted(string source)
    {
        if (source.Length < 2 || source.Any(char.IsControl)) throw new FormatException();
        if (source[0] == '\'')
        {
            if (!Regex.IsMatch(source, "\\A'(?:[^']|'')*'\\z")) throw new FormatException();
            return source[1..^1].Replace("''", "'");
        }
        if (source[0] != '"' || source[^1] != '"') throw new FormatException();
        var result = new StringBuilder();
        for (var i = 1; i < source.Length - 1; i++)
        {
            var character = source[i];
            if (character == '"') throw new FormatException();
            if (character != '\\') { result.Append(character); continue; }
            if (++i >= source.Length - 1) throw new FormatException();
            character = source[i];
            if (character == 'u')
            {
                if (i + 4 >= source.Length - 1 || !ushort.TryParse(source.AsSpan(i + 1, 4),
                    NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture, out var code)) throw new FormatException();
                result.Append((char)code);
                i += 4;
            }
            else result.Append(character switch
            {
                '"' => '"',
                '\\' => '\\',
                'n' => '\n',
                'r' => '\r',
                't' => '\t',
                _ => throw new FormatException()
            });
        }
        return result.ToString();
    }

    private static string Normalize(string text)
    {
        text = text.Replace("\r\n", "\n", StringComparison.Ordinal);
        if (text.Contains('\r') || text.Contains('\0')) throw new FormatException();
        _ = Utf8.GetByteCount(text);
        return text;
    }

    private static bool ValidUrl(string text) => !text.Any(c => char.IsWhiteSpace(c) || char.IsControl(c))
        && Uri.TryCreate(text, UriKind.Absolute, out var uri) && uri.Scheme is "http" or "https" && uri.Host.Length != 0;

    public static OperationResult<byte[]> Write(ParsedCatalogDocument document)
    {
        try
        {
            var output = new StringBuilder("---\nformat_version: 1\n");
            var keys = Keys(document.Kind);
            if (document.Fields.Keys.Any(key => !keys.Contains(key))) throw new FormatException();
            foreach (var key in keys)
            {
                if (!document.Fields.TryGetValue(key, out var value))
                {
                    if (key is "id" or "name" or "url") throw new FormatException();
                    value = "";
                }
                if (key == "id" && Guid.TryParseExact(value, "D", out var id)) value = id.ToString("D");
                output.Append(key).Append(": ").Append(Quote(Normalize(value))).Append('\n');
            }
            output.Append("---\n").Append(Normalize(document.Description));
            var bytes = Utf8.GetBytes(output.ToString());
            var parsed = Read(bytes, document.Kind);
            return parsed is OperationResult<ParsedCatalogDocument>.Failure failure
                ? new OperationResult<byte[]>.Failure(failure.Code) : new OperationResult<byte[]>.Success(bytes);
        }
        catch (FormatException) { return new OperationResult<byte[]>.Failure(ErrorCode.MalformedContent); }
        catch (EncoderFallbackException) { return new OperationResult<byte[]>.Failure(ErrorCode.MalformedContent); }
    }

    private static string[] Keys(CatalogDocumentKind kind) => kind switch
    {
        CatalogDocumentKind.Catalog => [],
        CatalogDocumentKind.Topic => ["id"],
        CatalogDocumentKind.Entry => EntryKeys,
        _ => throw new ArgumentOutOfRangeException(nameof(kind))
    };

    private static string Quote(string text)
    {
        var result = new StringBuilder("\"");
        foreach (var character in text)
            result.Append(character switch
            {
                '"' => "\\\"",
                '\\' => "\\\\",
                '\n' => "\\n",
                '\t' => "\\t",
                _ when char.IsControl(character) => "\\u" + ((int)character).ToString("x4", CultureInfo.InvariantCulture),
                _ => character.ToString()
            });
        return result.Append('"').ToString();
    }

    private sealed class VersionException : FormatException;
}
