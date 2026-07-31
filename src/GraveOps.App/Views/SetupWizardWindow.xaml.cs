using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Controls;
using GraveOps.App.Models;
using GraveOps.App.Services;

namespace GraveOps.App.Views;

public partial class SetupWizardWindow : Window
{
    private AppServices S => App.Services;
    private DiscoveredHost? Selected => HostsList.SelectedItem as DiscoveredHost;
    private readonly ObservableCollection<DetectedIntegrationOption> _localIntegrations = new();
    private LocalWindowsDiscoveryResult? _localResult;

    public bool SavedProfile { get; private set; }

    public SetupWizardWindow()
    {
        InitializeComponent();
        LocalIntegrationList.ItemsSource = _localIntegrations;
        ShowMode("local");
    }

    private void LocalWindowsMode_Click(object sender, RoutedEventArgs e) => ShowMode("local");
    private void RemoteWindowsMode_Click(object sender, RoutedEventArgs e) => ShowMode("remote-windows");
    private void LinuxMode_Click(object sender, RoutedEventArgs e) => ShowMode("linux");

    private void ShowMode(string mode)
    {
        LocalWindowsPanel.Visibility = mode == "local" ? Visibility.Visible : Visibility.Collapsed;
        RemoteWindowsPanel.Visibility = mode == "remote-windows" ? Visibility.Visible : Visibility.Collapsed;
        LinuxPanel.Visibility = mode == "linux" ? Visibility.Visible : Visibility.Collapsed;

        LocalWindowsModeButton.Style = (Style)FindResource(mode == "local" ? "PrimaryButton" : "SecondaryButton");
        RemoteWindowsModeButton.Style = (Style)FindResource(mode == "remote-windows" ? "PrimaryButton" : "SecondaryButton");
        LinuxModeButton.Style = (Style)FindResource(mode == "linux" ? "PrimaryButton" : "SecondaryButton");
    }

    private async void DetectLocal_Click(object sender, RoutedEventArgs e)
    {
        DetectLocalButton.IsEnabled = false;
        SaveLocalButton.IsEnabled = false;
        LocalStatusText.Text = "Inspecting Windows, storage, listeners, processes, Docker and local application endpoints...";

        try
        {
            _localResult = await S.WindowsDiscovery.DiscoverAsync();
            _localIntegrations.Clear();
            foreach (var integration in _localResult.Integrations)
                _localIntegrations.Add(integration);

            LocalIntegrationList.Visibility = _localIntegrations.Count == 0
                ? Visibility.Collapsed
                : Visibility.Visible;
            LocalIntegrationEmptyText.Visibility = _localIntegrations.Count == 0
                ? Visibility.Visible
                : Visibility.Collapsed;

            var host = _localResult.Host;
            LocalHostNameText.Text = host.HostName;
            LocalOsText.Text = host.OperatingSystem;
            LocalArchText.Text = $"{host.Architecture} | uptime {FormatUptime(host.Uptime)}";
            LocalCapabilityText.Text = FormatCapabilities(host.Capabilities);
            LocalStorageText.Text = host.StorageRoots.Count == 0
                ? "No ready volumes were detected."
                : string.Join(Environment.NewLine, host.StorageRoots);

            LocalStatusText.Text = _localIntegrations.Count == 0
                ? "Windows host detection succeeded. No supported media integrations were identified yet; the host can still be saved."
                : $"Detected {_localIntegrations.Count} supported integration(s). Uncheck anything you do not want enabled on this host.";
            SaveLocalButton.IsEnabled = true;
        }
        catch (Exception ex)
        {
            LocalStatusText.Text = ex.Message;
        }
        finally
        {
            DetectLocalButton.IsEnabled = true;
        }
    }

    private void SaveLocal_Click(object sender, RoutedEventArgs e)
    {
        if (_localResult is null)
        {
            LocalStatusText.Text = "Run Detect environment first.";
            return;
        }

        try
        {
            var profile = S.Config.Current.Servers.FirstOrDefault(x =>
                x.ConnectionKind == HostConnectionKind.LocalWindows);

            if (profile is null)
            {
                profile = new ServerProfile
                {
                    ConnectionKind = HostConnectionKind.LocalWindows,
                    Name = $"{Environment.MachineName} (Local)",
                    Host = "127.0.0.1",
                    Username = Environment.UserName,
                    Port = 0,
                    AuthType = SshAuthType.Password,
                    Role = "Windows",
                    UseForDashboard = true
                };
                S.Config.Current.Servers.Add(profile);
            }

            profile.ConnectionKind = HostConnectionKind.LocalWindows;
            profile.Name = $"{Environment.MachineName} (Local)";
            profile.Host = "127.0.0.1";
            profile.Username = Environment.UserName;
            profile.Port = 0;
            profile.Role = "Windows";
            profile.DetectedOperatingSystem = _localResult.Host.OperatingSystem;
            profile.EnabledModules.Clear();

            AddModule(profile, "LocalWindows");
            AddModule(profile, "Storage");
            AddModule(profile, "LocalHttp");
            if (_localResult.Host.Capabilities.HasFlag(HostCapability.Docker))
                AddModule(profile, "Docker");

            S.IntegrationAssignments.ApplyVerified(
                profile,
                _localIntegrations,
                "Native Windows discovery");

            S.Config.Current.SelectedServerId = profile.Id;
            S.Config.Current.Settings.FirstRunCompleted = true;
            S.Config.Save();
            S.Context.Select(profile);
            SavedProfile = true;

            LocalStatusText.Text = $"Saved native Windows host {profile.Name}. Enabled {_localIntegrations.Count(x => x.Enabled)} detected integration(s). No SSH or WinRM credentials were created.";
            S.Activity.Record(
                "Local Windows setup completed",
                $"Saved {profile.Name} with {_localIntegrations.Count(x => x.Enabled)} detected integration(s).",
                ActivityLevel.Success,
                serverId: profile.Id,
                deepLink: "page:Dashboard");
        }
        catch (Exception ex)
        {
            LocalStatusText.Text = ex.Message;
        }
    }

