using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using YamlDotNet.Core;
using YamlDotNet.RepresentationModel;

namespace UseConfig.StorageProbe;

internal enum DocumentKind { Catalog, Topic, Entry }

internal sealed class ProbeException(string code) : Exception(code);

internal sealed record Document(DocumentKind Kind, Dictionary<string, string> Fields, string Body);

internal static class MarkdownCodec
{
    internal static readonly UTF8Encoding Utf8 = new(false, true);
    private static readonly string[] EntryKeys =
        ["id", "name", "url", "username", "password", "additional_url", "two_factor_note"];

    internal static Document Read(byte[] bytes, DocumentKind kind)
    {
        try
        {
            return Parse(bytes, kind);
        }
        catch (YamlException)
        {
            // Library diagnostics may include credential values or source excerpts.
            throw new ProbeException("invalid-yaml");
        }
        catch (DecoderFallbackException)
        {
            throw new ProbeException("invalid-unicode");
        }
        catch (EncoderFallbackException)
        {
            throw new ProbeException("invalid-unicode");
        }
    }

    private static Document Parse(byte[] bytes, DocumentKind kind)
    {
        var text = Utf8.GetString(bytes);
        if (text.StartsWith('\uFEFF')) text = text[1..];
        text = Normalize(text);
        if (!text.StartsWith("---\n", StringComparison.Ordinal)) Fail("envelope");
        var end = text.IndexOf("\n---", 3, StringComparison.Ordinal);
        while (end >= 0 && end + 4 < text.Length && text[end + 4] != '\n')
            end = text.IndexOf("\n---", end + 4, StringComparison.Ordinal);
        if (end < 0) Fail("envelope");
        var front = text[4..end];
        var body = end + 4 == text.Length ? "" : text[(end + 5)..];
        if (kind != DocumentKind.Entry && body.Length != 0) Fail("metadata-body");
        var lines = front.Split('\n');
        var known = kind switch
        {
            DocumentKind.Catalog => new HashSet<string> { "format_version" },
            DocumentKind.Topic => new HashSet<string> { "format_version", "id" },
            _ => new HashSet<string>(EntryKeys.Append("format_version"))
        };
        var seen = new HashSet<string>();
        var yaml = new StringBuilder();
        for (var i = 0; i < lines.Length; i++)
        {
            var match = Regex.Match(lines[i], "^([a-z_]+): (.+)$", RegexOptions.CultureInvariant);
            if (!match.Success) Fail("field-syntax");
            var key = match.Groups[1].Value;
            var value = match.Groups[2].Value;
            if (!known.Contains(key)) Fail("unknown-key");
            if (!seen.Add(key)) Fail("duplicate-key");
            if (key == "format_version")
            {
                if (!Regex.IsMatch(value, "^[0-9]+$")) Fail("version-type");
                if (value != "1") Fail("unsupported-version");
            }
            else if (value is "|" or "|-" or "|+")
            {
                // v1 fixes indentation at two spaces; YAML itself infers indentation.
                var literal = new StringBuilder();
                while (i + 1 < lines.Length && (lines[i + 1].Length == 0 || lines[i + 1].StartsWith(' ')))
                {
                    var line = lines[++i];
                    if (line.Length != 0 && !line.StartsWith("  ", StringComparison.Ordinal)) Fail("block-indent");
                    literal.Append(line.Length == 0 ? "" : line[2..]).Append('\n');
                }
                var decoded = literal.ToString();
                if (value == "|-") decoded = decoded.TrimEnd('\n');
                else if (value == "|" && decoded.Length > 0) decoded = decoded.TrimEnd('\n') + "\n";
                value = Quote(decoded);
            }
            else
            {
                ValidateQuoted(value);
            }
            yaml.Append(key).Append(": ").Append(value).Append('\n');
        }
        if (!seen.Contains("format_version")) Fail("missing-version");
        var stream = new YamlStream();
        stream.Load(new StringReader(yaml.ToString()));
        if (stream.Documents.Count != 1 || stream.Documents[0].RootNode is not YamlMappingNode) Fail("mapping");
        var mapping = (YamlMappingNode)stream.Documents[0].RootNode;
        var fields = new Dictionary<string, string>();
        foreach (var pair in mapping.Children)
        {
            if (pair.Key is not YamlScalarNode || pair.Value is not YamlScalarNode) Fail("scalar");
            var key = ((YamlScalarNode)pair.Key).Value!;
            if (key != "format_version")
            {
                var value = Normalize(((YamlScalarNode)pair.Value).Value ?? "");
                _ = Utf8.GetByteCount(value);
                fields.Add(key, value);
            }
        }
        if (kind != DocumentKind.Catalog)
        {
            if (!fields.TryGetValue("id", out var id) || !Guid.TryParseExact(id, "D", out var parsed) || parsed == Guid.Empty)
                Fail("id");
            fields["id"] = Guid.Parse(fields["id"]).ToString("D");
        }
        if (kind == DocumentKind.Entry)
        {
            if (!fields.TryGetValue("name", out var name) || string.IsNullOrWhiteSpace(name) || name.Contains('\n')) Fail("name");
            if (!fields.TryGetValue("url", out var url) || !ValidUrl(url)) Fail("url");
            foreach (var key in EntryKeys) fields.TryAdd(key, "");
            if (fields["additional_url"].Length != 0 && !ValidUrl(fields["additional_url"])) Fail("additional-url");
        }
        return new Document(kind, fields, body);
    }

