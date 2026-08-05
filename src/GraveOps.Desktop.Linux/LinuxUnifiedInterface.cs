using System.Diagnostics;
using System.Globalization;
using System.Text;
using System.Text.Json;

namespace GraveOps.Desktop.Linux;

public sealed class UnifiedInterfacePreferences
{
    public string ThemeName { get; set; } = "GraveOps Default";
    public string Density { get; set; } = "Compact";
    public bool SetupCompleted { get; set; }
    public string SetupMode { get; set; } = "Local Linux — automatic discovery";
    public bool RestoreLastPage { get; set; } = true;
    public string LastNavigation { get; set; } = "DashboardNav";
    public bool SilentRefresh { get; set; } = true;
    public bool ShowFreshness { get; set; } = true;
    public int DashboardLayoutRevision { get; set; }
    public Dictionary<string, List<DashboardCardPreference>>
        DashboardLayouts { get; set; } =
            new(StringComparer.OrdinalIgnoreCase);

    public static UnifiedInterfacePreferences Default =>
        new();
}

public sealed class DashboardCardPreference
{
    public string Key { get; set; } = string.Empty;
    public bool IsVisible { get; set; } = true;
    public bool VisibilityExplicit { get; set; }
    public int Order { get; set; }
}

public sealed class UnifiedInterfacePreferencesStore
{
    private readonly JsonSerializerOptions _json =
        new()
        {
            WriteIndented = true,
            PropertyNameCaseInsensitive = true
        };

    public UnifiedInterfacePreferencesStore(
        string configDirectory)
    {
        Directory.CreateDirectory(configDirectory);
        FilePath = Path.Combine(
            configDirectory,
            "unified-interface.json");
    }

    public string FilePath { get; }

    public UnifiedInterfacePreferences Load()
    {
        try
        {
            if (!File.Exists(FilePath))
                return UnifiedInterfacePreferences.Default;

            var loaded =
                JsonSerializer.Deserialize<
                    UnifiedInterfacePreferences>(
                    File.ReadAllText(FilePath),
                    _json) ??
                UnifiedInterfacePreferences.Default;

            loaded.DashboardLayouts ??=
                new Dictionary<
                    string,
                    List<DashboardCardPreference>>(
                    StringComparer.OrdinalIgnoreCase);

            return loaded;
        }
        catch
        {
            return UnifiedInterfacePreferences.Default;
        }
    }

    public void Save(
        UnifiedInterfacePreferences preferences)
    {
        var temporary = FilePath + ".tmp";

        File.WriteAllText(
            temporary,
            JsonSerializer.Serialize(
                preferences,
                _json));

        File.Move(
            temporary,
            FilePath,
            overwrite: true);
    }
}

