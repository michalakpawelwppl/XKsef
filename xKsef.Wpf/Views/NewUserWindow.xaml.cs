using System.Windows;
using xKsef.Wpf.ViewModels;

namespace xKsef.Wpf.Views;

public partial class NewUserWindow : Window
{
    public NewUserWindow(NewUserViewModel viewModel)
    {
        InitializeComponent();
        DataContext = viewModel;
    }
}
