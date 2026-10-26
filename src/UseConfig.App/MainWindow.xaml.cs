using System.Windows;
using System.Windows.Input;
using Microsoft.Win32;
using UseConfig.Catalog;
using UseConfig.Storage;

namespace UseConfig.App;

public partial class MainWindow : Window
{
    private readonly CatalogFlow flow;
    private bool choosing = true;
    private IInputElement? busyFocus;
    private sealed record RecoveryChoice(string Label, RecoveryOperation Operation);

    public MainWindow() : this(new MarkdownCatalogFiles()) { }

    private MainWindow(MarkdownCatalogFiles files) : this(
        new MarkdownCatalogLocation(CatalogSettings.DefaultDirectory, files),
        new CatalogSettings(CatalogSettings.DefaultDirectory), files)
    { }

    internal MainWindow(ICatalogLocation location, ICatalogSettings settings, ICatalogRecovery recovery)
    {
        flow = new CatalogFlow(new CatalogSession(location, settings), recovery);
        InitializeComponent();
        flow.Changed += Render;
        Render();
    }

    private async void WindowLoaded(object sender, RoutedEventArgs e)
    {
        await flow.RestoreAsync();
        FocusAction();
    }

    private async void OpenCatalog(object sender, RoutedEventArgs e) => await SelectAsync(false);
    private async void CreateCatalog(object sender, RoutedEventArgs e) => await SelectAsync(true);
    private async void RetryCatalog(object sender, RoutedEventArgs e) => await flow.RetryAsync();
    private void ChangeCatalog(object sender, RoutedEventArgs e) => flow.Choose();
    private void BackToCatalog(object sender, RoutedEventArgs e) => flow.Back();

    private async Task SelectAsync(bool create)
    {
        if (flow.IsBusy) return;
        var path = PickFolder(create ? "Choose an empty folder to create a catalog" : "Open catalog");
        await flow.SelectAsync(path, create);
    }

    private string? PickFolder(string title)
    {
        var focused = Keyboard.FocusedElement;
        try
        {
            var dialog = new OpenFolderDialog { Title = title, Multiselect = false };
            return dialog.ShowDialog(this) == true ? dialog.FolderName : null;
        }
        catch (System.Runtime.InteropServices.COMException)
        {
            flow.PickerFailed();
            return null;
        }
        finally
        {
            if (focused is not null) Keyboard.Focus(focused);
        }
    }

    private void Render()
    {
        var session = flow.Session;
        var switched = choosing != flow.IsChoosing;
        choosing = flow.IsChoosing;
        StartView.Visibility = choosing ? Visibility.Visible : Visibility.Collapsed;
        CatalogView.Visibility = choosing ? Visibility.Collapsed : Visibility.Visible;
        BackButton.Visibility = session.Current is not null ? Visibility.Visible : Visibility.Collapsed;
        if (flow.IsBusy && ContentArea.IsEnabled) busyFocus = Keyboard.FocusedElement;
        ContentArea.IsEnabled = !flow.IsBusy;
        Loading.Visibility = flow.IsBusy ? Visibility.Visible : Visibility.Collapsed;
        Loading.IsIndeterminate = flow.IsBusy && SystemParameters.ClientAreaAnimation;
        Status.Text = flow.Status;
        RememberedPath.Text = session.RememberedPath ?? "No catalog remembered.";
        AttemptPanel.Visibility = flow.AttemptedPath is null ? Visibility.Collapsed : Visibility.Visible;
        AttemptedPath.Text = flow.AttemptedPath;
        CatalogPath.Text = session.CurrentPath;
        CatalogRecords.ItemsSource = session.Current?.Folders.Select(folder => $"Folder: {folder.Name}")
            .Concat(session.Current.Entries.Select(entry => $"Website: {entry.Name}"));
        CatalogErrors.ItemsSource = session.Current?.Diagnostics.Select(diagnostic =>
            $"{diagnostic.Code}: {diagnostic.RelativePath ?? "catalog"}{(diagnostic.Field is null ? "" : " (" + diagnostic.Field + ")")} — {CatalogFlow.Describe(diagnostic.Code)}");
        EmptyState.Visibility = session.Current is { IsReadOnly: false, Entries.Count: 0 } ? Visibility.Visible : Visibility.Collapsed;
        var selectedRecovery = (RecoveryOperations.SelectedItem as RecoveryChoice)?.Operation;
        var choices = flow.RecoveryOperations.Select((operation, index) => new RecoveryChoice($"Previous version {index + 1}", operation)).ToArray();
        RecoveryOperations.ItemsSource = choices;
        RecoveryOperations.SelectedItem = choices.FirstOrDefault(choice => choice.Operation == selectedRecovery) ?? choices.FirstOrDefault();
        RecoveryActions.Visibility = choices.Length == 0 ? Visibility.Collapsed : Visibility.Visible;
        if (switched)
        {
            ContentScroll.ScrollToTop();
            FocusAction();
        }
        else if (!flow.IsBusy && busyFocus is not null)
        {
            Keyboard.Focus(busyFocus);
        }
        if (!flow.IsBusy) busyFocus = null;
    }

    private void FocusAction()
    {
        if (!flow.IsBusy) (choosing ? OpenButton : ChangeButton).Focus();
    }

    private async void SaveRecoveryCopy(object sender, RoutedEventArgs e)
    {
        if (flow.IsBusy || RecoveryOperations.SelectedItem is not RecoveryChoice choice) return;
        var destination = PickFolder("Choose an external folder for a recovery copy");
        if (destination is not null) await flow.SaveRecoveryAsync(choice.Operation, destination);
    }
}
