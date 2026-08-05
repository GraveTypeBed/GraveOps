using System.Diagnostics;
using System.Text.Json;
using Avalonia.Controls;
using GraveOps.Core.Hosts;
using GraveOps.Core.Snapshots;

namespace GraveOps.Desktop.Linux;

public partial class MainWindow
{
    private static readonly TimeSpan
        ManualRefreshTimeout =
            TimeSpan.FromMinutes(3);
    private static readonly TimeSpan
        BackgroundRefreshTimeout =
            TimeSpan.FromMinutes(2);
    private static readonly TimeSpan
        BackupRefreshInterval =
            TimeSpan.FromMinutes(10);

    private readonly SemaphoreSlim
        _refreshGate =
            new(1, 1);
    private CancellationTokenSource?
        _activeRefreshCancellation;
    private DateTimeOffset
        _lastBackupCaptureAt =
            DateTimeOffset.MinValue;
    private string
        _commandPaletteSnapshotKey =
            string.Empty;
    private bool
        _refreshOrchestrationDisposed;

    private sealed record RefreshAnalysisBundle(
        IReadOnlyList<OpsLogGroup> Logs,
        int ExcludedGroupCount,
        OpsAnalysis Analysis,
        IReadOnlyList<OpsLifecycleStage> Lifecycle);

    private sealed record RefreshPerformanceEntry(
        DateTimeOffset StartedAt,
        string Reason,
        bool Succeeded,
        bool Cancelled,
        long CollectionMilliseconds,
        long IdentityMilliseconds,
        long AnalysisMilliseconds,
        long HistoryMilliseconds,
        long ProjectionMilliseconds,
        long TotalMilliseconds,
        string Error);

    private sealed record TargetBackupCapture(
        OpsBackupSnapshot Snapshot,
        bool Refreshed);

