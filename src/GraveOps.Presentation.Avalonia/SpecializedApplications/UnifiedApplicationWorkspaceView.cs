using System;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using GraveOps.Presentation.Avalonia.MediaWorkspaces;

namespace GraveOps.Presentation.Avalonia.SpecializedApplications;

public sealed class UnifiedApplicationWorkspaceView :
    UserControl
{
    private readonly TextBlock _targetText;
    private readonly TextBlock _nameText;
    private readonly TextBlock _subtitleText;
    private readonly TextBlock _stateText;
    private readonly TextBlock _runtimeText;
    private readonly TextBlock _roleText;
    private readonly TextBlock _findingsText;
    private readonly TextBlock _primaryTitleText;
    private readonly TextBlock _endpointText;
    private readonly TextBlock _secondaryTitleText;
    private readonly TextBlock _ownerText;
    private readonly TextBox _evidenceText;
    private readonly TextBox _relatedText;
    private readonly TextBlock _statusText;
    private readonly Button _openButton;
    private readonly Button _dockerButton;
    private readonly Button _logsButton;
    private readonly Button _intelligenceButton;
    private readonly Button _backButton;

    public UnifiedApplicationWorkspaceView()
    {
        _targetText =
            MediaUi.Dim(
                "--");

        _nameText =
            MediaUi.PageTitle(
                "Application");

        _subtitleText =
            MediaUi.Subtitle(
                "Verified application state and operational ownership.");

        _stateText =
            MediaUi.MetricValue(
                "WAITING",
                20);

        _runtimeText =
            MediaUi.MetricValue(
                "--",
                18);

        _roleText =
            MediaUi.MetricValue(
                "--",
                18);

        _findingsText =
            MediaUi.MetricValue(
                "0");

        _primaryTitleText =
            MediaUi.Title(
                "Application readiness",
                14);

        _endpointText =
            MediaUi.Muted(
                "--");

        _secondaryTitleText =
            MediaUi.Title(
                "Dependencies",
                14);

        _ownerText =
            MediaUi.Muted(
                "--");

        _evidenceText =
            MediaUi.Console(
                "Waiting for verified application evidence.",
                180,
                520);

        _relatedText =
            MediaUi.Console(
                "No related operational context is available.",
                180,
                520);

        _statusText =
            MediaUi.Dim(
                "Waiting for application selection.");

        _openButton =
            MediaUi.Primary(
                "Open application");

        _dockerButton =
            MediaUi.Compact(
                "Docker / dependencies");

        _logsButton =
            MediaUi.Compact(
                "Logs");

        _intelligenceButton =
            MediaUi.Compact(
                "Intelligence");

        _backButton =
            MediaUi.Compact(
                "Back to applications");

        _openButton.Click +=
            (_, _) =>
                RaiseAction(
                    UnifiedApplicationWorkspaceAction.Open);

        _dockerButton.Click +=
            (_, _) =>
                RaiseAction(
                    UnifiedApplicationWorkspaceAction.Docker);

        _logsButton.Click +=
            (_, _) =>
                RaiseAction(
                    UnifiedApplicationWorkspaceAction.Logs);

        _intelligenceButton.Click +=
            (_, _) =>
                RaiseAction(
                    UnifiedApplicationWorkspaceAction.Intelligence);

        _backButton.Click +=
            (_, _) =>
                RaiseAction(
                    UnifiedApplicationWorkspaceAction.Back);

        var header =
            new Grid
            {
                ColumnDefinitions =
                    new ColumnDefinitions(
                        "*,Auto"),
                ColumnSpacing =
                    12
            };

        header.Children.Add(
            new StackPanel
            {
                Children =
                {
                    _nameText,
                    _subtitleText
                }
            });

        var headerActions =
            new StackPanel
            {
                HorizontalAlignment =
                    HorizontalAlignment.Right,
                Spacing =
                    6,
                Children =
                {
                    _targetText,
                    _backButton
                }
            };

        Grid.SetColumn(
            headerActions,
            1);

        header.Children.Add(
            headerActions);

        var metrics =
            MediaUi.FourMetrics(
                MediaUi.Metric(
                    "STATE",
                    _stateText),
                MediaUi.Metric(
                    "RUNTIME",
                    _runtimeText),
                MediaUi.Metric(
                    "ROLE",
                    _roleText),
                MediaUi.Metric(
                    "ACTIVE FINDINGS",
                    _findingsText));

        var operationButtons =
            new WrapPanel
            {
                Children =
                {
                    _openButton,
                    _dockerButton,
                    _logsButton,
                    _intelligenceButton
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
                    8,
                    0);
        }

        var operations =
            MediaUi.Module(
                new StackPanel
                {
                    Spacing =
                        8,
                    Children =
                    {
                        MediaUi.Title(
                            "Operations"),
                        MediaUi.Subtitle(
                            "Common work stays on this page; Docker, logs and Intelligence remain one click away."),
                        operationButtons
                    }
                });

        var summary =
            new Grid
            {
                ColumnDefinitions =
                    new ColumnDefinitions(
                        "1.1*,1.1*,1.3*"),
                ColumnSpacing =
                    10
            };

        var applicationColumn =
            new StackPanel
            {
                Spacing =
                    5,
                Children =
                {
                    MediaUi.Eyebrow(
                        "APPLICATION"),
                    _primaryTitleText
                }
            };

        var endpointColumn =
            new StackPanel
            {
                Spacing =
                    5,
                Children =
                {
                    MediaUi.Eyebrow(
                        "ENDPOINT"),
                    _endpointText
                }
            };

        var ownershipColumn =
            new StackPanel
            {
                Spacing =
                    5,
                Children =
                {
                    MediaUi.Eyebrow(
                        "OWNERSHIP"),
                    _secondaryTitleText,
                    _ownerText
                }
            };

        Grid.SetColumn(
            endpointColumn,
            1);

        Grid.SetColumn(
            ownershipColumn,
            2);

        summary.Children.Add(
            applicationColumn);

        summary.Children.Add(
            endpointColumn);

        summary.Children.Add(
            ownershipColumn);

        var evidence =
            MediaUi.TwoColumns(
                MediaUi.Module(
                    new StackPanel
                    {
                        Spacing =
                            7,
                        Children =
                        {
                            MediaUi.Title(
                                "Evidence"),
                            MediaUi.Subtitle(
                                "Runtime, endpoint and provider evidence for the selected application."),
                            _evidenceText
                        }
                    }),
                MediaUi.Module(
                    new StackPanel
                    {
                        Spacing =
                            7,
                        Children =
                        {
                            MediaUi.Title(
                                "Related context"),
                            MediaUi.Subtitle(
                                "Findings and safe next-page handoff."),
                            _relatedText,
                            _statusText
                        }
                    }),
                "1.25*,0.75*",
                8);

        Content =
            MediaUi.Scroll(
                new StackPanel
                {
                    Spacing =
                        12,
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
                        MediaUi.Module(
                            summary),
                        evidence
                    }
                });

        Update(
            UnifiedApplicationWorkspaceState.Empty);
    }

    public event EventHandler<
        UnifiedApplicationWorkspaceActionEventArgs>?
        ActionRequested;

    public void Update(
        UnifiedApplicationWorkspaceState state)
    {
        _targetText.Text =
            state.Target;

        _nameText.Text =
            state.Name;

        _subtitleText.Text =
            state.Subtitle;

        _stateText.Text =
            state.State;

        _runtimeText.Text =
            state.Runtime;

        _roleText.Text =
            state.Role;

        _findingsText.Text =
            state.ActiveFindings;

        _primaryTitleText.Text =
            state.PrimaryTitle;

        _endpointText.Text =
            state.Endpoint;

        _secondaryTitleText.Text =
            state.SecondaryTitle;

        _ownerText.Text =
            state.Owner;

        _evidenceText.Text =
            state.Evidence;

        _relatedText.Text =
            state.RelatedContext;

        _statusText.Text =
            state.OperationsStatus;

        _openButton.IsEnabled =
            state.CanOpen;

        _dockerButton.IsEnabled =
            state.CanOpenDocker;

        _logsButton.IsEnabled =
            state.CanOpenLogs;

        _intelligenceButton.IsEnabled =
            state.CanOpenIntelligence;

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
        UnifiedApplicationWorkspaceAction action)
    {
        ActionRequested?.Invoke(
            this,
            new UnifiedApplicationWorkspaceActionEventArgs(
                action));
    }
}
