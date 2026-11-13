using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace xKsef.Wpf.ViewModels;

public partial class KsefTokenViewModel : ObservableObject
{
    [ObservableProperty]
    private string _token = string.Empty;

    [ObservableProperty]
    private bool? _dialogResult;

    [RelayCommand]
    private void Save()
    {
        if (string.IsNullOrWhiteSpace(Token)) return;

        DialogResult = true;
    }

    [RelayCommand]
    private void Cancel()
    {
        DialogResult = false;
    }
}