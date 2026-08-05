using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Diagnostics;
using System.Text;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;

namespace GraveOps.Desktop.Linux;

public partial class MainWindow
{
    private readonly Dictionary<string, VerifiedRemediationPlan>
        _verifiedRemediationPlans =
            new(StringComparer.OrdinalIgnoreCase);
    private Flyout? _verifiedRemediationFlyout;
    private CheckBox? _verifiedRemediationSafeModeEditor;
    private CheckBox? _verifiedRemediationConfirmEditor;
    private CheckBox? _verifiedRemediationVerifyEditor;
    private CheckBox? _verifiedRemediationStorageBlockEditor;
    private bool _verifiedRemediationBusy;

    private IReadOnlyList<UnifiedDashboardCard>
        AttachVerifiedRemediationActions(
            IReadOnlyList<UnifiedDashboardCard> cards)
    {
        var projected = RemediationPolicy.AttachActions(
            cards,
            _integrations,
            out var plans);
        _verifiedRemediationPlans.Clear();
        foreach (var item in plans)
            _verifiedRemediationPlans[item.Key] = item.Value;
        return projected;
    }

    private void OpenVerifiedRemediation(
        UnifiedDashboardAction action,
        Control? anchor)
    {
        if (anchor is null)
            return;

        var planId = !string.IsNullOrWhiteSpace(action.Endpoint)
            ? action.Endpoint
            : action.NavigationName.StartsWith(
                "@remediate:",
                StringComparison.OrdinalIgnoreCase)
                ? action.NavigationName[11..]
                : string.Empty;
        if (!_verifiedRemediationPlans.TryGetValue(planId, out var plan))
        {
            var fallback = new StackPanel
            {
                Width = 460,
                Spacing = 8
            };
            fallback.Children.Add(new TextBlock
            {
                Text = "Remediation plan expired",
                FontSize = 18,
                FontWeight = FontWeight.SemiBold
            });
            fallback.Children.Add(new TextBlock
            {
                Text = "Refresh the dashboard and reopen Remediation so the plan is rebuilt from current evidence.",
                TextWrapping = TextWrapping.Wrap,
                Classes = { "muted" }
            });
            var refresh = new Button
            {
                Content = "Refresh",
                Classes = { "primary" },
                HorizontalAlignment = HorizontalAlignment.Right
            };
            refresh.Click += (_, _) =>
            {
                _verifiedRemediationFlyout?.Hide();
                _ = RunCoordinatedRefreshAsync(background: false);
            };
            fallback.Children.Add(refresh);
            _verifiedRemediationFlyout = new Flyout { Content = fallback };
            _verifiedRemediationFlyout.FlyoutPresenterClasses.Add(
                "dashboardInfoFlyout");
            _verifiedRemediationFlyout.ShowAt(anchor);
            return;
        }

        var settings = RemediationStore.GetSettings();
        var storageFault = VerifiedRemediationStorageFaultActive();
        var blockReason = RemediationPolicy.MutationBlockReason(
            plan,
            settings,
            storageFault);
        var status = new TextBlock
        {
            Text = string.IsNullOrWhiteSpace(blockReason)
                ? "Ready for inspection. No command has run."
                : blockReason,
            TextWrapping = TextWrapping.Wrap,
            Classes = { "muted" }
        };
        var confirmation = new TextBox
        {
            PlaceholderText = plan.ConfirmationText,
            IsVisible = plan.CanMutate && settings.RequireTypedConfirmation,
            IsEnabled = string.IsNullOrWhiteSpace(blockReason)
        };
        var execute = new Button
        {
            Content = plan.RecoveryAction,
            IsVisible = plan.CanMutate,
            IsEnabled = plan.CanMutate &&
                string.IsNullOrWhiteSpace(blockReason) &&
                !settings.RequireTypedConfirmation,
            Classes = { "danger" }
        };
        confirmation.TextChanged += (_, _) =>
        {
            execute.IsEnabled =
                string.IsNullOrWhiteSpace(blockReason) &&
                confirmation.Text?.Equals(
                    plan.ConfirmationText,
                    StringComparison.Ordinal) == true;
        };

        var content = new StackPanel
        {
            Width = 650,
            Spacing = 9
        };
        content.Children.Add(new TextBlock
        {
            Text = $"Verified remediation · {plan.Product}",
            FontSize = 18,
            FontWeight = FontWeight.SemiBold
        });
        content.Children.Add(new TextBlock
        {
            Text = $"{plan.Family} · {plan.TargetKind} · {plan.Risk}",
            Classes = { "muted" }
        });

        var body = new StackPanel { Spacing = 8 };
        AddVerifiedRemediationSection(body, "Problem", plan.Problem);
        AddVerifiedRemediationSection(body, "Likely cause", plan.LikelyCause);
        AddVerifiedRemediationSection(
            body,
            "Dependencies",
            plan.Dependencies.Count == 0
                ? "No catalog dependency was declared."
                : string.Join(" · ", plan.Dependencies));
        AddVerifiedRemediationSection(
            body,
            "Recommended action",
            plan.CanMutate
                ? $"Inspect first, then {plan.RecoveryAction.ToLowerInvariant()} only when the evidence supports it."
                : "Inspect the owning workspace and logs. Mutation is unavailable until an exact service, container, timer, or mount is verified.");
        AddVerifiedRemediationSection(
            body,
            "Inspection",
            string.Join(
                Environment.NewLine,
                plan.InspectionCommands.Select(command =>
                    PlatformHardening.Redact(command, 2048))));
        AddVerifiedRemediationSection(
            body,
            "Exact recovery command",
            string.IsNullOrWhiteSpace(plan.RecoveryCommand)
                ? "Inspection only"
                : PlatformHardening.Redact(
                    plan.RecoveryCommand,
                    4096));
        AddVerifiedRemediationSection(body, "Expected result", plan.ExpectedResult);
        AddVerifiedRemediationSection(
            body,
            "Verification",
            PlatformHardening.Redact(
                plan.VerificationCommand,
                4096));
        AddVerifiedRemediationSection(body, "Rollback / recovery", plan.Rollback);
        body.Children.Add(status);
        body.Children.Add(confirmation);

        content.Children.Add(new ScrollViewer
        {
            MaxHeight = 500,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            Content = body
        });

        var footer = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 7,
            HorizontalAlignment = HorizontalAlignment.Right
        };
        var inspect = new Button { Content = "Run inspection" };
        inspect.Click += async (_, _) =>
            await RunVerifiedRemediationInspectionAsync(plan, status);
        execute.Click += async (_, _) =>
            await RunVerifiedRemediationRecoveryAsync(plan, status, execute);
        var workspace = new Button { Content = "Open workspace" };
        workspace.Click += (_, _) =>
        {
            _verifiedRemediationFlyout?.Hide();
            Navigate(plan.NavigationName);
        };
        var close = new Button { Content = "Close" };
        close.Click += (_, _) => _verifiedRemediationFlyout?.Hide();
        footer.Children.Add(inspect);
        if (plan.CanMutate)
            footer.Children.Add(execute);
        footer.Children.Add(workspace);
        footer.Children.Add(close);
        content.Children.Add(footer);

