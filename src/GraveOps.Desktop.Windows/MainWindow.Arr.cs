using System.Diagnostics;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Threading;
using GraveOps.Core.Hosts;
using GraveOps.Core.Telemetry;

namespace GraveOps.Desktop.Windows;

public partial class MainWindow
{
    private static readonly TimeSpan ArrForegroundRefreshInterval =
        TimeSpan.FromSeconds(10);

    private static readonly TimeSpan ArrBackgroundRefreshInterval =
        TimeSpan.FromSeconds(30);

    private static readonly TimeSpan ArrMinimizedRefreshInterval =
        TimeSpan.FromSeconds(60);

    private readonly DispatcherTimer _arrTimer = new()
    {
        Interval = ArrForegroundRefreshInterval
    };

    private readonly Dictionary<string, ArrLiveTelemetrySnapshot> _arrCache =
        new(StringComparer.OrdinalIgnoreCase);

    private readonly Dictionary<string, IntegrationSnapshot?> _arrDiscovery =
        new(StringComparer.OrdinalIgnoreCase);

    private WindowsArrTelemetryService? _arrTelemetry;
    private string _activeArrProduct = "Sonarr";
    private bool _arrBusy;

    private void InitializeArrWorkspace()
    {
        _arrTelemetry = new WindowsArrTelemetryService(_targetSession);

        _arrTimer.Tick += async (_, _) =>
        {
            UpdateArrTimerCadence();
            await RefreshArrTelemetryAsync(showStatus: false);
        };

        Opened += (_, _) =>
        {
            UpdateArrTimerCadence();
            _arrTimer.Start();
        };

        Closed += (_, _) => _arrTimer.Stop();
    }

    private void ActivateWindowsArrWorkspace(string product)
    {
        _activeArrProduct = WindowsArrProductPolicy.Normalize(product);
        UpdateArrTimerCadence();
        _ = LoadAndRefreshArrWorkspaceAsync();
    }

    private async Task LoadAndRefreshArrWorkspaceAsync()
    {
        var target = _targetSession.SelectedTarget;

        if (target is null || _arrTelemetry is null)
            return;

        SetArrProductLabels();
        SetText("ArrTargetText", target.DisplayName);
        RefreshArrDiscoveryEvidence();

        try
        {
            var endpoint = await _arrTelemetry.ResolveEndpointAsync(
                target,
                _activeArrProduct);

            Get<TextBox>("ArrEndpointTextBox").Text = endpoint.AbsoluteUri;
        }
        catch (Exception exception)
        {
            SetText("ArrStatusText", exception.Message);
        }

        Get<TextBox>("ArrApiKeyTextBox").Text = string.Empty;

        var cacheKey = ArrCacheKey(target.Id, _activeArrProduct);

        if (_arrCache.TryGetValue(cacheKey, out var cached))
            ApplyArrSnapshot(cached);
        else
            SetArrLoadingState();

        await RefreshArrTelemetryAsync(showStatus: false);
    }

    private void SetArrProductLabels()
    {
        SetText("ArrProductTitleText", _activeArrProduct);

        SetText(
            "ArrProductSubtitleText",
            _activeArrProduct.Equals(
                "Sonarr",
                StringComparison.OrdinalIgnoreCase)
                ? "Series and episode health, queue state and protected API telemetry."
                : "Movie health, queue state and protected API telemetry.");

        SetText("ArrWorkMetricLabelText", "QUEUE");

        SetText(
            "ArrWorkSectionTitleText",
            _activeArrProduct.Equals(
                "Sonarr",
                StringComparison.OrdinalIgnoreCase)
                ? "Episode queue & health"
                : "Movie queue & health");

        Get<Button>("ArrOpenButton").Content = $"Open {_activeArrProduct}";
    }

    private void SetArrLoadingState()
    {
        SetText("ArrStateMetricText", "CHECKING");
        SetText("ArrVersionMetricText", "--");
        SetText("ArrWorkMetricText", "--");
        SetText("ArrHealthMetricText", "--");
        SetText("ArrInstanceCountText", "Waiting for API status");

        Get<ListBox>("ArrInstanceTelemetryList").ItemsSource =
            Array.Empty<ArrServiceTelemetryRow>();
        Get<ListBox>("ArrInstanceTelemetryList").IsVisible = false;
        Get<Border>("ArrServiceEmptyState").IsVisible = true;

        Get<ListBox>("ArrQueueHealthList").ItemsSource =
            Array.Empty<ArrWorkItemRow>();
        Get<ListBox>("ArrQueueHealthList").IsVisible = false;
        Get<Border>("ArrQueueEmptyState").IsVisible = true;

        SetText(
            "ArrQueueEmptyText",
            "Arr queue and health telemetry is loading.");
        SetText(
            "ArrSecurityText",
            "API keys are stored only in Windows Credential Manager or read transiently from a local config.xml.");
        SetText("ArrStatusText", "Waiting for Arr API telemetry.");
        SetText("ArrFreshnessText", "CHECKING...");
    }

