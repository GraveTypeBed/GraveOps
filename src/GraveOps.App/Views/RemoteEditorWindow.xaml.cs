using System.Windows;
using GraveOps.App.Models;

namespace GraveOps.App.Views;

public partial class RemoteEditorWindow : Window
{
    private readonly ServerProfile _server;
    private readonly string _path;
    private string _original = "";

    public RemoteEditorWindow(ServerProfile server, string path)
    {
        InitializeComponent();
        _server = server;
        _path = path;
        PathText.Text = $"{server.Name}: {path}";
        Loaded += async (_, _) => await LoadAsync();
    }

    private async Task LoadAsync()
    {
        try
        {
            _original = await App.Services.Sftp.ReadTextAsync(_server, _path);
            Editor.Text = _original;
            StatusText.Text = "Loaded.";
        }
        catch (Exception ex)
        {
            StatusText.Text = ex.Message;
            Editor.IsReadOnly = true;
        }
    }

    private async void Save_Click(object sender, RoutedEventArgs e)
    {
        if (App.Services.Config.Current.Settings.SafeMode)
        {
            GraveOps.App.Windows.GraveOpsDialog.Show(
                this,
                "Safe Mode blocks remote file saves.",
                "GraveOps Safe Mode",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
            return;
        }

        if (Editor.IsReadOnly)
            return;

        if (Editor.Text == _original)
        {
            StatusText.Text = "No changes to save.";
            return;
        }

        var review = new DiffPreviewWindow(_original, Editor.Text) { Owner = this };
        if (review.ShowDialog() != true || !review.Approved)
        {
            StatusText.Text = "Save cancelled.";
            return;
        }

        try
        {
            var root = Path.Combine(
                App.Services.Config.DirectoryPath,
                "file-backups",
                _server.Id.ToString("N"));
            Directory.CreateDirectory(root);

            var safe = Convert.ToHexString(
                System.Security.Cryptography.SHA256.HashData(Encoding.UTF8.GetBytes(_path)))[..16]
                .ToLowerInvariant();
            var backup = Path.Combine(root, $"{safe}-{DateTime.Now:yyyyMMdd-HHmmss}.txt");
            File.WriteAllText(backup, _original, new UTF8Encoding(false));

            await App.Services.Sftp.WriteTextAsync(_server, _path, Editor.Text);
            _original = Editor.Text;
            StatusText.Text = $"Saved. Rollback copy: {backup}";

            App.Services.Activity.Record(
                "Remote file edited",
                $"Remote: {_path}\nRollback: {backup}",
                ActivityLevel.Success,
                serverId: _server.Id,
                deepLink: "page:Files");
        }
        catch (Exception ex)
        {
            StatusText.Text = ex.Message;
            App.Services.Activity.Record(
                "Remote file edit failed",
                $"{_path}: {ex.Message}",
                ActivityLevel.Error,
                serverId: _server.Id,
                deepLink: "page:Files");
        }
    }

    private void Close_Click(object sender, RoutedEventArgs e) => Close();
}
