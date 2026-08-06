using System;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using GraveOps.Presentation.Avalonia.MediaWorkspaces;

namespace GraveOps.Presentation.Avalonia.SpecializedApplications;

public sealed class UnifiedPiHoleView :
    UserControl
{
    private readonly TextBlock _targetText;
    private readonly TextBlock _freshnessText;
    private readonly TextBlock _stateText;
    private readonly TextBlock _dnsText;
    private readonly TextBlock _blockingText;
    private readonly TextBlock _queriesText;
    private readonly TextBlock _blockedText;
    private readonly TextBlock _versionsText;
    private readonly TextBlock _hostContextText;
    private readonly TextBlock _clientContextText;
    private readonly TextBlock _gravityContextText;
    private readonly TextBox _evidenceText;
    private readonly TextBlock _statusText;
    private readonly Button _refreshButton;
    private readonly Button _openButton;
    private readonly Button _enableButton;
    private readonly Button _disableButton;
    private readonly Button _reloadButton;
    private readonly Button _logsButton;
    private readonly Button _backButton;

    public UnifiedPiHoleView()
    {
        _targetText =
            MediaUi.Muted(
                "--");

        _freshnessText =
            MediaUi.Dim(
                "Not captured");

        _stateText =
            MediaUi.MetricValue(
                "NOT CAPTURED",
                19);

        _dnsText =
            MediaUi.MetricValue(
                "--");

        _blockingText =
            MediaUi.MetricValue(
                "--");

        _queriesText =
            MediaUi.MetricValue(
                "--");

        _blockedText =
            MediaUi.MetricValue(
                "--");

        _versionsText =
            MediaUi.Muted(
                "Core -- · Web -- · FTL --");

        _hostContextText =
            MediaUi.Muted(
                "Host context unavailable.");

        _clientContextText =
            MediaUi.Muted(
                "Client statistics unavailable.");

        _gravityContextText =
            MediaUi.Muted(
                "Gravity inventory unavailable.");

        _evidenceText =
            MediaUi.Console(
                "No verified Pi-hole source is associated with the active target.",
                220,
                600);

        _statusText =
            MediaUi.Subtitle(
                "Capture Pi-hole before running an operation. Safe Mode blocks all mutations.");

        _refreshButton =
            MediaUi.Primary(
                "Refresh");

        _openButton =
            MediaUi.Primary(
                "Open Pi-hole");

        _enableButton =
            MediaUi.Compact(
                "Enable blocking");

        _disableButton =
            MediaUi.Compact(
                "Disable 5m");

        _disableButton.Classes.Add(
            "danger");

        _reloadButton =
            MediaUi.Compact(
                "Reload DNS");

        _logsButton =
            MediaUi.Compact(
                "Logs");

        _backButton =
            MediaUi.Compact(
                "Back to applications");

        _refreshButton.Click +=
            (_, _) =>
                RaiseAction(
                    UnifiedPiHoleAction.Refresh);

        _openButton.Click +=
            (_, _) =>
                RaiseAction(
                    UnifiedPiHoleAction.Open);

        _enableButton.Click +=
            (_, _) =>
                RaiseAction(
                    UnifiedPiHoleAction.EnableBlocking);

        _disableButton.Click +=
            (_, _) =>
                RaiseAction(
                    UnifiedPiHoleAction.DisableBlockingFiveMinutes);

        _reloadButton.Click +=
            (_, _) =>
                RaiseAction(
                    UnifiedPiHoleAction.ReloadDns);

        _logsButton.Click +=
            (_, _) =>
                RaiseAction(
                    UnifiedPiHoleAction.Logs);

        _backButton.Click +=
            (_, _) =>
                RaiseAction(
                    UnifiedPiHoleAction.Back);

        var header =
            new Grid
            {
                ColumnDefinitions =
                    new ColumnDefinitions(
                        "*,Auto"),
                ColumnSpacing =
                    10
            };

        header.Children.Add(
            new StackPanel
            {
                Children =
                {
                    MediaUi.PageTitle(
                        "Pi-hole"),
                    MediaUi.Subtitle(
                        "DNS, blocking, query statistics, gravity state and confirmation-gated Linux controls.")
                }
            });

        var headerRight =
            new StackPanel
            {
                HorizontalAlignment =
                    HorizontalAlignment.Right,
                Spacing =
                    5,
                Children =
                {
                    _targetText,
                    _freshnessText,
                    _refreshButton,
                    _backButton
                }
            };

        Grid.SetColumn(
            headerRight,
            1);

        header.Children.Add(
            headerRight);

        var metrics =
            MediaUi.FourMetrics(
                MediaUi.Metric(
                    "DNS",
                    _dnsText),
                MediaUi.Metric(
                    "BLOCKING",
                    _blockingText),
                MediaUi.Metric(
                    "QUERIES · 24H",
                    _queriesText),
                MediaUi.Metric(
                    "BLOCKED",
                    _blockedText));

        var operationButtons =
            new WrapPanel
            {
                Children =
                {
                    _openButton,
                    _enableButton,
                    _disableButton,
                    _reloadButton,
                    _logsButton
                }
            };

        foreach (var button in
                 operationButtons
                     .Children
                     .OfType<Button>())
        {
            button.Margin =
                new Thickness(
                    0,
                    0,
                    7,
                    7);
        }

        var operations =
            MediaUi.Inset(
                new StackPanel
                {
                    Spacing =
                        8,
                    Children =
                    {
                        MediaUi.Title(
                            "Operations"),
                        _statusText,
                        operationButtons
                    }
                });

        var contextCard =
            MediaUi.Module(
                new StackPanel
                {
                    Spacing =
                        8,
                    Children =
                    {
                        MediaUi.Title(
                            "Pi-hole context"),
                        _stateText,
                        _versionsText,
                        _hostContextText,
                        _clientContextText
                    }
                });

        var gravityCard =
            MediaUi.Module(
                new StackPanel
                {
                    Spacing =
                        8,
                    Children =
                    {
                        MediaUi.Title(
                            "Gravity"),
                        _gravityContextText,
                        MediaUi.Subtitle(
                            "Gravity inventory remains read-only. Control changes are delegated to the active platform adapter.")
                    }
                });

        var context =
            MediaUi.TwoColumns(
                contextCard,
                gravityCard,
                "1.25*,0.75*",
                8);

        var evidence =
            MediaUi.Module(
                new StackPanel
                {
                    Spacing =
                        8,
                    Children =
                    {
                        MediaUi.Title(
                            "Evidence"),
                        MediaUi.Subtitle(
                            "Sanitized status, API and host evidence from the active target."),
                        _evidenceText
                    }
                });

        Content =
            MediaUi.Scroll(
                new StackPanel
                {
                    Spacing =
                        10,
                    Margin =
                        new Thickness(
                            0,
                            0,
                            4,
                            4),
                    Children =
                    {
                        header,
                        metrics,
                        operations,
                        context,
                        evidence
                    }
                });

        Update(
            UnifiedPiHoleState.Empty);
    }

    public event EventHandler<
        UnifiedPiHoleActionEventArgs>?
        ActionRequested;

    public void Update(
        UnifiedPiHoleState state)
    {
        _targetText.Text =
            state.Target;

        _freshnessText.Text =
            state.Freshness;

        _stateText.Text =
            state.State;

        _dnsText.Text =
            state.Dns;

        _blockingText.Text =
            state.Blocking;

        _queriesText.Text =
            state.Queries;

        _blockedText.Text =
            state.Blocked;

        _versionsText.Text =
            state.Versions;

        _hostContextText.Text =
            state.HostContext;

        _clientContextText.Text =
            state.ClientContext;

        _gravityContextText.Text =
            state.GravityContext;

        _evidenceText.Text =
            state.Evidence;

        _statusText.Text =
            state.Status;

        _refreshButton.IsEnabled =
            state.CanRefresh;

        _openButton.IsEnabled =
            state.CanOpen;

        _enableButton.IsEnabled =
            state.CanEnable;

        _disableButton.IsEnabled =
            state.CanDisable;

        _reloadButton.IsEnabled =
            state.CanReload;

        _logsButton.IsEnabled =
            state.CanOpenLogs;

        _backButton.IsVisible =
            state.ShowBack;
    }

    public void SetStatus(
        string status)
    {
        _statusText.Text =
            status;
    }

    private void RaiseAction(
        UnifiedPiHoleAction action)
    {
        ActionRequested?.Invoke(
            this,
            new UnifiedPiHoleActionEventArgs(
                action));
    }
}