public sealed record LinuxThemePalette(
    string Name,
    bool IsDark,
    string Backdrop,
    string Background,
    string Sidebar,
    string Header,
    string Surface,
    string Surface2,
    string Surface3,
    string Input,
    string Console,
    string Border,
    string BorderStrong,
    string Text,
    string Muted,
    string Dim,
    string Accent,
    string AccentHover,
    string AccentTint,
    string Success,
    string Warning,
    string Danger,
    string SuccessTint,
    string WarningTint,
    string DangerTint,
    string Overview,
    string OverviewTint,
    string Jobs,
    string JobsTint,
    string Intelligence,
    string IntelligenceTint,
    string Activity,
    string ActivityTint,
    string Terminal,
    string TerminalTint)
{
    public IReadOnlyDictionary<string, string>
        ResourceColors =>
        new Dictionary<string, string>(
            StringComparer.Ordinal)
        {
            ["BackdropBrush"] = Backdrop,
            ["BackgroundBrush"] = Background,
            ["SidebarBrush"] = Sidebar,
            ["HeaderBrush"] = Header,
            ["SurfaceBrush"] = Surface,
            ["Surface2Brush"] = Surface2,
            ["Surface3Brush"] = Surface3,
            ["InputBrush"] = Input,
            ["ConsoleBrush"] = Console,
            ["BorderBrush"] = Border,
            ["BorderStrongBrush"] = BorderStrong,
            ["TextBrush"] = Text,
            ["MutedBrush"] = Muted,
            ["DimBrush"] = Dim,
            ["AccentBrush"] = Accent,
            ["AccentHoverBrush"] = AccentHover,
            ["AccentTintBrush"] = AccentTint,
            ["SuccessBrush"] = Success,
            ["WarnBrush"] = Warning,
            ["DangerBrush"] = Danger,
            ["SuccessTintBrush"] = SuccessTint,
            ["WarnTintBrush"] = WarningTint,
            ["DangerTintBrush"] = DangerTint,
            ["OverviewCommandBrush"] = Overview,
            ["OverviewCommandTintBrush"] = OverviewTint,
            ["JobsCommandBrush"] = Jobs,
            ["JobsCommandTintBrush"] = JobsTint,
            ["IntelligenceCommandBrush"] = Intelligence,
            ["IntelligenceCommandTintBrush"] = IntelligenceTint,
            ["ActivityCommandBrush"] = Activity,
            ["ActivityCommandTintBrush"] = ActivityTint,
            ["TerminalCommandBrush"] = Terminal,
            ["TerminalCommandTintBrush"] = TerminalTint
        };
}

