using xKsef.Wpf.Models;

public interface IWindowNavigationService
{
    bool? ShowLoginWindow();
    bool? ShowNewUserWindow();
    TenantCreationDialogResult? ShowNewTenantWindow();
}