    private async Task RunCoordinatedRefreshAsync(
        bool background)
    {
        if (_refreshOrchestrationDisposed)
            return;

        var request =
            new CancellationTokenSource(
                background
                    ? BackgroundRefreshTimeout
                    : ManualRefreshTimeout);

        if (background)
        {
            if (Interlocked.CompareExchange(
                    ref _activeRefreshCancellation,
                    request,
                    null) is not null)
            {
                request.Dispose();
                return;
            }
        }
        else
        {
            var previous =
                Interlocked.Exchange(
                    ref _activeRefreshCancellation,
                    request);
            previous?.Cancel();
        }

        LinuxTargetRefreshContext context;

        try
        {
            context =
                BeginTargetRefreshContext();
        }
        catch
        {
            Interlocked.CompareExchange(
                ref _activeRefreshCancellation,
                null,
                request);
            request.Dispose();
            throw;
        }

        var entered = false;
        var startedAt =
            DateTimeOffset.UtcNow;
        var total =
            Stopwatch.StartNew();
        var collectionMs = 0L;
        var identityMs = 0L;
        var analysisMs = 0L;
        var historyMs = 0L;
        var projectionMs = 0L;
        var succeeded = false;
        var cancelled = false;
        var error = string.Empty;

        try
        {
            await _refreshGate.WaitAsync(
                request.Token);
            entered = true;

            request.Token.ThrowIfCancellationRequested();
            EnsureTargetRefreshCurrent(
                context);

            SetRefreshPresentation(
                refreshing: true,
                failed: false);

            var phase =
                Stopwatch.StartNew();
            var snapshot =
                await CaptureActiveTargetAsync(
                    context.Profile,
                    background,
                    request.Token);
            var envelope =
                CreateTargetSnapshotEnvelope(
                    context,
                    snapshot);

            var backupCapture =
                await CaptureBackupIfDueAsync(
                    context.Profile,
                    envelope.Snapshot,
                    background,
                    request.Token);
            collectionMs =
                phase.ElapsedMilliseconds;

            phase.Restart();
            var identity =
                await Task.Run(
                    () =>
                        ApplicationIdentityResolver.ResolveAsync(
                            envelope.Snapshot,
                            context.Profile.Id,
                            ActiveTargetUrlHost(
                                context.Profile),
                            context.Profile.IsLocal,
                            _applicationIdentityStore,
                            request.Token),
                    request.Token);
            identityMs =
                phase.ElapsedMilliseconds;

            phase.Restart();
            var analysisBundle =
                await Task.Run(
                    () =>
                    {
                        var logs =
                            LinuxOpsAnalyzer
                                .GroupLogs(
                                    envelope.Snapshot.RecentLogs)
                                .Where(item =>
                                    !IsPlexTokenProbePrivilegeNoise(
                                        item))
                                .ToArray();
                        var analysisLogs =
                            SignalQualityPolicy.ForHealthAnalysis(
                                logs,
                                out var excluded);
                        var analysis =
                            LinuxOpsAnalyzer.Analyze(
                                envelope.Snapshot,
                                backupCapture.Snapshot,
                                analysisLogs,
                                identity.Integrations);
                        var lifecycle =
                            LinuxOpsAnalyzer.BuildLifecycle(
                                envelope.Snapshot,
                                identity.Integrations,
                                analysis);

                        return new RefreshAnalysisBundle(
                            logs,
                            excluded,
                            analysis,
                            lifecycle);
                    },
                    request.Token);
            analysisMs =
                phase.ElapsedMilliseconds;

            request.Token.ThrowIfCancellationRequested();
            EnsureTargetRefreshCurrent(
                context);

            _activeTargetCapabilities =
                envelope.Capabilities;
            _acceptedTargetId =
                context.Profile.Id;
            _snapshot =
                envelope.Snapshot;
            _backup =
                backupCapture.Snapshot;

            if (backupCapture.Refreshed)
            {
                _lastBackupTargetId =
                    context.Profile.Id;
                _lastBackupCaptureAt =
                    DateTimeOffset.UtcNow;
            }

            _identityResolution =
                identity;
            _integrations =
                identity.Integrations;
            RememberApplicationInventory(
                context.Profile,
                envelope.Snapshot.CapturedAt,
                envelope.Capabilities,
                identity);
            _logs =
                analysisBundle.Logs;
            _signalQualityExcludedGroups =
                analysisBundle.ExcludedGroupCount;
            var signalObservations =
                SignalQualityRefreshSucceeded(
                    identity.Integrations);
            _rawAnalysis =
                HealthPolicy.MergeAnalysis(
                    analysisBundle.Analysis,
                    signalObservations,
                    SignalQualityStore.GetSettings(),
                    identity.Integrations);
            _rawLifecycle =
                analysisBundle.Lifecycle;
            ApplyFindingPolicies();
            RecordInsightCapture();

            phase.Restart();
            _history.Record(
                envelope.Snapshot,
                _analysis!,
                _lifecycle,
                backupCapture.Snapshot,
                _findingPolicies.EvaluateStorageSeverity);
            historyMs =
                phase.ElapsedMilliseconds;

            EnsureTargetRefreshCurrent(
                context);

            phase.Restart();
            BeginUiRefreshProjection();
            ProjectRefreshedSnapshot();
            projectionMs =
                phase.ElapsedMilliseconds;

            RecordRefreshSuccessAndNotify(
                context.Profile);

            if (!background)
            {
                _nextBackgroundRefreshAt =
                    DateTimeOffset.Now +
                    TimeSpan.FromSeconds(
                        NormalizeBackgroundRefreshSeconds(
                            _operatorSettings.BackgroundRefreshSeconds));
            }

            succeeded = true;
            SetRefreshPresentation(
                refreshing: false,
                failed: false);
        }
        catch (OperationCanceledException)
        {
            cancelled = true;

            if (!background &&
                IsTargetRefreshCurrent(
                    context) &&
                ReferenceEquals(
                    Volatile.Read(
                        ref _activeRefreshCancellation),
                    request))
            {
                SetRefreshPresentation(
                    refreshing: false,
                    failed: false);
            }
        }
        catch (Exception exception)
        {
            error = exception.Message;

            if (!IsTargetRefreshCurrent(
                    context))
            {
                cancelled = true;
            }
            else
            {
                try
                {
                    SignalQualityRefreshFailed(
                        exception);
                }
                catch (Exception signalException)
                {
                    Debug.WriteLine(
                        $"Signal-quality failure projection failed: {signalException}");
                }

                SetControlPlaneState(
                    OpsSeverity.Error,
                    "OFFLINE",
                    "Provider capture failed");
                Get<TextBlock>(
                        "LastUpdatedText")
                    .Text =
                    "Refresh failed";
                RecordRefreshFailure(
                    context.Profile,
                    exception);
                SetRefreshPresentation(
                    refreshing: false,
                    failed: true);
            }
        }
        finally
        {
            total.Stop();

            if (entered)
                _refreshGate.Release();

            Interlocked.CompareExchange(
                ref _activeRefreshCancellation,
                null,
                request);
            request.Dispose();

            _ = WriteRefreshPerformanceAsync(
                new RefreshPerformanceEntry(
                    startedAt,
                    background
                        ? "Automatic"
                        : "Manual",
                    succeeded,
                    cancelled,
                    collectionMs,
                    identityMs,
                    analysisMs,
                    historyMs,
                    projectionMs,
                    total.ElapsedMilliseconds,
                    error));
        }
    }

