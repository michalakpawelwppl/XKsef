using System.Windows;
using xKsef.Wpf.ViewModels;

namespace xKsef.Wpf;

public partial class MainWindow : Window
{
    public MainWindow(MainViewModel viewModel)
    {
        InitializeComponent();
        DataContext = viewModel;
    }
}