public static class LinuxThemeCatalog
{
    public static IReadOnlyList<LinuxThemePalette>
        All { get; } =
        new[]
        {
            new LinuxThemePalette(
                "GraveOps Default",
                true,
                "#090A0C", "#101214", "#0C0E10", "#0F1113",
                "#171A1E", "#1C2025", "#242930", "#121519",
                "#0C0F12", "#292E35", "#3A414A", "#F2F1EE",
                "#9AA1A9", "#69717A", "#D4AA5C", "#E3B967",
                "#2B2418", "#63CC8B", "#E0B24F", "#E16B75",
                "#14291F", "#2B2517", "#2D181C",
                "#62A7F5", "#16283C",
                "#E6A44C", "#352514",
                "#B58AF4", "#2B1F3C",
                "#56C8D8", "#143239",
                "#67C68A", "#163323"),
            new LinuxThemePalette(
                "Arctic Command",
                true,
                "#181D24", "#222831", "#1D232B", "#1C2229",
                "#2A323C", "#313A45", "#3A4551", "#20262D",
                "#14191F", "#414D5A", "#59697A", "#ECEFF4",
                "#B5C0CC", "#8895A5", "#88C0D0", "#9DD4E2",
                "#233A42", "#A3BE8C", "#EBCB8B", "#BF616A",
                "#263A2A", "#40371F", "#3B232A",
                "#81A1C1", "#263746",
                "#D08770", "#402A23",
                "#B48EAD", "#392B38",
                "#8FBCBB", "#253B3A",
                "#A3BE8C", "#2A3A2A"),
            new LinuxThemePalette(
                "Midnight Metro",
                true,
                "#0D1017", "#151925", "#11151F", "#121722",
                "#1B2130", "#222A3B", "#2B3548", "#111622",
                "#0A0E15", "#303B50", "#4B5A75", "#E6ECFF",
                "#ABB6D6", "#7886AA", "#7AA2F7", "#91B4FF",
                "#1D315A", "#73DACA", "#E0AF68", "#F7768E",
                "#18362F", "#3D301D", "#3E1D28",
                "#7AA2F7", "#1B315B",
                "#E0AF68", "#3B2E1B",
                "#BB9AF7", "#302752",
                "#7DCFFF", "#1D3646",
                "#9ECE6A", "#24371C"),
            new LinuxThemePalette(
                "Mocha Operations",
                true,
                "#11111B", "#1E1E2E", "#181825", "#181825",
                "#242436", "#2B2B40", "#36364E", "#181825",
                "#11111B", "#45475A", "#585B70", "#CDD6F4",
                "#BAC2DE", "#7F849C", "#CBA6F7", "#D8B7FF",
                "#332949", "#A6E3A1", "#F9E2AF", "#F38BA8",
                "#23382A", "#463B25", "#45232E",
                "#89B4FA", "#233452",
                "#FAB387", "#4A2F22",
                "#CBA6F7", "#382A50",
                "#89DCEB", "#223D43",
                "#A6E3A1", "#29402D"),
            new LinuxThemePalette(
                "Violet Crypt",
                true,
                "#100D16", "#18121F", "#140F1B", "#130F19",
                "#211829", "#2A2033", "#352943", "#18111F",
                "#0E0B13", "#443350", "#604A70", "#F8F8F2",
                "#C8BED2", "#9588A3", "#BD93F9", "#D0AEFF",
                "#382552", "#50FA7B", "#F1FA8C", "#FF5555",
                "#173A23", "#45461E", "#4A1A20",
                "#8BE9FD", "#203C48",
                "#FFB86C", "#4B3020",
                "#BD93F9", "#3A2854",
                "#8BE9FD", "#213E49",
                "#50FA7B", "#183C25"),
            new LinuxThemePalette(
                "Ember Terminal",
                true,
                "#151210", "#201B18", "#1B1714", "#1A1613",
                "#29221D", "#322A23", "#3D332A", "#1C1713",
                "#100D0B", "#4A3D32", "#665345", "#F2E5D5",
                "#C6B6A4", "#938271", "#D79955", "#E8AA68",
                "#432E1B", "#B8BB26", "#FABD2F", "#FB4934",
                "#313515", "#493914", "#4A1E17",
                "#83A598", "#293B3B",
                "#FE8019", "#4A2C10",
                "#D3869B", "#432A38",
                "#8EC07C", "#2A3A27",
                "#B8BB26", "#313714"),
            new LinuxThemePalette(
                "Arctic Daybreak",
                false,
                "#D8DEE9", "#ECEFF4", "#E5E9F0", "#E5E9F0",
                "#FFFFFF", "#F4F6F9", "#E8EDF3", "#FFFFFF",
                "#F4F6F9", "#C7D0DB", "#9AAABD", "#2E3440",
                "#4C566A", "#667085", "#5E81AC", "#4C76A6",
                "#DDE7F2", "#3A7D44", "#9A6700", "#B42318",
                "#E3F2E6", "#FFF2CF", "#FCE8E6",
                "#5E81AC", "#DCE8F4",
                "#B35C00", "#F8E5D2",
                "#7E57A5", "#EADFF3",
                "#187D88", "#D9EFF1",
                "#31784A", "#DCEEE2"),
            new LinuxThemePalette(
                "Latte Control",
                false,
                "#DCE0E8", "#EFF1F5", "#E6E9EF", "#E6E9EF",
                "#FFFFFF", "#F4F5F9", "#E8EAF0", "#FFFFFF",
                "#F3F4F8", "#C8CBD5", "#A6ADC8", "#4C4F69",
                "#5C5F77", "#7C7F93", "#8839EF", "#9C4FF7",
                "#EBDDFA", "#40A02B", "#DF8E1D", "#D20F39",
                "#E3F4DE", "#FFF0CF", "#FBE2E7",
                "#1E66F5", "#DCE7FD",
                "#FE640B", "#FDE4D6",
                "#8839EF", "#EBDDFA",
                "#179299", "#D6EFF0",
                "#40A02B", "#DFF0DA"),
            new LinuxThemePalette(
                "Solar Operations",
                false,
                "#EEE8D5", "#FDF6E3", "#F3ECD8", "#F3ECD8",
                "#FFFDF5", "#F7F0DD", "#ECE4CD", "#FFFDF5",
                "#F6EFDC", "#D5CAB0", "#B7AA8C", "#002B36",
                "#365A62", "#657B83", "#268BD2", "#1A77B7",
                "#D8EBF5", "#2AA198", "#B58900", "#DC322F",
                "#DDF2EE", "#FFF0C8", "#FCE1DE",
                "#268BD2", "#D9EBF6",
                "#CB4B16", "#F7E2D8",
                "#6C71C4", "#E5E3F4",
                "#2AA198", "#D9F0EC",
                "#859900", "#E9EDCF"),
            new LinuxThemePalette(
                "Rose Dawn",
                false,
                "#E8E0E0", "#FAF4ED", "#F2E9E1", "#F2E9E1",
                "#FFFDF9", "#F7EFE8", "#EDE3DC", "#FFFDF9",
                "#F5ECE6", "#D7CAC4", "#B8A9A4", "#575279",
                "#6E6A86", "#817C9C", "#D7827E", "#E0938E",
                "#F4DEDA", "#56949F", "#EA9D34", "#B4637A",
                "#DCEDEC", "#FAEBCF", "#F2DCE3",
                "#286983", "#DCE9ED",
                "#EA9D34", "#F7E8CC",
                "#907AA9", "#E9E0EF",
                "#56949F", "#DCECEB",
                "#618774", "#E0EAE4"),
            new LinuxThemePalette(
                "Warm Paper",
                false,
                "#E6D8C4", "#FBF1C7", "#F2E5B9", "#F2E5B9",
                "#FFF9DD", "#F7EBC2", "#EDE0B6", "#FFF9DD",
                "#F5E9BF", "#D4C39A", "#B9A67F", "#3C3836",
                "#504945", "#665C54", "#B57614", "#C47D18",
                "#F1DFC1", "#79740E", "#AF3A03", "#9D0006",
                "#E8E8C5", "#F5E1B4", "#F2D1CC",
                "#076678", "#D4E4E2",
                "#AF3A03", "#F1DDC9",
                "#8F3F71", "#E9D8E2",
                "#427B58", "#D9E7DD",
                "#79740E", "#E5E6C4")
        };

