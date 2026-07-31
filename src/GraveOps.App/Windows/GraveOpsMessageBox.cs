using System.Windows;

namespace GraveOps.App.Windows;

public static class GraveOpsMessageBox
{
    private static Window? Owner
        => Application.Current?.Windows
               .OfType<Window>()
               .FirstOrDefault(x => x.IsActive)
           ?? Application.Current?.MainWindow;

    public static MessageBoxResult Show(string message)
        => GraveOpsDialog.Show(
            Owner,
            message,
            "GraveOps",
            MessageBoxButton.OK,
            MessageBoxImage.None);

    public static MessageBoxResult Show(
        string message,
        string caption)
        => GraveOpsDialog.Show(
            Owner,
            message,
            caption,
            MessageBoxButton.OK,
            MessageBoxImage.None);

    public static MessageBoxResult Show(
        string message,
        string caption,
        MessageBoxButton buttons)
        => GraveOpsDialog.Show(
            Owner,
            message,
            caption,
            buttons,
            MessageBoxImage.None);

    public static MessageBoxResult Show(
        string message,
        string caption,
        MessageBoxButton buttons,
        MessageBoxImage image)
        => GraveOpsDialog.Show(
            Owner,
            message,
            caption,
            buttons,
            image);

    public static MessageBoxResult Show(
        string message,
        string caption,
        MessageBoxButton buttons,
        MessageBoxImage image,
        MessageBoxResult defaultResult)
        => GraveOpsDialog.Show(
            Owner,
            message,
            caption,
            buttons,
            image);

    public static MessageBoxResult Show(
        Window owner,
        string message)
        => GraveOpsDialog.Show(
            owner,
            message,
            "GraveOps",
            MessageBoxButton.OK,
            MessageBoxImage.None);

    public static MessageBoxResult Show(
        Window owner,
        string message,
        string caption)
        => GraveOpsDialog.Show(
            owner,
            message,
            caption,
            MessageBoxButton.OK,
            MessageBoxImage.None);

    public static MessageBoxResult Show(
        Window owner,
        string message,
        string caption,
        MessageBoxButton buttons)
        => GraveOpsDialog.Show(
            owner,
            message,
            caption,
            buttons,
            MessageBoxImage.None);

    public static MessageBoxResult Show(
        Window owner,
        string message,
        string caption,
        MessageBoxButton buttons,
        MessageBoxImage image)
        => GraveOpsDialog.Show(
            owner,
            message,
            caption,
            buttons,
            image);

    public static MessageBoxResult Show(
        Window owner,
        string message,
        string caption,
        MessageBoxButton buttons,
        MessageBoxImage image,
        MessageBoxResult defaultResult)
        => GraveOpsDialog.Show(
            owner,
            message,
            caption,
            buttons,
            image);
}