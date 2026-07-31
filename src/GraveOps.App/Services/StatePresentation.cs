using System.Windows;
using System.Windows.Media;
using GraveOps.App.Models;

namespace GraveOps.App.Services;

public static class StatePresentation
{
    public static string AppText(AppHealthState health) =>
        health switch
        {
            AppHealthState.Healthy => "Online",
            AppHealthState.Busy => "Busy",
            AppHealthState.Degraded => "Degraded",
            AppHealthState.Stale => "Stale",
            AppHealthState.Offline => "Offline",
            _ => "Unknown"
        };

    public static string PlexText(PlexTelemetry plex)
    {
        var endpointOnline = IsPositive(plex.EndpointState);
        var serviceOnline = IsPositive(plex.ServiceState);

        if (endpointOnline && serviceOnline)
            return "Online";
        if (endpointOnline || serviceOnline)
            return "Degraded";
        if (IsNegative(plex.EndpointState) || IsNegative(plex.ServiceState))
            return "Offline";
        return "Unknown";
    }

    public static Brush BrushFor(AppHealthState health) =>
        health switch
        {
            AppHealthState.Healthy => ResourceAny("GoSuccess", "Success"),
            AppHealthState.Busy => ResourceAny("GoAccent", "Accent"),
            AppHealthState.Degraded => ResourceAny("GoWarning", "Warn", "Warning"),
            AppHealthState.Stale => ResourceAny("GoWarning", "Warn", "Warning"),
            AppHealthState.Offline => ResourceAny("GoDanger", "Danger"),
            _ => ResourceAny("GoTextSecondary", "Muted")
        };

    public static Brush BrushForText(string? state)
    {
        var value = (state ?? "").Trim();

        if (value.Length == 0 ||
            value.Equals("Unknown", StringComparison.OrdinalIgnoreCase) ||
            value.Contains("Not configured", StringComparison.OrdinalIgnoreCase) ||
            value.Contains("Not run", StringComparison.OrdinalIgnoreCase))
            return ResourceAny("GoTextSecondary", "Muted");

        if (value.Contains("protected", StringComparison.OrdinalIgnoreCase) ||
            value.Contains("restricted", StringComparison.OrdinalIgnoreCase))
            return ResourceAny("GoInfo", "Info", "GoTextSecondary");

        if (IsNegative(value))
            return ResourceAny("GoDanger", "Danger");

        if (value.Contains("busy", StringComparison.OrdinalIgnoreCase))
            return ResourceAny("GoAccent", "Accent");

        if (value.Contains("degraded", StringComparison.OrdinalIgnoreCase) ||
            value.Contains("stale", StringComparison.OrdinalIgnoreCase) ||
            value.Contains("checking", StringComparison.OrdinalIgnoreCase) ||
            value.Contains("pending", StringComparison.OrdinalIgnoreCase) ||
            value.Contains("paused", StringComparison.OrdinalIgnoreCase) ||
            value.Contains("warning", StringComparison.OrdinalIgnoreCase) ||
            value.Equals("WARN", StringComparison.OrdinalIgnoreCase))
            return ResourceAny("GoWarning", "Warn", "Warning");

        if (IsPositive(value) ||
            value.Contains("idle", StringComparison.OrdinalIgnoreCase) ||
            value.Contains("download", StringComparison.OrdinalIgnoreCase) ||
            value.Contains("seeding", StringComparison.OrdinalIgnoreCase))
            return ResourceAny("GoSuccess", "Success");

        return ResourceAny("GoTextSecondary", "Muted");
    }

    public static Brush Resource(string key) =>
        key switch
        {
            "Success" => ResourceAny("GoSuccess", "Success"),
            "Danger" => ResourceAny("GoDanger", "Danger"),
            "Warn" => ResourceAny("GoWarning", "Warn", "Warning"),
            "Warning" => ResourceAny("GoWarning", "Warn", "Warning"),
            "Info" => ResourceAny("GoInfo", "Info"),
            "Accent" => ResourceAny("GoAccent", "Accent"),
            "Muted" => ResourceAny("GoTextSecondary", "Muted"),
            _ => ResourceAny(key, "GoTextSecondary", "Muted")
        };

    private static Brush ResourceAny(params string[] keys)
    {
        foreach (var key in keys)
        {
            if (Application.Current?.TryFindResource(key) is Brush brush)
                return brush;
        }
        return Brushes.LightGray;
    }

    private static bool IsPositive(string? value) =>
        !string.IsNullOrWhiteSpace(value) &&
        (value.Contains("online", StringComparison.OrdinalIgnoreCase) ||
         value.Contains("active", StringComparison.OrdinalIgnoreCase) ||
         value.Contains("healthy", StringComparison.OrdinalIgnoreCase) ||
         value.Contains("enabled", StringComparison.OrdinalIgnoreCase) ||
         value.Contains("running", StringComparison.OrdinalIgnoreCase) ||
         value.Equals("OK", StringComparison.OrdinalIgnoreCase) ||
         value.Equals("PASS", StringComparison.OrdinalIgnoreCase));

    private static bool IsNegative(string? value) =>
        !string.IsNullOrWhiteSpace(value) &&
        (value.Contains("offline", StringComparison.OrdinalIgnoreCase) ||
         value.Contains("failed", StringComparison.OrdinalIgnoreCase) ||
         value.Contains("error", StringComparison.OrdinalIgnoreCase) ||
         value.Contains("unreachable", StringComparison.OrdinalIgnoreCase) ||
         value.Equals("FAIL", StringComparison.OrdinalIgnoreCase));
}