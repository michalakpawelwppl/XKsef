using System.Windows;
using xKsef.Wpf.ViewModels;

namespace xKsef.Wpf.Views;

public partial class LoginWindow : Window
{

    public LoginWindow(LoginViewModel viewModel)
    {
        InitializeComponent();

        DataContext = viewModel;

        viewModel.LoginSucceeded += OnLoginSucceeded;
    }

    private void OnLoginSucceeded(object? sender, EventArgs e)
    {
        DialogResult = true;
    }
}