    public static IReadOnlyList<string> Names =>
        All.Select(theme => theme.Name).ToArray();

    public static LinuxThemePalette Find(
        string? name) =>
        All.FirstOrDefault(theme =>
            theme.Name.Equals(
                name,
                StringComparison.OrdinalIgnoreCase)) ??
        All[0];

    public static IReadOnlyList<string> Validate()
    {
        var failures = new List<string>();

        if (All.Select(theme => theme.Name)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Count() != All.Count)
        {
            failures.Add("Theme names are not unique.");
        }

        foreach (var theme in All)
        {
            if (theme.ResourceColors.Count != 33)
            {
                failures.Add(
                    $"{theme.Name}: expected 33 color resources.");
            }

            if (Contrast(theme.Text, theme.Background) < 4.5)
            {
                failures.Add(
                    $"{theme.Name}: primary text/background contrast is below 4.5.");
            }

            if (Contrast(theme.Text, theme.Surface) < 4.5)
            {
                failures.Add(
                    $"{theme.Name}: primary text/surface contrast is below 4.5.");
            }

            if (Contrast(theme.Muted, theme.Surface) < 3.0)
            {
                failures.Add(
                    $"{theme.Name}: muted text/surface contrast is below 3.0.");
            }

            if (Contrast(theme.Text, theme.Console) < 4.5)
            {
                failures.Add(
                    $"{theme.Name}: console text contrast is below 4.5.");
            }
        }

        return failures;
    }

    private static double Contrast(
        string foreground,
        string background)
    {
        var lighter =
            Math.Max(
                Luminance(foreground),
                Luminance(background));
        var darker =
            Math.Min(
                Luminance(foreground),
                Luminance(background));

        return (lighter + 0.05) /
               (darker + 0.05);
    }

