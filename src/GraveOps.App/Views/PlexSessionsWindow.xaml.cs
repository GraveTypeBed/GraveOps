using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using GraveOps.App.Models;
using GraveOps.App.Services;
using GraveOps.App.Windows;

namespace GraveOps.App.Views;

public partial class PlexSessionsWindow : Window
{
    private readonly PlexSessionService _plex =
        new(App.Services);

    private readonly ObservableCollection<PlexSessionRow> _sessions =
        new();

    private readonly DispatcherTimer _timer;
    private bool _refreshing;

    private AppServices S => App.Services;
    private ServerProfile? Server => S.Context.Current;
    private PlexSessionRow? Selected =>
        SessionsGrid.SelectedItem as PlexSessionRow;

    public PlexSessionsWindow()
    {
        InitializeComponent();

        SessionsGrid.ItemsSource = _sessions;

        _timer =
            new DispatcherTimer
            {
                Interval = TimeSpan.FromSeconds(15)
            };

        _timer.Tick += Timer_Tick;

        Loaded += PlexSessionsWindow_Loaded;
        Closed += (_, _) => _timer.Stop();
    }

    private async void PlexSessionsWindow_Loaded(
        object sender,
        RoutedEventArgs e)
    {
        TargetText.Text =
            Server?.Name ?? "No global target";

        UpdateTokenStatus();

        if (Server is not null &&
            _plex.HasToken(Server))
        {
            await RefreshAsync();
        }
        else
        {
            StatusText.Text =
                "Enter the Plex token, then use Save + test.";
        }

        UpdateTimer();
    }

    private void UpdateTokenStatus()
    {
        if (Server is not { } server)
        {
            TokenStatusText.Text =
                "Select a global server target first.";
            ClearTokenButton.IsEnabled = false;
            return;
        }

        var configured =
            _plex.HasToken(server);

        TokenStatusText.Text =
            configured
                ? $"Token saved securely in Windows Credential Manager for {server.Name}. The token is never displayed or written to GraveOps config."
                : $"No session token is saved for {server.Name}. Paste an X-Plex-Token to enable session analytics.";

        ClearTokenButton.IsEnabled = configured;
    }

    private async void Refresh_Click(
        object sender,
        RoutedEventArgs e)
        => await RefreshAsync();

    private async void Timer_Tick(
        object? sender,
        EventArgs e)
        => await RefreshAsync();

