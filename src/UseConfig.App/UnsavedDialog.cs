using System.Windows;
using System.Windows.Controls;

namespace UseConfig.App;

internal sealed class UnsavedDialog : Window
{
    public EditChoice Choice { get; private set; } = EditChoice.Cancel;

    public UnsavedDialog(Window owner)
    {
        Owner = owner;
        Title = "Unsaved description";
        Width = 420;
        SizeToContent = SizeToContent.Height;
        ResizeMode = ResizeMode.NoResize;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        SetResourceReference(BackgroundProperty, "SurfaceBrush");
        SetResourceReference(ForegroundProperty, "PrimaryTextBrush");
        SetResourceReference(FontFamilyProperty, "BodyFontFamily");
        var panel = new StackPanel { Margin = new Thickness(24) };
        panel.Children.Add(new TextBlock { Text = "Save changes to Description before continuing?", TextWrapping = TextWrapping.Wrap });
        var actions = new WrapPanel { Margin = new Thickness(0, 20, 0, 0) };
        Button? cancel = null;
        foreach (var choice in new[] { EditChoice.Save, EditChoice.Discard, EditChoice.Cancel })
        {
            var button = new Button { Content = choice.ToString(), Margin = new Thickness(0, 0, 8, 0), IsCancel = choice == EditChoice.Cancel };
            button.Click += (_, _) => { Choice = choice; DialogResult = choice != EditChoice.Cancel; };
            actions.Children.Add(button);
            if (choice == EditChoice.Cancel) cancel = button;
        }
        panel.Children.Add(actions);
        Content = panel;
        Loaded += (_, _) => cancel?.Focus();
    }
}