    private static double Luminance(
        string color)
    {
        var raw = color.TrimStart('#');

        if (raw.Length == 8)
            raw = raw[2..];

        var red =
            int.Parse(
                raw[..2],
                NumberStyles.HexNumber,
                CultureInfo.InvariantCulture) / 255.0;
        var green =
            int.Parse(
                raw.Substring(2, 2),
                NumberStyles.HexNumber,
                CultureInfo.InvariantCulture) / 255.0;
        var blue =
            int.Parse(
                raw.Substring(4, 2),
                NumberStyles.HexNumber,
                CultureInfo.InvariantCulture) / 255.0;

        static double Linear(double value) =>
            value <= 0.03928
                ? value / 12.92
                : Math.Pow(
                    (value + 0.055) / 1.055,
                    2.4);

        return
            0.2126 * Linear(red) +
            0.7152 * Linear(green) +
            0.0722 * Linear(blue);
    }
}

public sealed record UnifiedDashboardAction(
    string Label,
    string NavigationName,
    string Endpoint = "",
    bool IsPrimary = false,
    string LogSource = "",
    string LogText = "",
    bool IncludeInformationalLogs = false,
    string LogContext = "");

public sealed record UnifiedDashboardRow(
    string Label,
    string Value,
    string Detail = "",
    OpsSeverity Severity = OpsSeverity.Info,
    string SecondaryValue = "");

public sealed record UnifiedDashboardCard(
    string Key,
    string Title,
    string Category,
    string Status,
    OpsSeverity Severity,
    string PrimaryValue,
    string Summary,
    string Detail,
    string ActionLabel,
    string NavigationName,
    string Endpoint,
    string SourceKey,
    bool DefaultVisible)
{
    public IReadOnlyList<string> Facts { get; init; } =
        Array.Empty<string>();

    public IReadOnlyList<UnifiedDashboardRow> Rows { get; init; } =
        Array.Empty<UnifiedDashboardRow>();

    public IReadOnlyList<UnifiedDashboardAction> Actions { get; init; } =
        Array.Empty<UnifiedDashboardAction>();
}

public sealed record UnifiedFileEntry(
    string Name,
    string FullPath,
    string Kind,
    string Size,
    string Modified,
    bool IsDirectory);

public sealed record OperatorScriptDefinition(
    string Key,
    string Name,
    string Description,
    string Command,
    bool IsMutating);

public sealed class LinuxOperatorScriptStore
{
    public IReadOnlyList<OperatorScriptDefinition>
        Scripts { get; } =
        new[]
        {
            new OperatorScriptDefinition(
                "failed-units",
                "Failed systemd units",
                "Lists failed units without changing service state.",
                "systemctl --failed --no-pager",
                false),
            new OperatorScriptDefinition(
                "disk-free",
                "Filesystem capacity",
                "Shows mounted filesystems and free space.",
                "df -hT",
                false),
            new OperatorScriptDefinition(
                "docker-fleet",
                "Docker fleet",
                "Shows container names, images, state and status.",
                "docker ps -a --format 'table {{.Names}}\\t{{.Image}}\\t{{.State}}\\t{{.Status}}'",
                false),
            new OperatorScriptDefinition(
                "warning-journal",
                "Warning journal",
                "Shows the newest warning-or-higher journal entries.",
                "journalctl -p warning -n 100 --no-pager",
                false),
            new OperatorScriptDefinition(
                "network-listeners",
                "Listening sockets",
                "Shows TCP and UDP listeners with owning processes.",
                "ss -lntup",
                false)
        };
}

public sealed record LinuxParityItem(
    string Capability,
    string WindowsReference,
    string LinuxImplementation,
    string Classification,
    string Evidence);

