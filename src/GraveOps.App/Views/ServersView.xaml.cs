using System.Windows;
using System.Windows.Controls;
using GraveOps.App.Models;
using GraveOps.App.Services.Hosts;
using Microsoft.Win32;
using MessageBox = GraveOps.App.Windows.GraveOpsMessageBox;

namespace GraveOps.App.Views;

public partial class ServersView : UserControl
{
    private ServerProfile? _editing;
    private Services.AppServices S => App.Services;

    public ServersView()
    {
        InitializeComponent();
        ConnectionCombo.SelectedIndex = 2;
        RoleCombo.SelectedIndex = 0;
        AuthCombo.SelectedIndex = 0;
        Reload();
        UpdateConnectionUi();
    }

    private void Reload()
    {
        ServerList.ItemsSource = null;
        ServerList.ItemsSource = S.Config.Current.Servers;
    }

    private void ServerList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (ServerList.SelectedItem is not ServerProfile p)
            return;

        _editing = p;
        NameBox.Text = p.Name;
        HostBox.Text = p.Host;
        UserBox.Text = p.Username;
        PortBox.Text = p.Port.ToString();
        FingerprintBox.Text = p.HostKeyFingerprint;
        KeyPathBox.Text = p.PrivateKeyPath;
        RoleCombo.Text = p.Role;
        AuthCombo.SelectedIndex = p.AuthType == SshAuthType.Password ? 0 : 1;
        ConnectionCombo.SelectedIndex = p.ConnectionKind switch
        {
            HostConnectionKind.LocalWindows => 0,
            HostConnectionKind.RemoteWindows => 1,
            _ => 2
        };
        PasswordBox.Password = "";
        KeyPassBox.Password = "";
        UpdateConnectionUi();
        var connection = p.ConnectionKind switch
        {
            HostConnectionKind.LocalWindows => "Native local Windows host. No remote credentials are required.",
            HostConnectionKind.RemoteWindows => $"Remote Windows host over PowerShell remoting{(string.IsNullOrWhiteSpace(p.DetectedOperatingSystem) ? "." : $" | {p.DetectedOperatingSystem}")}",
            _ => $"Remote Linux host over SSH{(string.IsNullOrWhiteSpace(p.DetectedOperatingSystem) ? "." : $" | {p.DetectedOperatingSystem}")}"
        };
        var discovery = p.LastIntegrationDiscoveryUtc is { } last
            ? $"\nLast verified integration discovery: {last.ToLocalTime():g}\n{p.IntegrationDiscoverySummary}"
            : "\nIntegrations have not yet been verified by GraveOps discovery.";
        StatusText.Text = connection + discovery;
    }

    private void New_Click(object sender, RoutedEventArgs e)
    {
        _editing = new ServerProfile { ConnectionKind = HostConnectionKind.RemoteLinux };
        ServerList.SelectedItem = null;
        NameBox.Text = "";
        HostBox.Text = "";
        UserBox.Text = "";
        PortBox.Text = "22";
        FingerprintBox.Text = "";
        KeyPathBox.Text = "";
        PasswordBox.Password = "";
        KeyPassBox.Password = "";
        ConnectionCombo.SelectedIndex = 2;
        RoleCombo.SelectedIndex = 0;
        AuthCombo.SelectedIndex = 0;
        UpdateConnectionUi();
        StatusText.Text = "New host profile.";
    }

    private void Delete_Click(object sender, RoutedEventArgs e)
    {
        if (ServerList.SelectedItem is not ServerProfile p)
            return;

        if (MessageBox.Show(
                $"Delete host profile '{p.Name}' and any stored GraveOps credentials?",
                "Delete host",
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning) != MessageBoxResult.Yes)
            return;

        S.Config.Current.Servers.RemoveAll(x => x.Id == p.Id);
        S.Config.Current.Applications.RemoveAll(x => x.ServerId == p.Id);
        S.Credentials.DeleteSecret(p.PasswordCredentialTarget);
        S.Credentials.DeleteSecret(p.KeyPassphraseCredentialTarget);
        if (S.Config.Current.SelectedServerId == p.Id)
            S.Config.Current.SelectedServerId = S.Config.Current.Servers.FirstOrDefault()?.Id;
        S.Config.Save();
        if (Window.GetWindow(this) is GraveOps.App.MainWindow main)
            main.RefreshEnvironmentChrome();
        _editing = null;
        Reload();
        New_Click(sender, e);
    }

    private void Discover_Click(object sender, RoutedEventArgs e)
    {
        var w = new DiscoveryWindow { Owner = Window.GetWindow(this) };
        w.ShowDialog();
    }

    private void ConnectionCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (SshDetailsPanel is null)
            return;
        UpdateConnectionUi();
    }

    private void UpdateConnectionUi()
    {
        if (SshDetailsPanel is null)
            return;

        var local = ConnectionCombo.SelectedIndex == 0;
        var remoteWindows = ConnectionCombo.SelectedIndex == 1;
        var remoteLinux = ConnectionCombo.SelectedIndex == 2;

        SshDetailsPanel.Visibility = local ? Visibility.Collapsed : Visibility.Visible;
        HostBox.IsReadOnly = local;
        UserBox.IsReadOnly = local;
        PortBox.IsReadOnly = local;

        AuthCombo.IsEnabled = remoteLinux;
        KeyPanel.Visibility = remoteLinux && AuthCombo.SelectedIndex == 1 ? Visibility.Visible : Visibility.Collapsed;
        PasswordPanel.Visibility = local ? Visibility.Collapsed : Visibility.Visible;
        FingerprintBox.Visibility = remoteLinux ? Visibility.Visible : Visibility.Collapsed;
        PortLabelText.Text = remoteWindows ? "WinRM port" : remoteLinux ? "SSH port" : "Port";
        PasswordLabelText.Text = remoteWindows ? "Windows password" : "SSH password";

        if (local)
        {
            if (string.IsNullOrWhiteSpace(NameBox.Text) || NameBox.Text == "New Server")
                NameBox.Text = $"{Environment.MachineName} (Local)";
            HostBox.Text = "127.0.0.1";
            UserBox.Text = Environment.UserName;
            PortBox.Text = "0";
            RoleCombo.Text = "Windows";
        }
        else if (remoteWindows)
        {
            if (PortBox.Text is "0" or "22")
                PortBox.Text = "5985";
            RoleCombo.Text = "Windows";
            AuthCombo.SelectedIndex = 0;
        }
        else
        {
            if (PortBox.Text is "0" or "5985" or "5986")
                PortBox.Text = "22";
            if (RoleCombo.Text == "Windows")
                RoleCombo.SelectedIndex = 0;
        }
    }

    private void AuthCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (PasswordPanel is null)
            return;
        var remoteLinux = ConnectionCombo.SelectedIndex == 2;
        var key = remoteLinux && AuthCombo.SelectedIndex == 1;
        PasswordPanel.Visibility = key || ConnectionCombo.SelectedIndex == 0 ? Visibility.Collapsed : Visibility.Visible;
        KeyPanel.Visibility = key ? Visibility.Visible : Visibility.Collapsed;
    }

    private void BrowseKey_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new OpenFileDialog
        {
            Title = "Select SSH private key",
            Filter = "Private keys|id_*;*.pem;*.key|All files|*.*"
        };
        if (dlg.ShowDialog() == true)
            KeyPathBox.Text = dlg.FileName;
    }

    private ServerProfile BuildFromForm()
    {
        var p = _editing ??= new ServerProfile();
        var local = ConnectionCombo.SelectedIndex == 0;
        var remoteWindows = ConnectionCombo.SelectedIndex == 1;

        p.ConnectionKind = local
            ? HostConnectionKind.LocalWindows
            : remoteWindows
                ? HostConnectionKind.RemoteWindows
                : HostConnectionKind.RemoteLinux;
        p.Name = NameBox.Text.Trim();
        p.Host = local ? "127.0.0.1" : HostBox.Text.Trim();
        p.Username = local ? Environment.UserName : UserBox.Text.Trim();
        p.Port = local ? 0 : ParseRemotePort(remoteWindows);
        p.AuthType = !remoteWindows && AuthCombo.SelectedIndex == 1 ? SshAuthType.PrivateKey : SshAuthType.Password;
        p.PrivateKeyPath = local || remoteWindows ? "" : KeyPathBox.Text.Trim();
        p.Role = local || remoteWindows ? "Windows" : ((RoleCombo.SelectedItem as ComboBoxItem)?.Content?.ToString() ?? RoleCombo.Text);
        p.HostKeyFingerprint = local || remoteWindows ? "" : FingerprintBox.Text.Trim();
        return p;
    }

    private int ParseRemotePort(bool remoteWindows)
    {
        if (!int.TryParse(PortBox.Text, out var port) || port is < 1 or > 65535)
            throw new InvalidOperationException("Remote port must be between 1 and 65535.");
        if (remoteWindows && port is not 5985 and not 5986)
            throw new InvalidOperationException("Remote Windows currently supports WinRM HTTP 5985 or HTTPS 5986.");
        return port;
    }

    private void Save_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var p = BuildFromForm();
            var isNew = !S.Config.Current.Servers.Any(x => x.Id == p.Id);

            if (string.IsNullOrWhiteSpace(p.Name))
                throw new InvalidOperationException("Friendly name is required.");
            if (p.ConnectionKind != HostConnectionKind.LocalWindows &&
                (string.IsNullOrWhiteSpace(p.Host) || string.IsNullOrWhiteSpace(p.Username)))
                throw new InvalidOperationException("Remote hosts require a host/IP and username.");

            if (p.ConnectionKind == HostConnectionKind.LocalWindows)
            {
                var probe = S.Hosts.Resolve(p).ProbeAsync(p).GetAwaiter().GetResult();
                p.DetectedOperatingSystem = probe.OperatingSystem;
                p.EnabledModules.RemoveAll(x => x.Equals("RemoteLinux", StringComparison.OrdinalIgnoreCase));
                AddModule(p, "LocalWindows");
                AddModule(p, "Storage");
                AddModule(p, "LocalHttp");
                if (probe.Capabilities.HasFlag(HostCapability.Docker)) AddModule(p, "Docker");
                S.Credentials.DeleteSecret(p.PasswordCredentialTarget);
                S.Credentials.DeleteSecret(p.KeyPassphraseCredentialTarget);
            }
            else if (p.ConnectionKind == HostConnectionKind.RemoteWindows)
            {
                if (PasswordBox.Password.Length > 0)
                    S.Credentials.SaveSecret(p.PasswordCredentialTarget, p.Username, PasswordBox.Password);
                S.Credentials.DeleteSecret(p.KeyPassphraseCredentialTarget);
                p.EnabledModules.RemoveAll(x => x.Equals("RemoteLinux", StringComparison.OrdinalIgnoreCase));
                AddModule(p, "RemoteWindows");
                AddModule(p, "PowerShell");
                AddModule(p, "Storage");
                AddModule(p, "LocalHttp");
            }
            else
            {
                if (p.AuthType == SshAuthType.Password && PasswordBox.Password.Length > 0)
                    S.Credentials.SaveSecret(p.PasswordCredentialTarget, p.Username, PasswordBox.Password);
                if (p.AuthType == SshAuthType.PrivateKey && KeyPassBox.Password.Length > 0)
                    S.Credentials.SaveSecret(p.KeyPassphraseCredentialTarget, p.Username, KeyPassBox.Password);
            }

            if (isNew)
                S.Config.Current.Servers.Add(p);
            if (S.Config.Current.SelectedServerId is null)
                S.Config.Current.SelectedServerId = p.Id;

            S.Config.Save();
            _editing = p;
            Reload();
            ServerList.SelectedItem = p;
            PasswordBox.Password = "";
            KeyPassBox.Password = "";
            StatusText.Text = p.ConnectionKind switch
            {
                HostConnectionKind.LocalWindows => "Saved native Windows host. No remote credentials were created.",
                HostConnectionKind.RemoteWindows => "Saved remote Windows host. The password, if provided, was written to Windows Credential Manager.",
                _ => "Saved remote Linux host. Secrets, if provided, were written to Windows Credential Manager."
            };
        }
        catch (Exception ex)
        {
            StatusText.Text = ex.Message;
        }
    }

    private async void Test_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var p = BuildFromForm();
            if (p.ConnectionKind == HostConnectionKind.LocalWindows)
            {
                StatusText.Text = "Inspecting this Windows PC natively...";
                var nativeResult = await S.Hosts.Resolve(p).ProbeAsync(p);
                StatusText.Text = $"Native host ready. {nativeResult.HostName} | {nativeResult.OperatingSystem} | {nativeResult.Architecture}";
                return;
            }

            if (p.ConnectionKind == HostConnectionKind.RemoteWindows)
            {
                if (PasswordBox.Password.Length > 0)
                    S.Credentials.SaveSecret(p.PasswordCredentialTarget, p.Username, PasswordBox.Password);

                StatusText.Text = "Testing PowerShell remoting...";
                var remoteWindowsResult = await S.Hosts.Resolve(p).ProbeAsync(p);
                p.DetectedOperatingSystem = remoteWindowsResult.OperatingSystem;
                StatusText.Text = $"Remote Windows ready. {remoteWindowsResult.HostName} | {remoteWindowsResult.OperatingSystem} | {remoteWindowsResult.Architecture}";
                return;
            }

            if (p.AuthType == SshAuthType.Password && PasswordBox.Password.Length > 0)
                S.Credentials.SaveSecret(p.PasswordCredentialTarget, p.Username, PasswordBox.Password);
            if (p.AuthType == SshAuthType.PrivateKey && KeyPassBox.Password.Length > 0)
                S.Credentials.SaveSecret(p.KeyPassphraseCredentialTarget, p.Username, KeyPassBox.Password);

            StatusText.Text = "Testing SSH connection...";
            var result = await S.Ssh.TestAsync(p);
            StatusText.Text = result.Success ? $"{result.Message}\nHost key: {result.Fingerprint}" : result.Message;

            if (result.Success && string.IsNullOrWhiteSpace(p.HostKeyFingerprint) && !string.IsNullOrWhiteSpace(result.Fingerprint))
            {
                if (MessageBox.Show(
                        $"Trust and pin this SSH host-key fingerprint?\n\n{result.Fingerprint}",
                        "Trust first connection",
                        MessageBoxButton.YesNo,
                        MessageBoxImage.Question) == MessageBoxResult.Yes)
                {
                    FingerprintBox.Text = result.Fingerprint;
                    p.HostKeyFingerprint = result.Fingerprint;
                    if (_editing is not null)
                    {
                        _editing.HostKeyFingerprint = result.Fingerprint;
                        S.Config.Save();
                    }
                }
            }
        }
        catch (Exception ex)
        {
            StatusText.Text = ex.Message;
        }
    }

    private async void DetectIntegrations_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var p = BuildFromForm();
            if (!S.Config.Current.Servers.Any(x => x.Id == p.Id))
                throw new InvalidOperationException("Save this host profile before running integration discovery.");

            if (p.ConnectionKind == HostConnectionKind.LocalWindows)
            {
                StatusText.Text = "Verifying local Windows integrations...";
                var result = await S.WindowsDiscovery.DiscoverAsync();
                p.DetectedOperatingSystem = result.Host.OperatingSystem;
                p.EnabledModules.RemoveAll(x => x.Equals("Docker", StringComparison.OrdinalIgnoreCase));
                AddModule(p, "LocalWindows");
                AddModule(p, "Storage");
                AddModule(p, "LocalHttp");
                if (result.Host.Capabilities.HasFlag(HostCapability.Docker))
                    AddModule(p, "Docker");

                S.IntegrationAssignments.ApplyVerified(
                    p,
                    result.Integrations,
                    "Native Windows verified discovery");

                StatusText.Text = FormatDiscoveryResult(p, result.Integrations);
            }
            else if (p.ConnectionKind == HostConnectionKind.RemoteWindows)
            {
                StatusText.Text = "Verifying remote Windows host and application identities...";
                if (PasswordBox.Password.Length > 0)
                    S.Credentials.SaveSecret(p.PasswordCredentialTarget, p.Username, PasswordBox.Password);

                var host = await S.Hosts.Resolve(p).ProbeAsync(p);
                p.DetectedOperatingSystem = host.OperatingSystem;
                p.EnabledModules.RemoveAll(x =>
                    x.Equals("RemoteLinux", StringComparison.OrdinalIgnoreCase) ||
                    x.Equals("Systemd", StringComparison.OrdinalIgnoreCase) ||
                    x.Equals("SMART", StringComparison.OrdinalIgnoreCase));
                AddModule(p, "RemoteWindows");
                AddModule(p, "PowerShell");
                AddModule(p, "Storage");
                AddModule(p, "LocalHttp");
                if (host.Capabilities.HasFlag(HostCapability.Docker)) AddModule(p, "Docker");

                var result = await S.WindowsRemoteDiscovery.DiscoverAsync(p, host);
                S.IntegrationAssignments.ApplyVerified(
                    p,
                    result,
                    "Remote Windows verified discovery");

                StatusText.Text = FormatDiscoveryResult(p, result);
            }
            else
            {
                StatusText.Text = "Verifying remote Linux host and application identities...";
                var host = await S.Hosts.Resolve(p).ProbeAsync(p);
                p.DetectedOperatingSystem = host.OperatingSystem;
                p.EnabledModules.RemoveAll(x =>
                    x.Equals("Docker", StringComparison.OrdinalIgnoreCase) ||
                    x.Equals("Systemd", StringComparison.OrdinalIgnoreCase) ||
                    x.Equals("SMART", StringComparison.OrdinalIgnoreCase));
                AddModule(p, "RemoteLinux");
                if (host.Capabilities.HasFlag(HostCapability.Docker)) AddModule(p, "Docker");
                if (host.Capabilities.HasFlag(HostCapability.Systemd)) AddModule(p, "Systemd");
                if (host.Capabilities.HasFlag(HostCapability.Smart)) AddModule(p, "SMART");

                var result = await S.LinuxDiscovery.DiscoverAsync(p, host);
                S.IntegrationAssignments.ApplyVerified(
                    p,
                    result.Integrations,
                    "Remote Linux verified discovery");

                StatusText.Text = FormatDiscoveryResult(p, result.Integrations);
            }

            S.Config.Save();
            S.Activity.Record(
                "Integration discovery completed",
                p.IntegrationDiscoverySummary,
                ActivityLevel.Success,
                serverId: p.Id,
                deepLink: "page:Servers");
            if (Window.GetWindow(this) is GraveOps.App.MainWindow main)
                main.RefreshEnvironmentChrome();
        }
        catch (Exception ex)
        {
            StatusText.Text = ex.Message;
        }
    }

    private static string FormatDiscoveryResult(
        ServerProfile profile,
        IReadOnlyList<DetectedIntegrationOption> integrations)
    {
        if (integrations.Count == 0)
            return $"{profile.IntegrationDiscoverySummary}\nNo supported applications were verified on this host.";

        var lines = integrations.Select(x =>
            $"{x.DisplayName}{(x.Port is { } port ? $" :{port}" : "")} - {x.Evidence}");
        return profile.IntegrationDiscoverySummary + "\n" + string.Join("\n", lines);
    }

    private static void AddModule(ServerProfile profile, string module)
    {
        if (!profile.EnabledModules.Contains(module, StringComparer.OrdinalIgnoreCase))
            profile.EnabledModules.Add(module);
    }
}
