using System.Windows;
using Microsoft.Win32;
using UseConfig.Catalog;
using UseConfig.Storage;

namespace UseConfig.App;

public partial class MainWindow : Window
{
    private readonly CatalogSession session = new(
        new EmptyCatalogLocation(CatalogSettings.DefaultDirectory), new CatalogSettings(CatalogSettings.DefaultDirectory));

    public MainWindow()
    {
        InitializeComponent();
    }

    private async void WindowLoaded(object sender, RoutedEventArgs e) =>
        await RunAsync(() => session.RestoreAsync(CancellationToken.None));

    private async void OpenCatalog(object sender, RoutedEventArgs e) => await SelectAsync(false);
    private async void CreateCatalog(object sender, RoutedEventArgs e) => await SelectAsync(true);
    private async void RetryCatalog(object sender, RoutedEventArgs e)
    {
        if (session.RememberedPath is { } path)
            await RunAsync(() => session.OpenAsync(path, false, false, CancellationToken.None));
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
            Status.Text = result is OperationResult<Completion>.Failure failure ? Describe(failure.Code)
                : session.PersistenceError is not null ? "Catalog is open, but its path could not be remembered. Choose it again to retry saving."
                : session.CurrentPath is not null ? "Empty catalog is open."
                : "Select an existing catalog or create one in an empty folder.";
            RetryButton.IsEnabled = true;
        }
        finally
        {
            Actions.IsEnabled = true;
        }
    }

    private static string Describe(ErrorCode code) => code switch
    {
        ErrorCode.CatalogContentsNotYetSupported => "Catalogs with topics or other files cannot be opened yet. Choose an empty catalog.",
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