public static class LinuxParityCatalog
{
    public static IReadOnlyList<LinuxParityItem>
        Items { get; } =
        new[]
        {
            new LinuxParityItem(
                "Safe Mode",
                "Global mutation guard",
                "Native Linux guarded service, Docker and Plex actions",
                "Parity",
                "Existing Linux Safe Mode remains authoritative."),
            new LinuxParityItem(
                "Maintenance Mode",
                "Suppress expected alerts",
                "Timed maintenance state with activity history",
                "Parity",
                "Existing Linux control-plane state is retained."),
            new LinuxParityItem(
                "Lifecycle",
                "End-to-end media workflow visibility",
                "Dependency-aware Linux lifecycle with readiness, active items and guided remediation",
                "Superseded",
                "The Linux lifecycle uses verified owners and upstream-first remediation."),
            new LinuxParityItem(
                "History & Incidents",
                "Activity, transitions and incident replay",
                "Classified Linux history with duplicate collapse, navigation suppression and replay evidence",
                "Superseded",
                "Raw source events remain retained while meaningful views stay compact."),
            new LinuxParityItem(
                "Servers",
                "Local and remote target profiles",
                "Native local provider plus fingerprint-pinned remote Linux profiles and Secret Service credentials",
                "Linux-native equivalent",
                "Local targets do not loop through SSH and remote credential fields follow authentication type."),
            new LinuxParityItem(
                "Media Hub",
                "Application fleet and launcher ownership",
                "Verified identity registry, provider-neutral product cards and multi-instance ownership",
                "Superseded",
                "Unknown verified applications remain visible through generic fallback cards."),
            new LinuxParityItem(
                "Command palette",
                "Search pages, applications and actions",
                "Ctrl+K navigation with detected-application visibility and Linux operator destinations",
                "Parity",
                "Unavailable application pages are omitted until their identity is detected."),
            new LinuxParityItem(
                "Application catalog",
                "Plex, Jellyfin, Emby, Tautulli, Kometa, Arr, requests, downloads, processing, Pi-hole and Docker",
                "Linux identity catalog plus provider adapters and generic verified-application fallback",
                "Superseded",
                "Cards and workspaces are capability-driven rather than limited to the developer's installed stack."),
            new LinuxParityItem(
                "Dashboard",
                "Card-based overview",
                "Provider-neutral customizable capability cards",
                "Superseded",
                "Linux cards include verified identity and multi-instance context."),
            new LinuxParityItem(
                "Overview",
                "Global overview drawer",
                "Linux Overview drawer plus provider-neutral Dashboard",
                "Superseded",
                "Current Linux capture and policy state remain canonical."),
            new LinuxParityItem(
                "Jobs",
                "Background operation drawer",
                "Persistent Linux control-plane jobs drawer",
                "Parity",
                "Running jobs survive page navigation."),
            new LinuxParityItem(
                "Intelligence",
                "Root cause and dependency analysis",
                "Linux native host, storage, service, Docker and application analysis",
                "Superseded",
                "Linux signal-quality suppression and identity ownership are newer."),
            new LinuxParityItem(
                "Activity",
                "Notifications and operator actions",
                "Linux activity drawer plus classified History",
                "Superseded",
                "Routine navigation suppression and duplicate collapse are retained."),
            new LinuxParityItem(
                "Terminal",
                "PowerShell, CMD and SSH",
                "Linux local terminal and fingerprint-aware SSH/SFTP handoff",
                "Linux-native equivalent",
                "Uses the active Linux host profile."),
            new LinuxParityItem(
                "Application discovery",
                "Windows and remote Linux discovery",
                "Verified Linux identity registry with API fingerprinting",
                "Superseded",
                "Ports are hints only and multiple instances retain stable keys."),
            new LinuxParityItem(
                "Media servers",
                "Plex, Jellyfin and Emby catalog",
                "Provider-neutral media-server cards and generic verified workspace",
                "Parity",
                "Plex keeps richer Linux session telemetry; Jellyfin and Emby use adapters/fallback."),
            new LinuxParityItem(
                "Arr applications",
                "Dedicated product pages",
                "Shared version-aware multi-instance Arr workspace",
                "Superseded",
                "Sonarr v5/v3, Radarr v3 and v1-family negotiation are retained."),
            new LinuxParityItem(
                "Download clients",
                "SABnzbd and qBittorrent workspaces",
                "Protected local API telemetry and dense client-specific tables",
                "Superseded",
                "Credentials remain inside the target host/container."),
            new LinuxParityItem(
                "Processing applications",
                "Dedicated catalog pages",
                "Identity-driven generic workspace plus Recyclarr and Docker drilldown",
                "Linux-native equivalent",
                "Unknown verified products remain visible through fallback cards."),
            new LinuxParityItem(
                "Pi-hole",
                "DNS state, query statistics, gravity and guarded controls",
                "Native Linux Pi-hole CLI workspace with read-only capture and Safe-Mode controls",
                "Linux-native equivalent",
                "Uses the active local or fingerprint-pinned remote Linux profile without hard-coded host details."),
            new LinuxParityItem(
                "Services",
                "Windows service inventory and actions",
                "Native systemd inventory and confirmation-gated actions",
                "Linux-native equivalent",
                "Unit-file state and journal evidence are Linux-native."),
            new LinuxParityItem(
                "Docker",
                "Container inventory and logs",
                "Compose ownership, cleaned/raw logs and guarded actions",
                "Superseded",
                "Newer Linux log and identity work is preserved."),
            new LinuxParityItem(
                "Storage",
                "Drive and mount health",
                "Policy-aware Linux filesystem and mount health",
                "Linux-native equivalent",
                "Linux mount identity and custom threshold policies are retained."),
            new LinuxParityItem(
                "Logs",
                "Central log viewer",
                "Grouped journal evidence with source/severity/time filters",
                "Superseded",
                "Benign portal noise remains informational and raw evidence is retained."),
            new LinuxParityItem(
                "Backups",
                "Provider-neutral readiness",
                "systemd schedule, artifact and restore-readiness projection",
                "Parity",
                "No provider path or schedule is hard-coded."),
            new LinuxParityItem(
                "Files / SFTP",
                "Remote file browser",
                "Local browser plus active-profile SFTP handoff",
                "Linux-native equivalent",
                "No embedded credentials are stored."),
            new LinuxParityItem(
                "Script Library",
                "Saved scripts and commands",
                "Curated read-only Linux operator scripts",
                "Linux-native equivalent",
                "Mutating scripts remain Safe-Mode and confirmation gated."),
            new LinuxParityItem(
                "Update Center",
                "Read-only update inventory",
                "Manual apt, Flatpak, Docker and .NET inventory",
                "Linux-native equivalent",
                "No package is installed or upgraded automatically."),
            new LinuxParityItem(
                "Setup Assistant",
                "Guided host and integration setup",
                "First-launch Express Setup with six Linux setup modes",
                "Parity",
                "Discovery is previewed before anything is saved."),
            new LinuxParityItem(
                "Profile export",
                "Configuration export without credentials",
                "Redacted Linux profile and identity export",
                "Parity",
                "Passwords, API keys, tokens and private keys are excluded."),
            new LinuxParityItem(
                "Themes and density",
                "Windows visual preferences",
                "Eleven complete Linux themes and compact/comfortable density",
                "Superseded",
                "All themes define full surface and state resources."),
            new LinuxParityItem(
                "Settings",
                "Operator, appearance, setup and version preferences",
                "Linux operator defaults plus interface, themes, Dashboard, setup, paths, diagnostics and version state",
                "Superseded",
                "Existing Linux safety and policy controls remain intact while new interface settings are modular."),
            new LinuxParityItem(
                "Diagnostics",
                "Support and validation tools",
                "Redacted diagnostic bundle plus parity and source-contract audits",
                "Superseded",
                "Linux already exports native host and provider evidence.")
        };