    private void ApplyArrSnapshot(ArrLiveTelemetrySnapshot snapshot)
    {
        SetText("ArrStateMetricText", snapshot.OverallState);
        SetText(
            "ArrVersionMetricText",
            string.IsNullOrWhiteSpace(snapshot.VersionSummary)
                ? "--"
                : snapshot.VersionSummary);
        SetText("ArrWorkMetricText", snapshot.WorkSummary);
        SetText("ArrHealthMetricText", snapshot.HealthSummary);

        Get<ListBox>("ArrInstanceTelemetryList").ItemsSource =
            snapshot.Services;
        Get<ListBox>("ArrInstanceTelemetryList").IsVisible =
            snapshot.Services.Count > 0;
        Get<Border>("ArrServiceEmptyState").IsVisible =
            snapshot.Services.Count == 0;

        SetText(
            "ArrInstanceCountText",
            $"{snapshot.Services.Count} " +
            (snapshot.Services.Count == 1 ? "instance" : "instances"));

        Get<ListBox>("ArrQueueHealthList").ItemsSource =
            snapshot.WorkItems;
        Get<ListBox>("ArrQueueHealthList").IsVisible =
            snapshot.WorkItems.Count > 0;
        Get<Border>("ArrQueueEmptyState").IsVisible =
            snapshot.WorkItems.Count == 0;

        SetText(
            "ArrQueueEmptyText",
            "No queued work or active health issue was returned.");

        var service = snapshot.Services.FirstOrDefault();

        SetText(
            "ArrSecurityText",
            service?.Access ?? "Protected API key source unavailable.");

        SetText(
            "ArrStatusText",
            service is null
                ? "Arr telemetry refreshed."
                : $"{service.Service} · {service.State} · {service.Detail}");

        SetText(
            "ArrFreshnessText",
            $"LIVE · {ArrCadenceLabel()} · updated " +
            $"{snapshot.CapturedAt.ToLocalTime():h:mm:ss tt}");
    }

    private async Task RefreshArrTelemetryAsync(bool showStatus)
    {
        if (_arrBusy || _arrTelemetry is null)
            return;

        var target = _targetSession.SelectedTarget;
        if (target is null)
            return;

        var product = _activeArrProduct;
        var targetId = target.Id;
        var cacheKey = ArrCacheKey(targetId, product);
        var hasCached = _arrCache.TryGetValue(cacheKey, out var cached);

        _arrBusy = true;

        SetText(
            "ArrFreshnessText",
            hasCached
                ? $"LIVE · {ArrCadenceLabel()} · updating"
                : $"UPDATING · {ArrCadenceLabel()}");

        if (showStatus)
            SetText("ArrStatusText", $"Refreshing {product} telemetry...");

        try
        {
            var snapshot = await _arrTelemetry.CaptureAsync(target, product);
            _arrCache[cacheKey] = snapshot;

            if (IsCurrentArrSelection(targetId, product))
                ApplyArrSnapshot(snapshot);
        }
        catch (Exception exception)
        {
            if (!IsCurrentArrSelection(targetId, product))
                return;

            if (hasCached && cached is not null)
            {
                ApplyArrSnapshot(cached);
                SetText(
                    "ArrFreshnessText",
                    $"STALE · {ArrCadenceLabel()} · retrying");
                SetText(
                    "ArrStatusText",
                    showStatus
                        ? "Last live snapshot retained · " + exception.Message
                        : "Last live snapshot retained while Arr telemetry retries.");
            }
            else
            {
                SetText("ArrStateMetricText", "UNAVAILABLE");
                SetText("ArrFreshnessText", "PROBE FAILED");
                SetText("ArrStatusText", exception.Message);
                Get<ListBox>("ArrInstanceTelemetryList").IsVisible = false;
                Get<Border>("ArrServiceEmptyState").IsVisible = true;
                Get<ListBox>("ArrQueueHealthList").IsVisible = false;
                Get<Border>("ArrQueueEmptyState").IsVisible = true;
                SetText(
                    "ArrQueueEmptyText",
                    $"{product} API telemetry is unavailable.");
            }
        }
        finally
        {
            _arrBusy = false;
        }
    }

    private bool IsCurrentArrSelection(string targetId, string product) =>
        _targetSession.SelectedTarget?.Id.Equals(
            targetId,
            StringComparison.Ordinal) == true &&
        _activeArrProduct.Equals(
            product,
            StringComparison.OrdinalIgnoreCase);