    private async Task<TargetBackupCapture>
        CaptureBackupIfDueAsync(
            LinuxHostProfile profile,
            HostSnapshot snapshot,
            bool background,
            CancellationToken cancellationToken)
    {
        var sameTarget =
            _acceptedTargetId.Equals(
                profile.Id,
                StringComparison.OrdinalIgnoreCase) &&
            _lastBackupTargetId.Equals(
                profile.Id,
                StringComparison.OrdinalIgnoreCase);

        var force =
            !sameTarget ||
            _backup is null ||
            DateTimeOffset.UtcNow -
                _lastBackupCaptureAt >=
                BackupRefreshInterval ||
            (!background &&
             _unifiedCurrentNavigation.Equals(
                 "BackupsNav",
                 StringComparison.Ordinal));

        if (!force)
        {
            return new TargetBackupCapture(
                _backup!,
                Refreshed: false);
        }

        var backup =
            await CaptureTargetBackupAsync(
                profile,
                snapshot,
                cancellationToken);

        return new TargetBackupCapture(
            backup,
            Refreshed: true);
    }

    private void SetRefreshPresentation(
        bool refreshing,
        bool failed)
    {
        // Healthy polling is deliberately silent. The manual button's
        // pressed state is enough acknowledgement; persistent copy is
        // reserved for a failure that needs operator attention.
        _ = refreshing;

        Get<TextBlock>(
                "UnifiedDashboardStatusText")
            .Text =
            failed
                ? "Refresh failed"
                : string.Empty;

        if (!failed)
        {
            Get<TextBlock>(
                    "LastUpdatedText")
                .Text =
                string.Empty;
        }
    }

    private void ProjectRefreshedSnapshot()
    {
        if (_snapshot is null ||
            _backup is null ||
            _analysis is null ||
            _policyEvaluation is null)
        {
            return;
        }

        Get<TextBlock>(
                "SidebarHostname")
            .Text =
            _snapshot.Hostname;
        Get<TextBlock>(
                "SidebarOperatingSystem")
            .Text =
            _snapshot.OperatingSystem;
        Get<TextBlock>(
                "LastUpdatedText")
            .Text =
            string.Empty;

        SetControlPlaneState(
            OpsSeverity.Healthy,
            "ONLINE",
            ControlPlaneConnectionDetail());

        ApplyActiveTargetCapabilities();
        ProjectActiveTargetShell(
            _controlPlane.ActiveProfile,
            _snapshot);

        UpdateIntegrationNavigation();
        UpdateActionButtons();
        ApplyActionAvailabilityReasons();

        var paletteKey =
            string.Join(
                '|',
                _integrations
                    .OrderBy(item =>
                        item.InstanceKey)
                    .Select(item =>
                        $"{item.InstanceKey}:{item.IsVisible}:{item.ShowInNavigation}"));

        if (!_commandPaletteSnapshotKey.Equals(
                paletteKey,
                StringComparison.Ordinal))
        {
            _commandPaletteSnapshotKey =
                paletteKey;
            RebuildCommandPalette();
        }

        PopulateControlPlaneFoundation();
        ProjectCurrentPageIncrementally(
            _unifiedCurrentNavigation,
            navigationActivation: false,
            force: false);
    }