    public static IReadOnlyList<string> Validate()
    {
        var requiredCapabilities =
            new[]
            {
                "Safe Mode",
                "Maintenance Mode",
                "Lifecycle",
                "History & Incidents",
                "Servers",
                "Media Hub",
                "Command palette",
                "Application catalog",
                "Dashboard",
                "Overview",
                "Jobs",
                "Intelligence",
                "Activity",
                "Terminal",
                "Application discovery",
                "Media servers",
                "Arr applications",
                "Download clients",
                "Processing applications",
                "Pi-hole",
                "Services",
                "Docker",
                "Storage",
                "Logs",
                "Backups",
                "Files / SFTP",
                "Script Library",
                "Update Center",
                "Setup Assistant",
                "Profile export",
                "Themes and density",
                "Settings",
                "Diagnostics"
            };

        var allowed =
            new HashSet<string>(
                new[]
                {
                    "Parity",
                    "Superseded",
                    "Linux-native equivalent",
                    "Platform-specific"
                },
                StringComparer.OrdinalIgnoreCase);

        var failures = new List<string>();

        foreach (var capability in requiredCapabilities)
        {
            if (!Items.Any(item =>
                    item.Capability.Equals(
                        capability,
                        StringComparison.OrdinalIgnoreCase)))
            {
                failures.Add(
                    $"{capability}: required Windows parity capability is absent.");
            }
        }

        foreach (var item in Items)
        {
            if (!allowed.Contains(item.Classification))
            {
                failures.Add(
                    $"{item.Capability}: unclassified parity state.");
            }

            if (string.IsNullOrWhiteSpace(item.Evidence))
            {
                failures.Add(
                    $"{item.Capability}: no parity evidence.");
            }
        }

        return failures;
    }
}

