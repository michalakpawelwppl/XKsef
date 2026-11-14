using xKsef.Wpf.Models;

public interface IWindowNavigationService
{
    void ShowTenantUsersWindow(Action<string?> currentAccessChanged);
    bool? ShowLoginWindow();
    bool? ShowNewUserWindow();
    TenantCreationDialogResult? ShowNewTenantWindow();
}