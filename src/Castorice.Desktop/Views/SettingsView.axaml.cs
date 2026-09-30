using Avalonia.Controls;
using Avalonia.Interactivity;

namespace Castorice.Desktop.Views;

public partial class SettingsView : UserControl
{
    public SettingsView() => InitializeComponent();

    private void OnAboutClicked(object? sender, RoutedEventArgs e)
    {
        if (TopLevel.GetTopLevel(this) is Window owner)
        {
            _ = new AboutWindow().ShowDialog(owner);
        }
    }
}
