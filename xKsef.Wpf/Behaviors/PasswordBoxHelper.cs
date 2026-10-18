using System.Windows;
using System.Windows.Controls;

namespace xKsef.Wpf.Behaviors;

public static class PasswordBoxHelper
{
    public static readonly DependencyProperty BoundPasswordProperty =
        DependencyProperty.RegisterAttached(
            "BoundPassword",
            typeof(string),
            typeof(PasswordBoxHelper),
            new FrameworkPropertyMetadata(
                null,
                FrameworkPropertyMetadataOptions.BindsTwoWayByDefault,
                OnBoundPasswordChanged));

    public static string GetBoundPassword(DependencyObject obj)
    {
        return (string)obj.GetValue(BoundPasswordProperty);
    }

    public static void SetBoundPassword(
        DependencyObject obj,
        string value)
    {
        obj.SetValue(BoundPasswordProperty, value);
    }

    private static void OnBoundPasswordChanged(
    DependencyObject obj,
    DependencyPropertyChangedEventArgs e)
    {
        if (obj is not PasswordBox passwordBox)
            return;

        passwordBox.PasswordChanged -= OnPasswordChanged;

        string newPassword = e.NewValue as string ?? string.Empty;

        if (passwordBox.Password != newPassword)
        {
            passwordBox.Password = newPassword;
        }

        passwordBox.PasswordChanged += OnPasswordChanged;
    }

    private static void OnPasswordChanged(
        object sender,
        RoutedEventArgs e)
    {
        var passwordBox = (PasswordBox)sender;

        SetBoundPassword(
            passwordBox,
            passwordBox.Password);
    }
}