    private async void RemoteWindowsTestSave_Click(object sender, RoutedEventArgs e)
    {
        var port = (RemoteWindowsTransportBox.SelectedItem as ComboBoxItem)?.Tag?.ToString() == "5986" ? 5986 : 5985;
        var profile = new ServerProfile
        {
            ConnectionKind = HostConnectionKind.RemoteWindows,
            Name = RemoteWindowsNameBox.Text.Trim(),
            Host = RemoteWindowsHostBox.Text.Trim(),
            Username = RemoteWindowsUserBox.Text.Trim(),
            Port = port,
            AuthType = SshAuthType.Password,
            Role = "Windows",
            UseForDashboard = true
        };

        if (string.IsNullOrWhiteSpace(profile.Name) ||
            string.IsNullOrWhiteSpace(profile.Host) ||
            string.IsNullOrWhiteSpace(profile.Username))
        {
            RemoteWindowsStatusBox.Text = "Name, host and username are required.";
            return;
        }

        if (RemoteWindowsPasswordBox.Password.Length == 0)
        {
            RemoteWindowsStatusBox.Text = "Enter the Windows password for the initial connection.";
            return;
        }

        RemoteWindowsTestSaveButton.IsEnabled = false;
        try
        {
            S.Credentials.SaveSecret(profile.PasswordCredentialTarget, profile.Username, RemoteWindowsPasswordBox.Password);
            RemoteWindowsStatusBox.Text = "Testing PowerShell remoting and reading Windows capabilities...";
            var hostProbe = await S.Hosts.Resolve(profile).ProbeAsync(profile);
            profile.DetectedOperatingSystem = hostProbe.OperatingSystem;
            AddModule(profile, "RemoteWindows");
            AddModule(profile, "PowerShell");
            AddModule(profile, "Storage");
            AddModule(profile, "LocalHttp");
            if (hostProbe.Capabilities.HasFlag(HostCapability.Docker))
                AddModule(profile, "Docker");

            RemoteWindowsStatusBox.Text = "Windows remoting verified. Discovering applications by process, container and listener identity...";
            var discovery = await S.WindowsRemoteDiscovery.DiscoverAsync(profile, hostProbe);

            S.Config.Current.Servers.Add(profile);
            if (S.Config.Current.SelectedServerId is null)
                S.Config.Current.SelectedServerId = profile.Id;

            S.IntegrationAssignments.ApplyVerified(
                profile,
                discovery,
                "Remote Windows verified discovery");

            S.Config.Current.Settings.FirstRunCompleted = true;
            S.Config.Save();
            S.Context.Select(profile);
            SavedProfile = true;
            RemoteWindowsPasswordBox.Password = "";

            var verifiedNames = discovery.Count == 0
                ? "none"
                : string.Join(", ", discovery.Select(x => x.DisplayName));
            RemoteWindowsStatusBox.Text =
                $"Saved {profile.Name}. Verified integrations: {verifiedNames}.\n" +
                $"Windows: {hostProbe.OperatingSystem}\n" +
                "The credential remains in Windows Credential Manager. GraveOps did not alter WinRM or host trust policy.";

            S.Activity.Record(
                "Remote Windows setup completed",
                $"Saved {profile.Name}; verified {discovery.Count} integration(s).",
                ActivityLevel.Success,
                serverId: profile.Id,
                deepLink: "page:Dashboard");
        }
        catch (Exception ex)
        {
            S.Credentials.DeleteSecret(profile.PasswordCredentialTarget);
            RemoteWindowsStatusBox.Text = ex.Message;
        }
        finally
        {
            RemoteWindowsTestSaveButton.IsEnabled = true;
        }
    }

    private async void Discover_Click(object sender, RoutedEventArgs e)
    {
        DiscoverButton.IsEnabled = false;
        StatusBox.Text = "Scanning the local /24 for common GraveOps services...";
        try
        {
            var progress = new Progress<(int Done, int Total)>(x => StatusBox.Text = $"Scanning LAN: {x.Done}/{x.Total}");
            HostsList.ItemsSource = await S.Discovery.ScanLocal24Async(progress);
            StatusBox.Text = $"Found {HostsList.Items.Count} candidate host(s). Select one to continue.";
        }
        catch (Exception ex) { StatusBox.Text = ex.Message; }
        finally { DiscoverButton.IsEnabled = true; }
    }