    private static void ValidateQuoted(string value)
    {
        if (value.Length < 2 || value.Any(char.IsControl)) Fail("quoted-syntax");
        if (value[0] == '\'')
        {
            if (!Regex.IsMatch(value, "^'(?:[^']|'')*'$")) Fail("quoted-syntax");
            return;
        }
        if (value[0] != '"' || value[^1] != '"') Fail("quoted-syntax");
        for (var i = 1; i < value.Length - 1; i++)
        {
            if (value[i] == '"') Fail("quoted-syntax");
            if (value[i] != '\\') continue;
            i++;
            if (i >= value.Length - 1) Fail("escape");
            if (value[i] is '"' or '\\' or 'n' or 'r' or 't') continue;
            if (value[i] != 'u' || i + 4 >= value.Length - 1) Fail("escape");
            for (var h = 1; h <= 4; h++) if (!Uri.IsHexDigit(value[i + h])) Fail("escape");
            i += 4;
        }
    }

    private static string Normalize(string text)
    {
        text = text.Replace("\r\n", "\n", StringComparison.Ordinal);
        if (text.Contains('\r') || text.Contains('\0')) Fail("invalid-control");
        return text;
    }

    private static bool ValidUrl(string text) => !text.Any(c => char.IsWhiteSpace(c) || char.IsControl(c))
        && Uri.TryCreate(text, UriKind.Absolute, out var uri)
        && uri.Scheme is "http" or "https" && uri.Host.Length != 0;

    internal static byte[] Write(Document document)
    {
        var result = new StringBuilder("---\nformat_version: 1\n");
        var keys = document.Kind switch
        {
            DocumentKind.Catalog => Array.Empty<string>(),
            DocumentKind.Topic => ["id"],
            _ => EntryKeys
        };
        foreach (var key in keys) result.Append(key).Append(": ").Append(Quote(document.Fields[key])).Append('\n');
        result.Append("---\n").Append(document.Body);
        var bytes = Utf8.GetBytes(result.ToString());
        _ = Read(bytes, document.Kind);
        return bytes;
    }

    internal static string Quote(string text)
    {
        var result = new StringBuilder("\"");
        foreach (var c in text)
        {
            result.Append(c switch
            {
                '"' => "\\\"",
                '\\' => "\\\\",
                '\n' => "\\n",
                '\r' => "\\r",
                '\t' => "\\t",
                _ when char.IsControl(c) => "\\u" + ((int)c).ToString("x4", CultureInfo.InvariantCulture),
                _ => c.ToString()
            });
        }
        return result.Append('"').ToString();
    }

    private static void Fail(string code) => throw new ProbeException(code);
}
