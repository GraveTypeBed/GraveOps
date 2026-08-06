using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using GraveOps.Presentation.Avalonia.MediaWorkspaces;

namespace GraveOps.Presentation.Avalonia.SpecializedApplications;

public sealed class UnifiedRecyclarrView :
    UserControl
{
    private readonly TextBlock _targetText;
    private readonly TextBlock _freshnessText;
    private readonly TextBlock _runtimeText;
    private readonly TextBlock _versionText;
    private readonly TextBlock _configCountText;
    private readonly TextBlock _targetCountText;
    private readonly TextBlock _containerText;
    private readonly TextBlock _imageText;
    private readonly TextBlock _composeText;
    private readonly TextBlock _scheduleText;
    private readonly TextBlock _configPathText;
    private readonly TextBlock _lastRunText;
    private readonly TextBlock _evidenceText;
    private readonly StackPanel _targetRows;
    private readonly StackPanel _configRows;
    private readonly TextBlock _previewStatusText;
    private readonly TextBox _previewOutputText;
    private readonly TextBlock _statusText;
    private readonly Button _refreshButton;
    private readonly Button _openConfigButton;
    private readonly Button _previewButton;
    private readonly Button _dockerButton;
    private readonly Button _logsButton;
    private readonly Button _backButton;

    public UnifiedRecyclarrView()
    {
        _targetText =
            MediaUi.Muted(
                "--");

        _freshnessText =
            MediaUi.Dim(
                "Capture pending");

        _runtimeText =
            MediaUi.MetricValue(
                "WAITING",
                20);

        _versionText =
            MediaUi.MetricValue(
                "--",
                18);

        _configCountText =
            MediaUi.MetricValue(
                "0");

        _targetCountText =
            MediaUi.MetricValue(
                "0");

        _containerText =
            MediaUi.Muted(
                "--");

        _imageText =
            MediaUi.Muted(
                "--");

        _composeText =
            MediaUi.Muted(
                "--");

        _scheduleText =
            MediaUi.Muted(
                "--");

        _configPathText =
            MediaUi.Muted(
                "--");

        _lastRunText =
            MediaUi.Muted(
                "--");

        _evidenceText =
            MediaUi.Muted(
                "Recyclarr evidence has not been captured.");

        _targetRows =
            new StackPanel
            {
                Spacing =
                    6
            };

        _configRows =
            new StackPanel
            {
                Spacing =
                    6
            };

        _previewStatusText =
            MediaUi.Muted(
                "Preview has not been run.");

        _previewOutputText =
            MediaUi.Console(
                "Recyclarr preview output will appear here.",
                180,
                560);

        _statusText =
            MediaUi.Dim(
                "Open this page to capture the Recyclarr container and configuration inventory.");

        _refreshButton =
            MediaUi.Primary(
                "Refresh");

        _openConfigButton =
            MediaUi.Compact(
                "Open config folder");

        _previewButton =
            MediaUi.Primary(
                "Preview all");

        _dockerButton =
            MediaUi.Compact(
                "Docker");

        _logsButton =
            MediaUi.Compact(
                "Logs");

        _backButton =
            MediaUi.Compact(
                "Back to applications");

        _refreshButton.Click +=
            (_, _) =>
                RaiseAction(
                    UnifiedRecyclarrAction.Refresh);

        _openConfigButton.Click +=
            (_, _) =>
                RaiseAction(
                    UnifiedRecyclarrAction.OpenConfig);

        _previewButton.Click +=
            (_, _) =>
                RaiseAction(
                    UnifiedRecyclarrAction.Preview);

        _dockerButton.Click +=
            (_, _) =>
                RaiseAction(
                    UnifiedRecyclarrAction.Docker);

        _logsButton.Click +=
            (_, _) =>
                RaiseAction(
                    UnifiedRecyclarrAction.Logs);

        _backButton.Click +=
            (_, _) =>
                RaiseAction(
                    UnifiedRecyclarrAction.Back);

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
                    MediaUi.PageTitle(
                        "Recyclarr"),
                    MediaUi.Subtitle(
                        "Container runtime, configuration targets, read-only preview and synchronization evidence.")
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
                    "RUNTIME",
                    _runtimeText),
                MediaUi.Metric(
                    "VERSION",
                    _versionText),
                MediaUi.Metric(
                    "CONFIG FILES",
                    _configCountText),
                MediaUi.Metric(
                    "TARGETS",
                    _targetCountText));

        var operationButtons =
            new WrapPanel
            {
                Children =
                {
                    _openConfigButton,
                    _previewButton,
                    _dockerButton,
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
                    8,
                    0);
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
                        MediaUi.Subtitle(
                            "Inventory and preview are read-only. Applying a real synchronization remains outside this batch."),
                        operationButtons
                    }
                });

        var targetsCard =
            MediaUi.FlatCard(
                new StackPanel
                {
                    Spacing =
                        8,
                    Children =
                    {
                        MediaUi.Title(
                            "Configuration targets"),
                        MediaUi.Subtitle(
                            "Sonarr and Radarr instance names parsed without exposing API keys."),
                        _targetRows
                    }
                });

        var ownership =
            new Grid
            {
                ColumnDefinitions =
                    new ColumnDefinitions(
                        "Auto,*"),
                RowDefinitions =
                    new RowDefinitions(
                        "Auto,Auto,Auto,Auto,Auto,Auto,Auto"),
                ColumnSpacing =
                    10,
                RowSpacing =
                    7
            };

        AddDefinitionRow(
            ownership,
            0,
            "CONTAINER",
            _containerText);

        AddDefinitionRow(
            ownership,
            1,
            "IMAGE",
            _imageText);

        AddDefinitionRow(
            ownership,
            2,
            "COMPOSE",
            _composeText);

        AddDefinitionRow(
            ownership,
            3,
            "SCHEDULE",
            _scheduleText);

        AddDefinitionRow(
            ownership,
            4,
            "CONFIG",
            _configPathText);

        AddDefinitionRow(
            ownership,
            5,
            "LAST RUN",
            _lastRunText);

        AddDefinitionRow(
            ownership,
            6,
            "EVIDENCE",
            _evidenceText);

        var ownershipCard =
            MediaUi.FlatCard(
                new StackPanel
                {
                    Spacing =
                        8,
                    Children =
                    {
                        MediaUi.Title(
                            "Container & ownership"),
                        MediaUi.Subtitle(
                            "Docker identity, Compose ownership, schedule and config mount."),
                        ownership
                    }
                });

        var targetAndOwnership =
            MediaUi.TwoColumns(
                targetsCard,
                ownershipCard,
                "1.2*,0.8*",
                10);

        var configsCard =
            MediaUi.FlatCard(
                new StackPanel
                {
                    Spacing =
                        8,
                    Children =
                    {
                        MediaUi.Title(
                            "Configuration files"),
                        MediaUi.Subtitle(
                            "Default recyclarr.yml and files under configs/. File contents and credentials are never displayed."),
                        _configRows
                    }
                });

        var previewCard =
            MediaUi.FlatCard(
                new StackPanel
                {
                    Spacing =
                        8,
                    Children =
                    {
                        MediaUi.Title(
                            "Read-only preview"),
                        MediaUi.Subtitle(
                            "Runs recyclarr sync --preview with log output. Preview reads Sonarr and Radarr state but does not apply changes."),
                        _previewStatusText,
                        _previewOutputText
                    }
                });

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
                        targetAndOwnership,
                        configsCard,
                        previewCard,
                        _statusText
                    }
                });

        Update(
            UnifiedRecyclarrState.Empty);
    }

    public event EventHandler<
        UnifiedRecyclarrActionEventArgs>?
        ActionRequested;

    public void Update(
        UnifiedRecyclarrState state)
    {
        _targetText.Text =
            state.Target;

        _freshnessText.Text =
            state.Freshness;

        _runtimeText.Text =
            state.Runtime;

        _versionText.Text =
            state.Version;

        _configCountText.Text =
            state.ConfigFiles;

        _targetCountText.Text =
            state.Targets;

        _containerText.Text =
            state.ContainerName;

        _imageText.Text =
            state.Image;

        _composeText.Text =
            state.Compose;

        _scheduleText.Text =
            state.Schedule;

        _configPathText.Text =
            state.ConfigPath;

        _lastRunText.Text =
            state.LastRun;

        _evidenceText.Text =
            state.Evidence;

        _previewStatusText.Text =
            state.PreviewStatus;

        _previewOutputText.Text =
            state.PreviewOutput;

        _statusText.Text =
            state.Status;

        _refreshButton.IsEnabled =
            state.CanRefresh;

        _openConfigButton.IsEnabled =
            state.CanOpenConfig;

        _previewButton.IsEnabled =
            state.CanPreview;

        _dockerButton.IsEnabled =
            state.CanOpenDocker;

        _logsButton.IsEnabled =
            state.CanOpenLogs;

        _backButton.IsVisible =
            state.ShowBack;

        PopulateTargetRows(
            state.TargetRows);

        PopulateConfigRows(
            state.ConfigRows);
    }

    public void SetStatus(
        string status)
    {
        _statusText.Text =
            status;
    }

    private void PopulateTargetRows(
        IReadOnlyList<UnifiedRecyclarrTargetRow> rows)
    {
        _targetRows.Children.Clear();

        if (rows.Count == 0)
        {
            _targetRows.Children.Add(
                MediaUi.EmptyState(
                    "No readable Recyclarr targets",
                    "Check the container config mount and file permissions. GraveOps does not require an HTTP endpoint for this application."));

            return;
        }

        foreach (var row in rows)
        {
            _targetRows.Children.Add(
                Row(
                    row.Service,
                    row.Instance,
                    row.ConfigFile,
                    row.Endpoint));
        }
    }

    private void PopulateConfigRows(
        IReadOnlyList<UnifiedRecyclarrConfigFileRow> rows)
    {
        _configRows.Children.Clear();

        if (rows.Count == 0)
        {
            _configRows.Children.Add(
                MediaUi.EmptyState(
                    "No readable Recyclarr configuration files",
                    "The container may have no local config, the mount may be unreadable, or its YAML may not define Sonarr/Radarr instances."));

            return;
        }

        foreach (var row in rows)
        {
            _configRows.Children.Add(
                Row(
                    row.File,
                    row.RelativePath,
                    row.Size,
                    row.Targets));
        }
    }

    private static Border Row(
        string first,
        string second,
        string third,
        string fourth)
    {
        var grid =
            new Grid
            {
                ColumnDefinitions =
                    new ColumnDefinitions(
                        "0.8*,1.1*,1.1*,1.2*"),
                ColumnSpacing =
                    8
            };

        grid.Children.Add(
            MediaUi.Cell(
                first,
                0,
                strong:
                    true));

        grid.Children.Add(
            MediaUi.Cell(
                second,
                1));

        grid.Children.Add(
            MediaUi.Cell(
                third,
                2));

        grid.Children.Add(
            MediaUi.Cell(
                fourth,
                3));

        return MediaUi.Inset(
            grid,
            8);
    }

    private static void AddDefinitionRow(
        Grid grid,
        int row,
        string label,
        Control value)
    {
        var labelBlock =
            MediaUi.Eyebrow(
                label);

        Grid.SetRow(
            labelBlock,
            row);

        Grid.SetRow(
            value,
            row);

        Grid.SetColumn(
            value,
            1);

        grid.Children.Add(
            labelBlock);

        grid.Children.Add(
            value);
    }

    private void RaiseAction(
        UnifiedRecyclarrAction action)
    {
        ActionRequested?.Invoke(
            this,
            new UnifiedRecyclarrActionEventArgs(
                action));
    }
}
