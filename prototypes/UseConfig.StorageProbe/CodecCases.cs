namespace UseConfig.StorageProbe;

internal static class CodecCases
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
            Program.Require(doc.Fields["name"] == "Пример 日本語 😀");
            Program.Require(doc.Fields["username"] == "demo'user");
            Program.Require(doc.Fields["password"] == "  demo:\"quote\"\\path#123  ");
            Program.Require(doc.Fields["two_factor_note"] == "First line\nВторая строка");
            Program.Require(doc.Body == "# Описание\n\n日本語 😀\n\n");
            RoundTrip(doc);
        });
        test("codec-minimal-defaults", () =>
        {
            var doc = Read(Program.Original);
            Program.Require(doc.Fields["password"] == "" && doc.Fields["two_factor_note"] == "" && doc.Body == "");
            RoundTrip(doc);
        });
        test("codec-bom-crlf", () =>
        {
            var bytes = new byte[] { 0xEF, 0xBB, 0xBF }.Concat(MarkdownCodec.Utf8.GetBytes(Full.Replace("\n", "\r\n"))).ToArray();
            var doc = MarkdownCodec.Read(bytes, DocumentKind.Entry);
            Equal(Read(Full), doc);
            var saved = MarkdownCodec.Write(doc);
            Program.Require(!saved.AsSpan().StartsWith(new byte[] { 0xEF, 0xBB, 0xBF }) && !saved.Contains((byte)'\r'));
        });
        foreach (var (header, expected) in new[] { ("|-", "one\n\ntwo"), ("|", "one\n\ntwo\n"), ("|+", "one\n\ntwo\n\n") })
        {
            test("codec-literal-" + header, () =>
            {
                var doc = Read(WithField("two_factor_note: " + header + "\n  one\n\n  two\n\n"));
                Program.Require(doc.Fields["two_factor_note"] == expected);
                RoundTrip(doc);
            });
        }
        test("codec-fixed-block-indent", () =>
        {
            var doc = Read(WithField("two_factor_note: |-\n    indented\n"));
            Program.Require(doc.Fields["two_factor_note"] == "  indented");
        });
        test("codec-escaped-controls", () =>
        {
            var doc = Read(WithField("password: \"first\\nsecond\\t\\u0001\"\n"));
            Program.Require(doc.Fields["password"] == "first\nsecond\t\u0001");
            RoundTrip(doc);
        });
        test("codec-unicode-escaped-pair", () =>
        {
            var doc = Read(WithField("password: \"\\uD83D\\uDE00\"\n"));
            Program.Require(doc.Fields["password"] == "😀");
            RoundTrip(doc);
        });
        test("codec-description-delimiter-and-newlines", () =>
        {
            var doc = Read(Program.Original + "\n---\nbody\n\n");
            Program.Require(doc.Body == "\n---\nbody\n\n");
            RoundTrip(doc);
        });
        test("codec-closing-delimiter-eof", () => RoundTrip(Read(Program.Original.TrimEnd('\n'))));
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
            test("codec-invalid-field-" + invalidNumber++, () => Program.Reject(() => Read(WithField(field))));
        }
        foreach (var (name, text) in new[]
        {
            ("version", Program.Original.Replace("version: 1", "version: 2")),
            ("quoted-version", Program.Original.Replace("version: 1", "version: \"1\"")),
            ("missing-name", Program.Original.Replace("name: \"Example\"\n", "")),
            ("empty-name", Program.Original.Replace("name: \"Example\"", "name: \" \"")),
            ("scheme", Program.Original.Replace("https://example.com/", "file:///example")),
            ("uuid", Program.Original.Replace("33333333-3333-4333-8333-333333333333", "not-a-uuid")),
            ("bare-cr", Program.Original + "body\rtext"),
            ("leading-line", "\n" + Program.Original)
        }) test("codec-invalid-" + name, () => Program.Reject(() => Read(text)));
        test("codec-invalid-utf8", () => Program.Reject(() => MarkdownCodec.Read([0xFF, 0xFE], DocumentKind.Entry)));
        test("codec-invalid-metadata-body", () => Program.Reject(() => MarkdownCodec.Read(MarkdownCodec.Utf8.GetBytes(Program.Topic + "body"), DocumentKind.Topic)));
    }

    private static int invalidNumber;
    private static string WithField(string field) => Program.Original.Replace("\n---\n", "\n" + field + "---\n", StringComparison.Ordinal);
    private static Document Read(string text) => MarkdownCodec.Read(MarkdownCodec.Utf8.GetBytes(text), DocumentKind.Entry);

    private static void RoundTrip(Document doc) => Equal(doc, MarkdownCodec.Read(MarkdownCodec.Write(doc), doc.Kind));
    private static void Equal(Document expected, Document actual)
    {
        Program.Require(expected.Kind == actual.Kind && expected.Body == actual.Body && expected.Fields.Count == actual.Fields.Count);
        foreach (var (key, value) in expected.Fields) Program.Require(actual.Fields[key] == value);
    }
}