public static class LinuxReadOnlyUpdateInventory
{
    public static async Task<string> CaptureAsync(
        CancellationToken cancellationToken = default)
    {
        var sections =
            new List<string>();

        sections.Add(
            await RunSectionAsync(
                "APT UPGRADABLE",
                "bash",
                new[]
                {
                    "-lc",
                    "apt list --upgradable 2>/dev/null | sed -n '1,80p'"
                },
                cancellationToken));

        sections.Add(
            await RunSectionAsync(
                "FLATPAK UPDATES",
                "bash",
                new[]
                {
                    "-lc",
                    "command -v flatpak >/dev/null && flatpak remote-ls --updates 2>/dev/null | sed -n '1,80p' || echo 'Flatpak unavailable or no updates reported.'"
                },
                cancellationToken));

        sections.Add(
            await RunSectionAsync(
                "DOCKER IMAGES",
                "bash",
                new[]
                {
                    "-lc",
                    "command -v docker >/dev/null && docker images --format '{{.Repository}}:{{.Tag}}  {{.ID}}  {{.CreatedSince}}' | sed -n '1,80p' || echo 'Docker unavailable.'"
                },
                cancellationToken));

        sections.Add(
            await RunSectionAsync(
                ".NET",
                "dotnet",
                new[]
                {
                    "--info"
                },
                cancellationToken));

        return string.Join(
            Environment.NewLine +
            Environment.NewLine,
            sections);
    }

    private static async Task<string> RunSectionAsync(
        string title,
        string executable,
        IReadOnlyList<string> arguments,
        CancellationToken cancellationToken)
    {
        try
        {
            using var process =
                new Process
                {
                    StartInfo =
                        new ProcessStartInfo
                        {
                            FileName = executable,
                            RedirectStandardOutput = true,
                            RedirectStandardError = true,
                            UseShellExecute = false,
                            CreateNoWindow = true
                        }
                };

            foreach (var argument in arguments)
                process.StartInfo.ArgumentList.Add(argument);

            process.Start();

            var stdout =
                process.StandardOutput.ReadToEndAsync(
                    cancellationToken);
            var stderr =
                process.StandardError.ReadToEndAsync(
                    cancellationToken);

            await process.WaitForExitAsync(
                cancellationToken);

            var output =
                (await stdout).Trim();
            var error =
                (await stderr).Trim();

            return
                $"=== {title} ===" +
                Environment.NewLine +
                (string.IsNullOrWhiteSpace(output)
                    ? string.IsNullOrWhiteSpace(error)
                        ? "No rows reported."
                        : error
                    : output);
        }
        catch (Exception exception)
        {
            return
                $"=== {title} ===" +
                Environment.NewLine +
                $"Unavailable: {exception.Message}";
        }
    }
}
