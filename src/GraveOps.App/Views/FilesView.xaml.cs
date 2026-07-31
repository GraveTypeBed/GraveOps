using System.Windows;
using System.Windows.Controls;
using GraveOps.App.Models;
using Microsoft.Win32;
using MessageBox = GraveOps.App.Windows.GraveOpsMessageBox;

namespace GraveOps.App.Views;

public partial class FilesView : UserControl
{
    private Services.AppServices S => App.Services;
    private ServerProfile? Server => S.Context.Current;
    private RemoteFileItem? Selected => FilesGrid.SelectedItem as RemoteFileItem;
    public FilesView()
    {
        InitializeComponent(); TargetText.Text = Server?.Name ?? "No target";
        S.Context.TargetChanged += Context_TargetChanged;
        Unloaded += (_, _) => S.Context.TargetChanged -= Context_TargetChanged;
        Loaded += async (_, _) => await RefreshAsync();
    }
    private void Context_TargetChanged(ServerProfile? p) => Dispatcher.Invoke(async () => { TargetText.Text = p?.Name ?? "No target"; await RefreshAsync(); });
    private async Task RefreshAsync()
    {
        if (Server is not { } p) { StatusText.Text = "No global server target."; return; }
        StatusText.Text = "Loading...";
        try { FilesGrid.ItemsSource = await S.Sftp.ListAsync(p, PathBox.Text.Trim()); StatusText.Text = "Loaded."; }
        catch (Exception ex) { StatusText.Text = ex.Message; }
    }
    private async void Go_Click(object sender, RoutedEventArgs e) => await RefreshAsync();
    private async void Refresh_Click(object sender, RoutedEventArgs e) => await RefreshAsync();
    private async void Up_Click(object sender, RoutedEventArgs e) { var path = PathBox.Text.TrimEnd('/'); var i = path.LastIndexOf('/'); PathBox.Text = i <= 0 ? "/" : path[..i]; await RefreshAsync(); }
    private async void Favorite_Click(object sender, RoutedEventArgs e) { if (sender is Button { Tag: string path }) { PathBox.Text = path; await RefreshAsync(); } }
    private async void FilesGrid_MouseDoubleClick(object sender, System.Windows.Input.MouseButtonEventArgs e) { if (Selected is { IsDirectory: true } d) { PathBox.Text = d.FullPath; await RefreshAsync(); } }
    private async void FilesGrid_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (Selected is not { } item)
        {
            DownloadButton.IsEnabled = false;
            EditButton.IsEnabled = false;
            PreviewPathText.Text = "Select a file";
            PreviewMetaText.Text = "";
            PreviewBox.Text = "";
            return;
        }

        DownloadButton.IsEnabled = !item.IsDirectory;
        EditButton.IsEnabled = !item.IsDirectory;
        PreviewPathText.Text = item.FullPath; PreviewMetaText.Text = $"{item.Type} | {item.DisplaySize} | {item.LastWriteTime:g}";
        if (item.IsDirectory) { PreviewBox.Text = "Directory"; return; }
        if (item.Size > 1024 * 1024) { PreviewBox.Text = "Preview skipped for files larger than 1 MB."; return; }
        if (Server is not { } p) return;
        try
        {
            var text = await S.Sftp.ReadTextAsync(p, item.FullPath);
            PreviewBox.Text = text.Length > 65536 ? text[..65536] + "\n\n[preview truncated]" : text;
        }
        catch { PreviewBox.Text = "Binary, protected, or unreadable file."; }
    }
    private async void Download_Click(object sender, RoutedEventArgs e) { if (Server is not { } p || Selected is not { IsDirectory: false } f) return; var dlg = new SaveFileDialog { FileName = f.Name }; if (dlg.ShowDialog() != true) return; try { await S.Sftp.DownloadAsync(p, f.FullPath, dlg.FileName); StatusText.Text = "Downloaded."; } catch (Exception ex) { StatusText.Text = ex.Message; } }
    private async void Upload_Click(object sender, RoutedEventArgs e) { if (S.Config.Current.Settings.SafeMode) { MessageBox.Show("Safe Mode blocks remote uploads and edits.", "GraveOps Safe Mode"); return; } if (Server is not { } p) return; var dlg = new OpenFileDialog(); if (dlg.ShowDialog() != true) return; var remote = PathBox.Text.TrimEnd('/') + "/" + System.IO.Path.GetFileName(dlg.FileName); try { await S.Sftp.UploadAsync(p, dlg.FileName, remote); StatusText.Text = "Uploaded."; await RefreshAsync(); } catch (Exception ex) { StatusText.Text = ex.Message; } }
    private async void Edit_Click(object sender, RoutedEventArgs e) { if (S.Config.Current.Settings.SafeMode) { MessageBox.Show("Safe Mode blocks remote uploads and edits.", "GraveOps Safe Mode"); return; } if (Server is not { } p || Selected is not { IsDirectory: false } f) return; try { var editor = new RemoteEditorWindow(p, f.FullPath) { Owner = Window.GetWindow(this) }; editor.ShowDialog(); await RefreshAsync(); } catch (Exception ex) { StatusText.Text = ex.Message; } }
    private void TerminalHere_Click(object sender, RoutedEventArgs e)
    {
        if (Server is not { } server)
        {
            StatusText.Text = "No global server target.";
            return;
        }

        var path = Selected is { IsDirectory: true } d ? d.FullPath : PathBox.Text;
        TerminalView.QueueSshHandoff(server, path);
        S.Activity.Record("Terminal handoff", $"Open SSH terminal at {path}", ActivityLevel.Info, serverId: server.Id, deepLink: "page:Terminal");
        StatusText.Text = $"Opening SSH terminal at {path}...";
        S.Navigation.Request("page:Terminal");
    }
}