    private void ProjectCurrentPageFromSnapshot(
        string? navigationName = null,
        bool navigationActivation = false)
    {
        if (_snapshot is null ||
            _backup is null ||
            _analysis is null)
        {
            return;
        }

        var key =
            string.IsNullOrWhiteSpace(
                navigationName)
                ? _unifiedCurrentNavigation
                : navigationName;

        if (!_navigation.TryGetValue(
                key,
                out var target))
        {
            return;
        }

        if (navigationActivation &&
            target.PageName is
                "ApplicationWorkspacePage" or
                "PlexWorkspacePage" or
                "PiHoleWorkspacePage" or
                "ArrWorkspacePage" or
                "DownloadClientWorkspacePage" or
                "RecyclarrWorkspacePage" or
                "DockerPage")
        {
            return;
        }

        switch (target.PageName)
        {
            case "DashboardPage":
                PopulateUnifiedDashboard();
                break;
            case "IntelligencePage":
                PopulateIntelligence();
                break;
            case "LifecyclePage":
                PopulateLifecycle();
                break;
            case "HistoryPage":
                PopulateHistory();
                break;
            case "ServersPage":
                PopulateServerPage();
                break;
            case "MediaHubPage":
                ApplyMediaFilter();
                break;
            case "PlexWorkspacePage":
                PopulatePlexWorkspace();
                break;
            case "PiHoleWorkspacePage":
                PopulatePiHoleWorkspace();
                break;
            case "ApplicationWorkspacePage":
                PopulateDirectIntegrationWorkspace();
                break;
            case "DownloadClientWorkspacePage":
                PopulateDownloadClientWorkspace();
                break;
            case "ArrWorkspacePage":
                PopulateArrApplicationPage();
                break;
            case "RecyclarrWorkspacePage":
                PopulateRecyclarrWorkspace();
                break;
            case "ServicesPage":
                ApplyServicesFilter();
                break;
            case "DockerPage":
                PopulateDockerWorkspaceFallback();
                break;
            case "StoragePage":
                ApplyStorageFilter();
                PopulateStorageCapacityPolicySettings();
                break;
            case "LogsPage":
                ApplyLogsFilter();
                break;
            case "BackupsPage":
                PopulateBackups();
                break;
            case "SettingsPage":
            case "ToolsPage":
                PopulateSettingsAndTools();
                PopulateUnifiedInterfaceSettings();
                PopulateStorageCapacityPolicySettings();
                PopulateSignalQualitySettings();
                PopulateVerifiedRemediationSettings();
                PopulateUiPerformanceSettings();
                break;
        }
    }

    private async Task WriteRefreshPerformanceAsync(
        RefreshPerformanceEntry entry)
    {
        try
        {
            await Task.Run(
                () =>
                {
                    var root =
                        Environment.GetEnvironmentVariable(
                            "XDG_CACHE_HOME");
                    if (string.IsNullOrWhiteSpace(
                            root))
                    {
                        root =
                            Path.Combine(
                                Environment.GetFolderPath(
                                    Environment.SpecialFolder.UserProfile),
                                ".cache");
                    }

                    var directory =
                        Path.Combine(
                            root,
                            "GraveOps");
                    Directory.CreateDirectory(
                        directory);
                    var path =
                        Path.Combine(
                            directory,
                            "refresh-performance.jsonl");

                    File.AppendAllText(
                        path,
                        JsonSerializer.Serialize(
                            entry) +
                        Environment.NewLine);

                    var info =
                        new FileInfo(path);
                    if (info.Length <=
                        2 * 1024 * 1024)
                    {
                        return;
                    }

                    var retained =
                        File.ReadLines(path)
                            .TakeLast(750)
                            .ToArray();
                    File.WriteAllLines(
                        path,
                        retained);
                });
        }
        catch
        {
            // Self-observability never interrupts refresh work.
        }
    }

    private void DisposeRefreshOrchestration()
    {
        _refreshOrchestrationDisposed =
            true;
        Interlocked.Exchange(
                ref _activeRefreshCancellation,
                null)
            ?.Cancel();
    }
}
