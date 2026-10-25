using System.Text;
using UseConfig.Catalog;
using UseConfig.Storage;

internal static class MarkdownCodecChecks
{
    internal const string Full = "---\nformat_version: 1\nid: \"33333333-3333-4333-8333-333333333333\"\n"
        + "name: \"Пример 日本語 😀\"\nurl: \"https://example.com/\"\nusername: 'demo''user'\n"
        + "password: \"  demo:\\\"quote\\\"\\\\path#123  \"\nadditional_url: \"http://example.org/mail\"\n"
        + "two_factor_note: |-\n  First line\n  Вторая строка\n---\n# Описание\n\n日本語 😀\n\n";

    internal static void Run(Action<string, Action> test)
    {
        test("codec-manual-full-values", () =>
        {
            var doc = Read(Full);
            Require(doc.Fields["name"] == "Пример 日本語 😀");
            Require(doc.Fields["username"] == "demo'user");
            Require(doc.Fields["password"] == "  demo:\"quote\"\\path#123  ");
            Require(doc.Fields["two_factor_note"] == "First line\nВторая строка");
            Require(doc.Description == "# Описание\n\n日本語 😀\n\n");
            RoundTrip(doc);
        });
        test("codec-minimal-defaults", () =>
        {
            var doc = Read(Original);
            Require(doc.Fields["password"] == "" && doc.Fields["two_factor_note"] == "" && doc.Description == "");
            RoundTrip(doc);
        });
        test("codec-bom-crlf", () =>
        {
            var bytes = new byte[] { 0xEF, 0xBB, 0xBF }.Concat(Utf8.GetBytes(Full.Replace("\n", "\r\n"))).ToArray();
            var doc = ReadBytes(bytes, CatalogDocumentKind.Entry);
            Equal(Read(Full), doc);
            var saved = WriteBytes(doc);
            Require(!saved.AsSpan().StartsWith(new byte[] { 0xEF, 0xBB, 0xBF }) && !saved.Contains((byte)'\r'));
        });
        foreach (var (header, expected) in new[] { ("|-", "one\n\ntwo"), ("|", "one\n\ntwo\n"), ("|+", "one\n\ntwo\n\n") })
        {
            test("codec-literal-" + header, () =>
            {
                var doc = Read(WithField("two_factor_note: " + header + "\n  one\n\n  two\n\n"));
                Require(doc.Fields["two_factor_note"] == expected);
                RoundTrip(doc);
            });
        }
        test("codec-fixed-block-indent", () =>
        {
            var doc = Read(WithField("two_factor_note: |-\n    indented\n"));
            Require(doc.Fields["two_factor_note"] == "  indented");
        });
        test("codec-escaped-controls", () =>
        {
            var doc = Read(WithField("password: \"first\\nsecond\\t\\u0001\"\n"));
            Require(doc.Fields["password"] == "first\nsecond\t\u0001");
            RoundTrip(doc);
        });
        test("codec-unicode-escaped-pair", () =>
        {
            var doc = Read(WithField("password: \"\\uD83D\\uDE00\"\n"));
            Require(doc.Fields["password"] == "😀");
            RoundTrip(doc);
        });
        test("codec-description-delimiter-and-newlines", () =>
        {
            var doc = Read(Original + "\n---\nbody\n\n");
            Require(doc.Description == "\n---\nbody\n\n");
            RoundTrip(doc);
        });
        test("codec-closing-delimiter-eof", () => RoundTrip(Read(Original.TrimEnd('\n'))));
        foreach (var field in new[]
        {
            "unknown: \"value\"\n", "password: \"a\"\npassword: \"b\"\n", "password: plain\n",
            "password: &anchor \"value\"\n", "password: *anchor\n", "password: !!str \"value\"\n",
            "password: [\"a\"]\n", "password: {key: value}\n", "password: >\n  folded\n",
            "password: \"value\" # comment\n", "# comment\n", "\n", "password: \"\\x41\"\n",
            "password: \"\\0\"\n", "password: \"\\u0000\"\n", "password: \"\\r\"\n",
            "password: \"\\uD800\"\n", "password: |2\n  text\n", "password: |-\n text\n",
            "password: \"tab\ttext\"\n", "password: \"value\" trailing\n"
        })
        {
            // A counter identifies cases without printing potentially sensitive input.
            test("codec-invalid-field-" + invalidNumber++, () => Reject(() => Read(WithField(field))));
        }
        foreach (var (name, text) in new[]
        {
            ("version", Original.Replace("version: 1", "version: 2")),
            ("quoted-version", Original.Replace("version: 1", "version: \"1\"")),
            ("missing-name", Original.Replace("name: \"Example\"\n", "")),
            ("empty-name", Original.Replace("name: \"Example\"", "name: \" \"")),
            ("scheme", Original.Replace("https://example.com/", "file:///example")),
            ("uuid", Original.Replace("33333333-3333-4333-8333-333333333333", "not-a-uuid")),
            ("bare-cr", Original + "body\rtext"),
            ("leading-line", "\n" + Original)
        }) test("codec-invalid-" + name, () => Reject(() => Read(text)));
        test("codec-invalid-utf8", () => Reject(() => ReadBytes([0xFF, 0xFE], CatalogDocumentKind.Entry)));
        test("codec-invalid-metadata-body", () => Reject(() => ReadBytes(Utf8.GetBytes(Topic + "body"), CatalogDocumentKind.Topic)));
    }

    private static int invalidNumber;
    private static string WithField(string field) => Original.Replace("\n---\n", "\n" + field + "---\n", StringComparison.Ordinal);
    private static ParsedCatalogDocument Read(string text) => ReadBytes(Utf8.GetBytes(text), CatalogDocumentKind.Entry);

    private static void RoundTrip(ParsedCatalogDocument doc) => Equal(doc, ReadBytes(WriteBytes(doc), doc.Kind));
    private static void Equal(ParsedCatalogDocument expected, ParsedCatalogDocument actual)
    {
        Require(expected.Kind == actual.Kind && expected.Description == actual.Description && expected.Fields.Count == actual.Fields.Count);
        foreach (var (key, value) in expected.Fields) Require(actual.Fields[key] == value);
    }
    private static readonly UTF8Encoding Utf8 = new(false, true);
    private const string Original = "---\nformat_version: 1\nid: \"33333333-3333-4333-8333-333333333333\"\nname: \"Example\"\nurl: \"https://example.com/\"\n---\n";
    private const string Topic = "---\nformat_version: 1\nid: \"22222222-2222-4222-8222-222222222222\"\n---\n";
    private static ParsedCatalogDocument ReadBytes(byte[] bytes, CatalogDocumentKind kind) =>
        MarkdownCatalogCodec.Read(bytes, kind) is OperationResult<ParsedCatalogDocument>.Success success
            ? success.Value : throw new FormatException();
    private static byte[] WriteBytes(ParsedCatalogDocument document) =>
        MarkdownCatalogCodec.Write(document) is OperationResult<byte[]>.Success success
            ? success.Value : throw new FormatException();
    private static void Require(bool condition) { if (!condition) throw new InvalidOperationException("Codec assertion failed."); }
    private static void Reject(Action action)
    {
        try { action(); }
        catch (FormatException) { return; }
        throw new InvalidOperationException("Invalid document accepted.");
    }
}
