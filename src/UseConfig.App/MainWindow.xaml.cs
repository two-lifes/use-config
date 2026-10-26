using System.Windows;
using System.Windows.Input;
using Microsoft.Win32;
using UseConfig.Catalog;
using UseConfig.Storage;

namespace UseConfig.App;

public partial class MainWindow : Window
{
    private readonly CatalogFlow flow;
    private readonly CatalogShell shell;
    private bool closeApproved;
    private bool focusActionPending;
    private bool choosing = true;
    private IInputElement? busyFocus;
    private sealed record RecoveryChoice(string Label, RecoveryOperation Operation);

    public MainWindow() : this(new MarkdownCatalogFiles()) { }

    private MainWindow(MarkdownCatalogFiles files) : this(
        new MarkdownCatalogLocation(CatalogSettings.DefaultDirectory, files),
        new CatalogSettings(CatalogSettings.DefaultDirectory), files, new MarkdownDescriptionEditor(files))
    { }

    internal MainWindow(ICatalogLocation location, ICatalogSettings settings, ICatalogRecovery recovery, IDescriptionEditor editor)
    {
        flow = new CatalogFlow(new CatalogSession(location, settings), recovery);
        shell = new CatalogShell(flow.Session, editor);
        InitializeComponent();
        BrowserPane.Connect(shell, PromptEdits);
        flow.Changed += Render;
        shell.Changed += Render;
        Render();
    }

    private async void WindowLoaded(object sender, RoutedEventArgs e)
    {
        await flow.RestoreAsync();
        FocusAction();
    }

    private async void OpenCatalog(object sender, RoutedEventArgs e) => await SelectAsync(false);
    private async void CreateCatalog(object sender, RoutedEventArgs e) => await SelectAsync(true);
    private async void RetryCatalog(object sender, RoutedEventArgs e)
    {
        if (!flow.IsBusy) await shell.TransitionAsync(PromptEdits, flow.RetryAsync);
    }
    private async void ChangeCatalog(object sender, RoutedEventArgs e)
    {
        if (!flow.IsBusy) await shell.TransitionAsync(PromptEdits, () => { flow.Choose(); return Task.CompletedTask; });
    }
    private void BackToCatalog(object sender, RoutedEventArgs e) => flow.Back();

    private async Task SelectAsync(bool create)
    {
        if (flow.IsBusy || shell.IsBusy) return;
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
        shell.Synchronize();
        var switched = choosing != flow.IsChoosing;
        choosing = flow.IsChoosing;
        StartView.Visibility = choosing ? Visibility.Visible : Visibility.Collapsed;
        CatalogView.Visibility = choosing ? Visibility.Collapsed : Visibility.Visible;
        BackButton.Visibility = session.Current is not null ? Visibility.Visible : Visibility.Collapsed;
        if ((flow.IsBusy || shell.IsBusy) && ContentArea.IsEnabled) busyFocus = Keyboard.FocusedElement;
        ContentArea.IsEnabled = !flow.IsBusy && !shell.IsBusy;
        Loading.Visibility = flow.IsBusy || shell.IsBusy ? Visibility.Visible : Visibility.Collapsed;
        Loading.IsIndeterminate = (flow.IsBusy || shell.IsBusy) && SystemParameters.ClientAreaAnimation;
        Status.Text = flow.Status;
        RememberedPath.Text = session.RememberedPath ?? "No catalog remembered.";
        AttemptPanel.Visibility = flow.AttemptedPath is null ? Visibility.Collapsed : Visibility.Visible;
        AttemptedPath.Text = flow.AttemptedPath;
        CatalogPath.Text = session.CurrentPath;
        BrowserPane.Render();
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
            focusActionPending = true;
        }
        if (!flow.IsBusy && !shell.IsBusy && focusActionPending)
        {
            FocusAction();
            focusActionPending = false;
        }
        else if (!flow.IsBusy && !shell.IsBusy && busyFocus is not null)
        {
            Keyboard.Focus(busyFocus);
        }
        if (!flow.IsBusy && !shell.IsBusy) busyFocus = null;
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

    private EditChoice PromptEdits()
    {
        var dialog = new UnsavedDialog(this);
        dialog.ShowDialog();
        return dialog.Choice;
    }

    private async void WindowClosing(object? sender, System.ComponentModel.CancelEventArgs e)
    {
        if (closeApproved) return;
        if (flow.IsBusy || shell.IsBusy) { e.Cancel = true; return; }
        if (!shell.IsDirty) return;
        e.Cancel = true;
        if (await shell.TransitionAsync(PromptEdits, () => Task.CompletedTask))
        {
            closeApproved = true;
            // Close again only after the original Closing event has returned.
            _ = Dispatcher.BeginInvoke(new Action(Close));
        }
    }

    private void FindCanExecute(object sender, CanExecuteRoutedEventArgs e)
    {
        e.CanExecute = !flow.IsChoosing && !flow.IsBusy && !shell.IsBusy;
        e.Handled = true;
    }

    private void FindExecuted(object sender, ExecutedRoutedEventArgs e)
    {
        BrowserPane.FocusSearch();
        e.Handled = true;
    }

    private void WindowSizeChanged(object sender, SizeChangedEventArgs e)
    {
        if (BrowserPane is not null) BrowserPane.Height = Math.Max(300, ActualHeight - 290);
    }

}
