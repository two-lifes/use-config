using System.Windows;
using System.Windows.Controls;
using UseConfig.Catalog;

namespace UseConfig.App;

public partial class CatalogPane : UserControl
{
    private CatalogShell? shell;
    private Func<EditChoice>? prompt;
    private bool rendering;
    public CatalogPane() => InitializeComponent();

    internal void Connect(CatalogShell state, Func<EditChoice> choose)
    {
        shell = state;
        prompt = choose;
        shell.Changed += Render;
        Render();
    }

    internal void FocusSearch() => SearchBox.Focus();

    internal void Render()
    {
        if (shell is null) return;
        rendering = true;
        try
        {
            IsEnabled = !shell.IsBusy;
            if (SearchBox.Text != shell.Query) SearchBox.Text = shell.Query;
            var folders = shell.Folders;
            var entries = shell.Entries;
            if (!FolderList.Items.Cast<CatalogFolder>().SequenceEqual(folders)) FolderList.ItemsSource = folders;
            FolderList.SelectedItem = folders.FirstOrDefault(folder => folder.Id == shell.FolderId);
            if (!WebsiteList.Items.Cast<WebsiteEntry>().SequenceEqual(entries)) WebsiteList.ItemsSource = entries;
            WebsiteList.SelectedItem = entries.FirstOrDefault(entry => entry.Id == shell.EntryId);
            NoFolders.Visibility = folders.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
            NoWebsites.Text = shell.Query.Length == 0 ? "No websites in this folder. Add Markdown entries, then Retry." : "No matches. Clear search to return to the selected folder.";
            NoWebsites.Visibility = entries.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
            var selected = shell.Entry;
            Details.Visibility = selected is null ? Visibility.Collapsed : Visibility.Visible;
            NoSelection.Visibility = selected is null ? Visibility.Visible : Visibility.Collapsed;
            EntryName.Text = selected?.Name;
            EntryUrl.Text = selected?.Url;
            EntryUsername.Text = selected?.Username;
            EntryAdditionalUrl.Text = selected?.AdditionalUrl;
            EntryNote.Text = selected?.TwoFactorNote;
            if (DescriptionBox.Text != shell.Description) DescriptionBox.Text = shell.Description;
            DescriptionBox.IsReadOnly = !shell.CanEdit;
            ReadOnlyNotice.Visibility = shell.CanEdit ? Visibility.Collapsed : Visibility.Visible;
            SaveButton.IsEnabled = shell.CanEdit && shell.IsDirty;
            DiscardButton.IsEnabled = shell.IsDirty;
            EditorStatus.Text = shell.Status;
        }
        finally { rendering = false; }
    }

    private void SearchChanged(object sender, TextChangedEventArgs e) { if (!rendering) shell?.Search(SearchBox.Text); }
    private void DescriptionChanged(object sender, TextChangedEventArgs e) { if (!rendering) shell?.Edit(DescriptionBox.Text); }
    private void ClearSearch(object sender, RoutedEventArgs e) { shell?.Search(""); FocusSearch(); }
    private void DiscardDescription(object sender, RoutedEventArgs e) { shell?.Discard(); DescriptionBox.Focus(); }
    private async void SaveDescription(object sender, RoutedEventArgs e)
    {
        if (shell is null) return;
        await shell.SaveAsync();
        DescriptionBox.Focus();
    }
    private async void FolderChanged(object sender, SelectionChangedEventArgs e)
    {
        if (rendering || shell is null || prompt is null || FolderList.SelectedItem is not CatalogFolder folder) return;
        await shell.SelectFolderAsync(folder.Id, prompt);
        Render();
        FolderList.Focus();
    }
    private async void WebsiteChanged(object sender, SelectionChangedEventArgs e)
    {
        if (rendering || shell is null || prompt is null || WebsiteList.SelectedItem is not WebsiteEntry entry) return;
        await shell.SelectEntryAsync(entry.Id, prompt);
        Render();
        WebsiteList.Focus();
    }
}
