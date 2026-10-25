using System.Text.Json;
using UseConfig.Catalog;

namespace UseConfig.Storage;

public sealed class CatalogSettings(string settingsDirectory) : ICatalogSettings
{
    public static string DefaultDirectory => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "UseConfig");
    private readonly string directory = LocationPaths.Normalize(settingsDirectory);

    public Task<OperationResult<string?>> ReadAsync(CancellationToken cancellationToken) => Task.Run(() =>
    {
        cancellationToken.ThrowIfCancellationRequested();
        try
        {
            var path = Path.Combine(directory, "settings.json");
            // Missing files are handled by the open, not Exists(), which also hides access errors.
            using var stream = File.OpenRead(path);
            LocationPaths.RejectLinks(directory);
            if ((File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0)
                return Failure<string?>(ErrorCode.InvalidInput);
            using var document = JsonDocument.Parse(stream);
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object) return Failure<string?>(ErrorCode.MalformedContent);
            var fields = root.EnumerateObject().ToArray();
            if (fields.Length != 2 || fields.Count(field => field.Name == "settings_version") != 1
                || fields.Count(field => field.Name == "catalog_path") != 1)
                return Failure<string?>(ErrorCode.MalformedContent);
            var version = root.GetProperty("settings_version");
            if (version.ValueKind != JsonValueKind.Number || !version.TryGetInt32(out var number))
                return Failure<string?>(ErrorCode.MalformedContent);
            if (number != 1) return Failure<string?>(ErrorCode.UnsupportedFormat);
            var selected = root.GetProperty("catalog_path");
            if (selected.ValueKind == JsonValueKind.Null) return Success<string?>(null);
            if (selected.ValueKind != JsonValueKind.String) return Failure<string?>(ErrorCode.MalformedContent);
            var catalog = LocationPaths.Normalize(selected.GetString()!);
            if (LocationPaths.Overlaps(catalog, directory)) return Failure<string?>(ErrorCode.InvalidInput);
            return Success<string?>(catalog);
        }
        catch (FileNotFoundException) { return Success<string?>(null); }
        catch (DirectoryNotFoundException) { return Success<string?>(null); }
        catch (JsonException) { return Failure<string?>(ErrorCode.MalformedContent); }
        catch (Exception exception) when (LocationPaths.Classify(exception) is not null)
        {
            return Failure<string?>(LocationPaths.Classify(exception)!.Value);
        }
    }, cancellationToken);

    public Task<OperationResult<Completion>> RememberAsync(string path, CancellationToken cancellationToken) => Task.Run(() =>
    {
        cancellationToken.ThrowIfCancellationRequested();
        try
        {
            var catalog = LocationPaths.Normalize(path);
            if (LocationPaths.Overlaps(catalog, directory)) return Failure<Completion>(ErrorCode.InvalidInput);
            Directory.CreateDirectory(directory);
            LocationPaths.RejectLinks(directory);
            var destination = Path.Combine(directory, "settings.json");
            var temporary = Path.Combine(directory, $"settings-{Guid.NewGuid():N}.tmp");
            using (var stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                JsonSerializer.Serialize(stream, new { settings_version = 1, catalog_path = catalog });
                stream.Flush(true);
            }
            cancellationToken.ThrowIfCancellationRequested();
            LocationPaths.RejectLinks(directory);
            // Every replacement keeps a distinct recovery copy, including malformed/future settings.
            if (File.Exists(destination))
            {
                if ((File.GetAttributes(destination) & FileAttributes.ReparsePoint) != 0)
                    return Failure<Completion>(ErrorCode.InvalidInput);
                File.Replace(temporary, destination, Path.Combine(directory, $"settings-{Guid.NewGuid():N}.bak"));
            }
            else
                File.Move(temporary, destination, false);
            return Success(Completion.Done);
        }
        catch (Exception exception) when (LocationPaths.Classify(exception) is not null)
        {
            return Failure<Completion>(LocationPaths.Classify(exception)!.Value);
        }
    }, cancellationToken);

    private static OperationResult<T> Success<T>(T value) => new OperationResult<T>.Success(value);
    private static OperationResult<T> Failure<T>(ErrorCode code) => new OperationResult<T>.Failure(code);
}