    private void Hosts_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (Selected is not { } h) return;
        HostBox.Text = h.Address;
        NameBox.Text = h.Guess.Contains("Pi-hole", StringComparison.OrdinalIgnoreCase)
            ? "Pi-hole"
            : h.OpenPorts.Contains("32400", StringComparison.OrdinalIgnoreCase)
                ? "Media Server"
                : h.Address;
        RoleBox.SelectedIndex = h.Guess.Contains("Pi-hole", StringComparison.OrdinalIgnoreCase) ? 2 : 0;
    }

    private async void TestSave_Click(object sender, RoutedEventArgs e)
    {
        var profile = new ServerProfile
        {
            ConnectionKind = HostConnectionKind.RemoteLinux,
            Name = NameBox.Text.Trim(),
            Host = HostBox.Text.Trim(),
            Username = UserBox.Text.Trim(),
            Port = 22,
            AuthType = SshAuthType.Password,
            Role = (RoleBox.SelectedItem as ComboBoxItem)?.Content?.ToString() ?? "Linux Server"
        };

        if (string.IsNullOrWhiteSpace(profile.Name) || string.IsNullOrWhiteSpace(profile.Host) || string.IsNullOrWhiteSpace(profile.Username))
        {
            StatusBox.Text = "Name, host and username are required.";
            return;
        }
        if (PasswordBox.Password.Length == 0)
        {
            StatusBox.Text = "Enter the SSH password for the initial connection.";
            return;
        }

        TestSaveButton.IsEnabled = false;
        try
        {
            S.Credentials.SaveSecret(profile.PasswordCredentialTarget, profile.Username, PasswordBox.Password);
            StatusBox.Text = "Testing SSH and reading the host key...";
            var test = await S.Ssh.TestAsync(profile);
            if (!test.Success)
            {
                S.Credentials.DeleteSecret(profile.PasswordCredentialTarget);
                StatusBox.Text = test.Message;
                return;
            }

            profile.HostKeyFingerprint = test.Fingerprint;
            var hostProbe = await new GraveOps.App.Services.Hosts.RemoteLinuxHostProvider(S.Ssh).ProbeAsync(profile);
            profile.DetectedOperatingSystem = hostProbe.OperatingSystem;
            profile.EnabledModules.Add("RemoteLinux");
            if (hostProbe.Capabilities.HasFlag(HostCapability.Docker)) profile.EnabledModules.Add("Docker");
            if (hostProbe.Capabilities.HasFlag(HostCapability.Systemd)) profile.EnabledModules.Add("Systemd");
            if (hostProbe.Capabilities.HasFlag(HostCapability.Smart)) profile.EnabledModules.Add("SMART");

            StatusBox.Text = "SSH verified. Verifying Linux applications by process, container and HTTP identity...";
            var discovery = await S.LinuxDiscovery.DiscoverAsync(profile, hostProbe);

            S.Config.Current.Servers.Add(profile);
            if (S.Config.Current.SelectedServerId is null) S.Config.Current.SelectedServerId = profile.Id;
            S.IntegrationAssignments.ApplyVerified(
                profile,
                discovery.Integrations,
                "Remote Linux verified discovery");

            S.Config.Current.Settings.FirstRunCompleted = true;
            S.Config.Save();
            S.Context.Select(profile);
            SavedProfile = true;
            PasswordBox.Password = "";

            var verifiedNames = discovery.Integrations.Count == 0
                ? "none"
                : string.Join(", ", discovery.Integrations.Select(x => x.DisplayName));
            StatusBox.Text = $"Saved {profile.Name}. Verified integrations: {verifiedNames}.\nListening ports observed: {string.Join(", ", discovery.ListeningPorts)}.\nSecrets remain in Windows Credential Manager.";
            S.Activity.Record(
                "Setup Assistant completed",
                $"Saved remote Linux host {profile.Name}; verified {discovery.Integrations.Count} integration(s).",
                ActivityLevel.Success,
                serverId: profile.Id,
                deepLink: "page:Dashboard");
        }
        catch (Exception ex) { StatusBox.Text = ex.ToString(); }
        finally { TestSaveButton.IsEnabled = true; }
    }

    private static void AddModule(ServerProfile profile, string module)
    {
        if (!profile.EnabledModules.Any(x => x.Equals(module, StringComparison.OrdinalIgnoreCase)))
            profile.EnabledModules.Add(module);
    }

    private static string FormatUptime(TimeSpan? uptime)
    {
        if (uptime is not { } value) return "--";
        if (value.TotalDays >= 1) return $"{(int)value.TotalDays}d {value.Hours}h";
        return $"{value.Hours}h {value.Minutes}m";
    }

    private static string FormatCapabilities(HostCapability capabilities)
    {
        var values = Enum.GetValues<HostCapability>()
            .Where(x => x != HostCapability.None && capabilities.HasFlag(x))
            .Select(x => x.ToString());
        return string.Join(" | ", values);
    }

    private void Close_Click(object sender, RoutedEventArgs e) => Close();
}