    private async Task RefreshAsync()
    {
        if (_refreshing)
            return;

        if (Server is not { } server)
        {
            StatusText.Text =
                "Select a global server target first.";
            return;
        }

        if (!_plex.HasToken(server))
        {
            UpdateTokenStatus();
            StatusText.Text =
                "Plex session token is not configured.";
            return;
        }

        _refreshing = true;
        RefreshButton.IsEnabled = false;

        try
        {
            StatusText.Text =
                "Refreshing Plex sessions...";

            var snapshot =
                await _plex.GetAsync(server);

            Bind(snapshot);

            StatusText.Text =
                $"Updated {snapshot.Timestamp.ToLocalTime():HH:mm:ss} | {snapshot.SessionCount} active session(s).";
        }
        catch (Exception ex)
        {
            StatusText.Text = ex.Message;

            if (!ex.Message.Contains(
                    "token",
                    StringComparison.OrdinalIgnoreCase))
            {
                GraveOpsDialog.Show(
                    this,
                    ex.Message,
                    "Plex session refresh failed",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
            }
        }
        finally
        {
            RefreshButton.IsEnabled = true;
            _refreshing = false;
        }
    }

    private void Bind(
        PlexSessionSnapshot snapshot)
    {
        var selectedId =
            Selected?.SessionId;

        _sessions.Clear();

        foreach (var item in snapshot.Sessions)
            _sessions.Add(item);

        SessionCountText.Text =
            snapshot.SessionCount.ToString();

        DirectPlayText.Text =
            snapshot.DirectPlayCount.ToString();

        DirectStreamText.Text =
            snapshot.DirectStreamCount.ToString();

        TranscodeText.Text =
            snapshot.TranscodeCount.ToString();

        BandwidthText.Text =
            snapshot.TotalBandwidthText;

        ServerVersionText.Text =
            string.IsNullOrWhiteSpace(snapshot.Version)
                ? "--"
                : snapshot.Version;

        if (!string.IsNullOrWhiteSpace(selectedId))
        {
            var match =
                _sessions.FirstOrDefault(
                    x => x.SessionId == selectedId);

            if (match is not null)
                SessionsGrid.SelectedItem = match;
        }

        UpdateSelectedSession();
    }

    private async void SaveToken_Click(
        object sender,
        RoutedEventArgs e)
    {
        if (Server is not { } server)
        {
            GraveOpsDialog.Show(
                this,
                "Select a global server target first.",
                "No target",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
            return;
        }

        var token =
            TokenBox.Password.Trim();

        if (token.Length == 0)
        {
            GraveOpsDialog.Show(
                this,
                "Paste an X-Plex-Token first. The saved value will not be displayed afterward.",
                "Plex token required",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
            return;
        }

        RefreshButton.IsEnabled = false;

        try
        {
            StatusText.Text =
                "Testing Plex token before saving...";

            var snapshot =
                await _plex.TestAndSaveAsync(
                    server,
                    token);

            TokenBox.Clear();
            UpdateTokenStatus();
            Bind(snapshot);

            StatusText.Text =
                $"Plex token verified and saved securely. Loaded {snapshot.SessionCount} active session(s).";
        }
        catch (Exception ex)
        {
            GraveOpsDialog.Show(
                this,
                ex.Message,
                "Plex token test failed",
                MessageBoxButton.OK,
                MessageBoxImage.Error);

            StatusText.Text =
                "Token was not saved because validation failed.";
        }
        finally
        {
            RefreshButton.IsEnabled = true;
        }
    }

    private void ClearToken_Click(
        object sender,
        RoutedEventArgs e)
    {
        if (Server is not { } server)
            return;

        if (GraveOpsDialog.Show(
                this,
                $"Remove the saved Plex session token for {server.Name} from Windows Credential Manager?",
                "Clear Plex token",
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning)
            != MessageBoxResult.Yes)
            return;

        _plex.ClearToken(server);
        TokenBox.Clear();
        _sessions.Clear();
        ResetSummary();
        UpdateTokenStatus();

        StatusText.Text =
            "Saved Plex session token removed.";
    }

    private void SessionsGrid_SelectionChanged(
        object sender,
        SelectionChangedEventArgs e)
        => UpdateSelectedSession();

    private void UpdateSelectedSession()
    {
        if (Selected is not { } item)
        {
            SelectedSessionText.Text =
                "Select a session for client, codec and transcode details.";
            SessionDetailBox.Text = "";
            return;
        }

        SelectedSessionText.Text =
            $"{item.User} | {item.Title} | {item.Decision}";

        SessionDetailBox.Text =
            item.DetailText;
    }

    private void ResetSummary()
    {
        SessionCountText.Text = "--";
        DirectPlayText.Text = "--";
        DirectStreamText.Text = "--";
        TranscodeText.Text = "--";
        BandwidthText.Text = "--";
        ServerVersionText.Text = "--";
        SessionDetailBox.Text = "";
        SelectedSessionText.Text =
            "Select a session for client, codec and transcode details.";
    }

    private void AutoRefresh_Changed(
        object sender,
        RoutedEventArgs e)
        => UpdateTimer();

    private void UpdateTimer()
    {
        if (!IsLoaded)
            return;

        if (AutoRefreshCheck.IsChecked == true)
            _timer.Start();
        else
            _timer.Stop();
    }

    private void Close_Click(
        object sender,
        RoutedEventArgs e)
        => Close();
}