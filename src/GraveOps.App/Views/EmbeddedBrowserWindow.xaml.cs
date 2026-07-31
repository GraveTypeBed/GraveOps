using System.Windows;
using System.Windows.Input;

namespace GraveOps.App.Views;

public partial class EmbeddedBrowserWindow : Window
{
    private readonly string _url;
    public EmbeddedBrowserWindow(string title, string url) { InitializeComponent(); Title = $"GraveOps - {title}"; _url = url; Loaded += async (_, _) => { await Browser.EnsureCoreWebView2Async(); AddressBox.Text = _url; Browser.Source = new Uri(_url); Browser.NavigationCompleted += (_, _) => AddressBox.Text = Browser.Source?.ToString() ?? AddressBox.Text; }; }
    private void Back_Click(object sender, RoutedEventArgs e) { if (Browser.CanGoBack) Browser.GoBack(); }
    private void Go_Click(object sender, RoutedEventArgs e) { if (Uri.TryCreate(AddressBox.Text, UriKind.Absolute, out var u)) Browser.Source = u; }
    private void AddressBox_KeyDown(object sender, KeyEventArgs e) { if (e.Key == Key.Enter) Go_Click(sender, e); }
}