    private void UpdateArrDiscovery(
        string product,
        IntegrationSnapshot? integration)
    {
        var normalized = WindowsArrProductPolicy.Normalize(product);
        _arrDiscovery[normalized] = integration;

        Get<TextBlock>("AcquisitionGroupLabel").IsVisible = true;
        Get<Button>(normalized + "Nav").IsVisible = true;

        if (_activeArrProduct.Equals(
                normalized,
                StringComparison.OrdinalIgnoreCase))
        {
            RefreshArrDiscoveryEvidence();
        }
    }

    private void RefreshArrDiscoveryEvidence()
    {
        _arrDiscovery.TryGetValue(_activeArrProduct, out var integration);

        SetText(
            "ArrDiscoveryEvidenceText",
            integration is null
                ? "No Windows provider evidence was reported. Manual LAN or localhost API configuration remains available."
                : $"{integration.Kind} · {integration.State} · {integration.Evidence}");
    }

    private void OnArrTargetChanged()
    {
        Get<TextBox>("ArrApiKeyTextBox").Text = string.Empty;

        if (Get<Control>("ArrPage").IsVisible)
            ActivateWindowsArrWorkspace(_activeArrProduct);
    }

    private void UpdateArrTimerCadence()
    {
        var interval =
            WindowState == WindowState.Minimized
                ? ArrMinimizedRefreshInterval
                : Get<Control>("ArrPage").IsVisible
                    ? ArrForegroundRefreshInterval
                    : ArrBackgroundRefreshInterval;

        if (_arrTimer.Interval != interval)
            _arrTimer.Interval = interval;
    }

    private string ArrCadenceLabel()
    {
        if (WindowState == WindowState.Minimized)
            return "60s minimized";

        return Get<Control>("ArrPage").IsVisible
            ? "10s live"
            : "30s background";
    }

    private static string ArrCacheKey(string targetId, string product) =>
        $"{targetId}|{product}";

    private async void ArrRefreshButton_OnClick(
        object? sender,
        RoutedEventArgs e) =>
        await RefreshArrTelemetryAsync(showStatus: true);

    private async void ArrSaveTestButton_OnClick(
        object? sender,
        RoutedEventArgs e)
    {
        if (_arrTelemetry is null)
            return;

        var target = ActiveTargetOrThrow();
        var endpoint =
            Get<TextBox>("ArrEndpointTextBox").Text ?? string.Empty;
        var apiKey = Get<TextBox>("ArrApiKeyTextBox").Text;

        SetText(
            "ArrStatusText",
            $"Testing {_activeArrProduct} API access...");

        try
        {
            var snapshot = await _arrTelemetry.TestAndSaveAsync(
                target,
                _activeArrProduct,
                endpoint,
                apiKey);

            _arrCache[ArrCacheKey(target.Id, _activeArrProduct)] = snapshot;
            Get<TextBox>("ArrApiKeyTextBox").Text = string.Empty;
            ApplyArrSnapshot(snapshot);

            SetText(
                "ArrStatusText",
                $"{_activeArrProduct} endpoint and protected API access verified.");
        }
        catch (Exception exception)
        {
            SetText("ArrStatusText", exception.Message);
        }
    }

    private async void ArrClearSavedKeyButton_OnClick(
        object? sender,
        RoutedEventArgs e)
    {
        if (_arrTelemetry is null)
            return;

        try
        {
            var target = ActiveTargetOrThrow();

            await _arrTelemetry.ClearSavedApiKeyAsync(
                target.Id,
                _activeArrProduct);

            _arrCache.Remove(ArrCacheKey(target.Id, _activeArrProduct));
            Get<TextBox>("ArrApiKeyTextBox").Text = string.Empty;

            SetText(
                "ArrStatusText",
                $"Saved {_activeArrProduct} API key removed from Windows Credential Manager. " +
                "A native local config.xml may still be discovered.");

            await RefreshArrTelemetryAsync(showStatus: false);
        }
        catch (Exception exception)
        {
            SetText("ArrStatusText", exception.Message);
        }
    }

    private void ArrOpenButton_OnClick(
        object? sender,
        RoutedEventArgs e)
    {
        try
        {
            var endpoint = ArrTelemetryEndpoint.Normalize(
                Get<TextBox>("ArrEndpointTextBox").Text ?? string.Empty);

            Process.Start(
                new ProcessStartInfo(endpoint.AbsoluteUri)
                {
                    UseShellExecute = true
                });

            SetText("ArrStatusText", $"Opened {_activeArrProduct}.");
        }
        catch (Exception exception)
        {
            SetText("ArrStatusText", exception.Message);
        }
    }
}
