namespace UseConfig.Catalog;

public static class CatalogFormat
{
    public const int CurrentVersion = 1;
    public static IReadOnlyList<int> SupportedVersions { get; } = Array.AsReadOnly(new[] { CurrentVersion });
}

public sealed class CatalogFolder(Guid id, string name, int formatVersion = CatalogFormat.CurrentVersion)
{
    public Guid Id { get; } = id;
    public string Name { get; } = name;
    public int FormatVersion { get; } = formatVersion;
}

public sealed class WebsiteEntry(
    Guid id, Guid folderId, string name, string url,
    string description = "", string username = "", string password = "",
    string additionalUrl = "", string twoFactorNote = "",
    int formatVersion = CatalogFormat.CurrentVersion)
{
    public Guid Id { get; } = id;
    public Guid FolderId { get; } = folderId;
    public int FormatVersion { get; } = formatVersion;
    public string Name { get; } = name;
    public string Url { get; } = url;
    public string Description { get; } = description;
    public string Username { get; } = username;
    public string Password { get; } = password;
    public string AdditionalUrl { get; } = additionalUrl;
    public string TwoFactorNote { get; } = twoFactorNote;
}
