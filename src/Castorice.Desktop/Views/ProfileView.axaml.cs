using Avalonia.Controls;
using Avalonia.Input;
using Castorice.Desktop.ViewModels;

namespace Castorice.Desktop.Views;

public partial class ProfileView : UserControl
{
    public ProfileView() => InitializeComponent();

    private void OnQueryKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key is not Key.Enter || DataContext is not ProfileViewModel viewModel)
        {
            return;
        }

        e.Handled = true;
        if (viewModel.SearchCommand.CanExecute(null))
        {
            viewModel.SearchCommand.Execute(null);
        }
    }
}
