using System.Windows;
using Microsoft.Win32;
using UseConfig.Catalog;
using UseConfig.Storage;

namespace UseConfig.App;

public partial class MainWindow : Window
{
    private readonly MarkdownCatalogFiles files = new();
    private readonly CatalogSession session;
    private sealed record RecoveryChoice(string Label, RecoveryOperation Operation);

    public MainWindow()
    {
        session = new CatalogSession(new MarkdownCatalogLocation(CatalogSettings.DefaultDirectory, files),
            new CatalogSettings(CatalogSettings.DefaultDirectory));
        InitializeComponent();
    }

    private async void WindowLoaded(object sender, RoutedEventArgs e) =>
        await RunAsync(() => session.RestoreAsync(CancellationToken.None));

    private async void OpenCatalog(object sender, RoutedEventArgs e) => await SelectAsync(false);
    private async void CreateCatalog(object sender, RoutedEventArgs e) => await SelectAsync(true);
    private async void RetryCatalog(object sender, RoutedEventArgs e)
    {
        if ((session.CurrentPath ?? session.RememberedPath) is { } path)
            await RunAsync(() => session.OpenAsync(path, false, true, CancellationToken.None));
        else
            await RunAsync(() => session.RestoreAsync(CancellationToken.None));
    }

    private async Task SelectAsync(bool create)
    {
        var dialog = new OpenFolderDialog
        {
            Title = create ? "Choose an empty folder to create a catalog" : "Open catalog",
            Multiselect = false
        };
        try
        {
            if (dialog.ShowDialog(this) != true) return;
        }
        catch (System.Runtime.InteropServices.COMException)
        {
            Status.Text = "The folder picker is unavailable. Please try again.";
            return;
        }
        await RunAsync(() => session.OpenAsync(dialog.FolderName, create, true, CancellationToken.None));
    }

    private async Task RunAsync(Func<Task<OperationResult<Completion>>> operation)
    {
        Actions.IsEnabled = false;
        Status.Text = "Opening catalog…";
        try
        {
            var result = await operation();
            CatalogPath.Text = session.CurrentPath ?? session.RememberedPath ?? "No catalog selected.";
            CatalogRecords.ItemsSource = session.Current?.Folders.Select(folder => $"Folder: {folder.Name}")
                .Concat(session.Current.Entries.Select(entry => $"Website: {entry.Name}"));
            CatalogErrors.ItemsSource = session.Current?.Diagnostics.Select(diagnostic =>
                $"{diagnostic.Code}: {diagnostic.RelativePath ?? "catalog"}{(diagnostic.Field is null ? "" : " (" + diagnostic.Field + ")")}");
            Status.Text = result is OperationResult<Completion>.Failure failure ? Describe(failure.Code)
                : session.Current?.IsReadOnly == true ? "Catalog is read-only. Repair the listed files and retry. This selection was not saved."
                : session.PersistenceError is not null ? "Catalog is open, but its path could not be remembered. Choose it again to retry saving."
                : session.Current is { } current ? $"Catalog is open. {current.Folders.Count} folders, {current.Entries.Count} websites."
                : "Select an existing catalog or create one in an empty folder.";
            RetryButton.IsEnabled = true;
            RecoveryActions.Visibility = Visibility.Collapsed;
            if (session.CurrentPath is { } root && session.Current?.Diagnostics.Any(item => item.Code == ErrorCode.RecoveryRequired) == true)
            {
                var pending = await files.FindAsync(root, CancellationToken.None);
                if (pending is OperationResult<IReadOnlyList<RecoveryOperation>>.Success success && success.Value.Count != 0)
                {
                    RecoveryOperations.ItemsSource = success.Value.Select((operation, index) => new RecoveryChoice($"Previous version {index + 1}", operation));
                    RecoveryOperations.SelectedIndex = 0;
                    RecoveryActions.Visibility = Visibility.Visible;
                    Status.Text = "Catalog is read-only because an interrupted operation needs recovery.";
                }
                else Status.Text = "Recovery data could not be verified. Preserve the catalog and service copies for inspection.";
            }
        }
        finally
        {
            Actions.IsEnabled = true;
        }
    }

    private async void SaveRecoveryCopy(object sender, RoutedEventArgs e)
    {
        if (session.CurrentPath is not { } root || RecoveryOperations.SelectedItem is not RecoveryChoice choice) return;
        var dialog = new OpenFolderDialog { Title = "Choose an external folder for a recovery copy", Multiselect = false };
        try { if (dialog.ShowDialog(this) != true) return; }
        catch (System.Runtime.InteropServices.COMException) { Status.Text = "The folder picker is unavailable. Please try again."; return; }
        Actions.IsEnabled = false;
        RecoveryActions.IsEnabled = false;
        try
        {
            var result = await files.SaveCopyAsync(root, choice.Operation, dialog.FolderName, CancellationToken.None);
            Status.Text = result is OperationResult<string>.Success success
                ? $"Recovery copy saved to {success.Value}. The current catalog is unchanged. Choose the copy explicitly to open it."
                : Describe(((OperationResult<string>.Failure)result).Code);
        }
        finally { Actions.IsEnabled = true; RecoveryActions.IsEnabled = true; }
    }

    private static string Describe(ErrorCode code) => code switch
    {
        ErrorCode.NotFound => "The catalog directory is missing. Retry or choose another location.",
        ErrorCode.AccessDenied => "Access was denied. Check permissions and retry, or choose another location.",
        ErrorCode.AlreadyExists => "The selected folder is not empty. No existing files were replaced.",
        ErrorCode.MalformedContent => "Catalog metadata or settings are malformed. Repair them or explicitly choose another catalog.",
        ErrorCode.UnsupportedFormat => "The catalog or settings version is unsupported. Choose a supported catalog; existing files are preserved.",
        ErrorCode.InvalidInput => "Choose a normal directory outside the application settings directory. Linked or overlapping paths are not supported.",
        ErrorCode.Conflict => "The directory changed during creation. Check it and try again.",
        ErrorCode.InsufficientSpace => "There is not enough disk space. Free space and retry.",
        _ => "The operation could not finish. Check the directory and permissions, then retry."
    };
}