        _verifiedRemediationFlyout = new Flyout { Content = content };
        _verifiedRemediationFlyout.FlyoutPresenterClasses.Add(
            "dashboardInfoFlyout");
        _verifiedRemediationFlyout.ShowAt(anchor);
    }

    private async Task RunVerifiedRemediationInspectionAsync(
        VerifiedRemediationPlan plan,
        TextBlock status)
    {
        if (_verifiedRemediationBusy)
        {
            status.Text = "Another remediation operation is already running.";
            return;
        }

        _verifiedRemediationBusy = true;
        status.Text = "Running read-only inspection…";
        try
        {
            var output = new StringBuilder();
            var success = true;
            foreach (var command in plan.InspectionCommands)
            {
                var result = await RunVerifiedRemediationShellAsync(
                    command,
                    TimeSpan.FromSeconds(25));
                output.AppendLine($"> {PlatformHardening.Redact(command, 2048)}");
                output.AppendLine(result.Output);
                if (!result.Success)
                    success = false;
            }
            status.Text =
                (success ? "Inspection completed." : "Inspection completed with errors.") +
                Environment.NewLine +
                LimitVerifiedRemediationOutput(output.ToString());
            RecordRoutineControlPlaneActivity(
                "Inspection",
                plan.Target,
                $"{plan.Product} remediation inspection",
                status.Text,
                plan.NavigationName,
                TimeSpan.FromSeconds(10),
                unread: false);
        }
        finally
        {
            _verifiedRemediationBusy = false;
        }
    }

    private async Task RunVerifiedRemediationRecoveryAsync(
        VerifiedRemediationPlan plan,
        TextBlock status,
        Button execute)
    {
        if (_verifiedRemediationBusy)
        {
            status.Text = "Another remediation operation is already running.";
            return;
        }

        var settings = RemediationStore.GetSettings();
        var block = RemediationPolicy.MutationBlockReason(
            plan,
            settings,
            VerifiedRemediationStorageFaultActive());
        if (!string.IsNullOrWhiteSpace(block))
        {
            status.Text = block;
            return;
        }

        var hostId = _controlPlane.ActiveProfile.Id;
        if (!RemediationStore.TryStart(plan, hostId, out var job))
        {
            status.Text =
                $"A remediation job for {plan.Target} is already queued, running, or verifying.";
            return;
        }

        _verifiedRemediationBusy = true;
        execute.IsEnabled = false;
        status.Text = $"Executing {plan.RecoveryAction.ToLowerInvariant()}…";
        try
        {
            RemediationStore.Update(
                job.Id,
                VerifiedRemediationJobState.Running);
            var action = await ExecuteVerifiedRemediationActionAsync(plan);
            if (!action.Success)
            {
                var failed = RemediationStore.Update(
                    job.Id,
                    VerifiedRemediationJobState.Failed,
                    action.Output);
                status.Text = action.Summary + Environment.NewLine +
                    LimitVerifiedRemediationOutput(action.Output);
                RecordVerifiedRemediationActivity(plan, failed, false);
                return;
            }

            RemediationStore.Update(
                job.Id,
                VerifiedRemediationJobState.Verifying,
                action.Output);
            var verification = settings.VerifyAfterAction
                ? await RunVerifiedRemediationShellAsync(
                    plan.VerificationCommand,
                    TimeSpan.FromSeconds(25))
                : new VerifiedRemediationExecutionResult(
                    true,
                    "Post-action verification disabled by policy.",
                    "verification disabled");
            var verified = !settings.VerifyAfterAction ||
                RemediationPolicy.VerificationSucceeded(
                    plan,
                    verification.Success ? 0 : 1,
                    verification.Output);
            var finalState = verified
                ? VerifiedRemediationJobState.Succeeded
                : VerifiedRemediationJobState.Failed;
            var completed = RemediationStore.Update(
                job.Id,
                finalState,
                action.Output,
                verification.Output,
                verified);
            status.Text = verified
                ? $"{plan.RecoveryAction} completed and the expected state was verified."
                : $"{plan.RecoveryAction} returned successfully, but verification failed. The finding remains open.";
            status.Text += Environment.NewLine +
                LimitVerifiedRemediationOutput(verification.Output);
            RecordVerifiedRemediationActivity(plan, completed, verified);
            await RunCoordinatedRefreshAsync(background: false);
        }
        catch (Exception exception)
        {
            var failed = RemediationStore.Update(
                job.Id,
                VerifiedRemediationJobState.Failed,
                PlatformHardening.SanitizeException(exception));
            status.Text =
                "Remediation failed before verification." +
                Environment.NewLine +
                PlatformHardening.SanitizeException(exception);
            RecordVerifiedRemediationActivity(plan, failed, false);
        }
        finally
        {
            _verifiedRemediationBusy = false;
            execute.IsEnabled = true;
            PopulateVerifiedRemediationSettings();
        }
    }

    private async Task<VerifiedRemediationExecutionResult>
        ExecuteVerifiedRemediationActionAsync(
            VerifiedRemediationPlan plan)
    {
        OpsActionResult result;
        switch (plan.TargetKind)
        {
            case VerifiedRemediationTargetKind.SystemdService:
            case VerifiedRemediationTargetKind.BackupTimer:
                result = await _actions.ServiceAsync(
                    plan.Target,
                    "restart");
                return new VerifiedRemediationExecutionResult(
                    result.Success,
                    result.Summary,
                    result.Output);

            case VerifiedRemediationTargetKind.DockerContainer:
                result = await _actions.ContainerAsync(
                    plan.Target,
                    "restart");
                return new VerifiedRemediationExecutionResult(
                    result.Success,
                    result.Summary,
                    result.Output);

            case VerifiedRemediationTargetKind.Mount:
            case VerifiedRemediationTargetKind.PiHole:
                return await RunVerifiedRemediationShellAsync(
                    plan.RecoveryCommand,
                    TimeSpan.FromSeconds(60));

            default:
                return new VerifiedRemediationExecutionResult(
                    false,
                    "No verified mutating action is available.",
                    "The plan does not identify an exact owning service, container, timer, or mount.");
        }
    }

    private void RecordVerifiedRemediationActivity(
        VerifiedRemediationPlan plan,
        VerifiedRemediationJob job,
        bool success)
    {
        RecordRoutineControlPlaneActivity(
            success ? "Recovery" : "Failure",
            plan.Target,
            success
                ? $"{plan.Product} remediation verified"
                : $"{plan.Product} remediation failed",
            $"{job.State} · {plan.RecoveryAction} · " +
            $"verification {(job.Verified ? "passed" : "failed")} · " +
            LimitVerifiedRemediationOutput(
                string.IsNullOrWhiteSpace(job.Verification)
                    ? job.Output
                    : job.Verification),
            plan.NavigationName,
            TimeSpan.Zero,
            unread: !success);
    }

    private bool VerifiedRemediationStorageFaultActive()
    {
        if (_policyEvaluation is null)
            return false;
        return _policyEvaluation.Active.Any(item =>
            item.Severity >= OpsSeverity.Error &&
            (item.Component.Contains(
                 "storage",
                 StringComparison.OrdinalIgnoreCase) ||
             item.Component.Contains(
                 "mount",
                 StringComparison.OrdinalIgnoreCase) ||
             item.Problem.Contains(
                 "read-only",
                 StringComparison.OrdinalIgnoreCase) ||
             item.Problem.Contains(
                 "unavailable",
                 StringComparison.OrdinalIgnoreCase)));
    }

    private async Task<VerifiedRemediationExecutionResult>
        RunVerifiedRemediationShellAsync(
            string command,
            TimeSpan timeout)
    {
        var result = await PlatformHardening.RunShellAsync(
            command,
            timeout);
        return new VerifiedRemediationExecutionResult(
            result.Success,
            result.Summary,
            result.Output);
    }

    private void PopulateVerifiedRemediationSettings()
    {
        var summary = this.FindControl<TextBlock>(
            "SettingsVerifiedRemediationSummaryText");
        if (summary is null)
            return;
        var settings = RemediationStore.GetSettings();
        var jobs = RemediationStore.RecentJobs(
            _controlPlane.ActiveProfile.Id);
        var failed = jobs.Count(item =>
            item.State == VerifiedRemediationJobState.Failed);
        summary.Text =
            $"Operational parity · {ProductOperations.CoverageSummary()} · " +
            $"safe mode {(settings.SafeMode ? "on" : "off")} · " +
            $"{jobs.Count} recent job(s) · {failed} failed";
    }

    private void VerifiedRemediationPolicyButton_OnClick(
        object? sender,
        RoutedEventArgs e)
    {
        if (sender is not Button anchor)
            return;
        var settings = RemediationStore.GetSettings();
        _verifiedRemediationSafeModeEditor = new CheckBox
        {
            Content = "Safe mode — block all mutating remediation actions",
            IsChecked = settings.SafeMode
        };
        _verifiedRemediationConfirmEditor = new CheckBox
        {
            Content = "Require typed confirmation for recovery",
            IsChecked = settings.RequireTypedConfirmation
        };
        _verifiedRemediationVerifyEditor = new CheckBox
        {
            Content = "Require post-action verification",
            IsChecked = settings.VerifyAfterAction
        };
        _verifiedRemediationStorageBlockEditor = new CheckBox
        {
            Content = "Block storage-sensitive recovery during active storage faults",
            IsChecked = settings.BlockOnStorageFault
        };

        var content = new StackPanel
        {
            Width = 600,
            Spacing = 9
        };
        content.Children.Add(new TextBlock
        {
            Text = "Verified remediation safety",
            FontSize = 18,
            FontWeight = FontWeight.SemiBold
        });
        content.Children.Add(new TextBlock
        {
            Text = "The catalog covers detected and currently absent Arr, media-server, download, processing, request and DNS products. Actions appear only when current evidence identifies a problem.",
            TextWrapping = TextWrapping.Wrap,
            Classes = { "muted" }
        });
        content.Children.Add(_verifiedRemediationSafeModeEditor);
        content.Children.Add(_verifiedRemediationConfirmEditor);
        content.Children.Add(_verifiedRemediationVerifyEditor);
        content.Children.Add(_verifiedRemediationStorageBlockEditor);
        content.Children.Add(new TextBlock
        {
            Text = $"Operational parity: {ProductOperations.CoverageSummary()}" +
                Environment.NewLine +
                string.Join(", ", ProductOperations.All.Select(item => item.Name)),
            TextWrapping = TextWrapping.Wrap,
            Classes = { "dim" },
            FontSize = 9
        });

        var footer = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 7,
            HorizontalAlignment = HorizontalAlignment.Right
        };
        var save = new Button
        {
            Content = "Save",
            Classes = { "primary" }
        };
        save.Click += (_, _) => SaveVerifiedRemediationSettings();
        var close = new Button { Content = "Close" };
        close.Click += (_, _) => _verifiedRemediationFlyout?.Hide();
        footer.Children.Add(save);
        footer.Children.Add(close);
        content.Children.Add(footer);

        _verifiedRemediationFlyout = new Flyout { Content = content };
        _verifiedRemediationFlyout.FlyoutPresenterClasses.Add(
            "dashboardInfoFlyout");
        _verifiedRemediationFlyout.ShowAt(anchor);
    }

    private void SaveVerifiedRemediationSettings()
    {
        var settings = RemediationStore.GetSettings();
        settings.SafeMode = _verifiedRemediationSafeModeEditor?.IsChecked != false;
        settings.RequireTypedConfirmation =
            _verifiedRemediationConfirmEditor?.IsChecked != false;
        settings.VerifyAfterAction =
            _verifiedRemediationVerifyEditor?.IsChecked != false;
        settings.BlockOnStorageFault =
            _verifiedRemediationStorageBlockEditor?.IsChecked != false;
        RemediationStore.SetSettings(settings);
        _verifiedRemediationFlyout?.Hide();
        PopulateVerifiedRemediationSettings();
    }

    private static void AddVerifiedRemediationSection(
        Panel panel,
        string heading,
        string value)
    {
        panel.Children.Add(new TextBlock
        {
            Text = heading.ToUpperInvariant(),
            Classes = { "eyebrow" }
        });
        panel.Children.Add(new TextBlock
        {
            Text = string.IsNullOrWhiteSpace(value) ? "--" : value,
            TextWrapping = TextWrapping.Wrap
        });
    }

    private string LimitVerifiedRemediationOutput(string value) =>
        PlatformHardening.Redact(value, 4000).Trim();
}
