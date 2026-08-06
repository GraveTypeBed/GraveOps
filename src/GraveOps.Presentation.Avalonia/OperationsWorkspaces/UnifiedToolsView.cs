using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;

namespace GraveOps.Presentation.Avalonia.OperationsWorkspaces;

public sealed class UnifiedToolsView :
    UserControl
{
    private readonly TextBlock _terminalStatus;
    private readonly Button _homeTerminal;
    private readonly Button _repositoryTerminal;
    private readonly Button _configTerminal;
    private readonly Button _diagnosticsTerminal;
    private readonly Button _sshTerminal;

    private readonly TextBlock _diagnosticsStatus;
    private readonly Button _createDiagnostics;
    private readonly TextBox _validationOutput;
    private readonly Button _runValidation;

    private readonly TextBox _filesPath;
    private readonly StackPanel _files;
    private readonly TextBlock _filesStatus;
    private readonly Button _filesParent;
    private readonly Button _filesRefresh;
    private readonly Button _filesOpen;
    private readonly Button _sftp;

    private readonly StackPanel _scripts;
    private readonly TextBox _scriptOutput;
    private readonly TextBlock _scriptStatus;
    private readonly Button _runScript;
    private readonly Button _copyScript;

    private readonly TextBox _updateOutput;
    private readonly TextBlock _updateStatus;
    private readonly Button _captureUpdates;

    private readonly StackPanel _parity;
    private readonly TextBlock _paritySummary;
    private readonly TextBlock _parityTitle;
    private readonly TextBox _parityDetail;

    private UnifiedToolsState _state =
        UnifiedToolsState.Empty;

    private UnifiedFileRow? _selectedFile;
    private UnifiedScriptRow? _selectedScript;
    private UnifiedParityRow? _selectedParity;
    private bool _suppressFilesPath;
    private bool _filesPathDirty;

    public UnifiedToolsView()
    {
        HorizontalAlignment =
            HorizontalAlignment.Stretch;
        VerticalAlignment =
            VerticalAlignment.Stretch;

        _terminalStatus =
            OperationsUi.Muted(
                "No terminal handoff run.");

        _homeTerminal =
            new Button
            {
                Content =
                    "Home terminal"
            };

        _repositoryTerminal =
            new Button
            {
                Content =
                    "Repository terminal"
            };

        _configTerminal =
            new Button
            {
                Content =
                    "Config terminal"
            };

        _diagnosticsTerminal =
            new Button
            {
                Content =
                    "Diagnostics terminal"
            };

        _sshTerminal =
            new Button
            {
                Content =
                    "SSH active target",
                Classes =
                {
                    "primary"
                }
            };

        _diagnosticsStatus =
            OperationsUi.Dim(
                "Ready to export.");

        _createDiagnostics =
            new Button
            {
                Content =
                    "Create diagnostics bundle",
                Classes =
                {
                    "primary"
                }
            };

        _validationOutput =
            OperationsUi.Console(
                "Validation has not been run.",
                220,
                430);

        _runValidation =
            new Button
            {
                Content =
                    "Run validation"
            };

        _filesPath =
            new TextBox
            {
                PlaceholderText =
                    "Local path"
            };

        _files =
            new StackPanel
            {
                Spacing =
                    3
            };

        _filesStatus =
            OperationsUi.Dim(
                "Ready.");

        _filesParent =
            new Button
            {
                Content =
                    "Parent"
            };

        _filesRefresh =
            new Button
            {
                Content =
                    "Refresh"
            };

        _filesOpen =
            OperationsUi.Compact(
                "Open selected");

        _sftp =
            new Button
            {
                Content =
                    "SFTP active target",
                Classes =
                {
                    "primary"
                }
            };

        _scripts =
            new StackPanel
            {
                Spacing =
                    3
            };

        _scriptOutput =
            OperationsUi.Console(
                "Select a script to inspect or run.",
                220,
                430);

        _scriptStatus =
            OperationsUi.Dim(
                "Script library ready.");

        _runScript =
            new Button
            {
                Content =
                    "Run selected",
                Classes =
                {
                    "primary"
                }
            };

        _copyScript =
            new Button
            {
                Content =
                    "Copy command"
            };

        _updateOutput =
            OperationsUi.Console(
                "Update inventory has not been captured.",
                230,
                460);

        _updateStatus =
            OperationsUi.Dim(
                "Manual read-only capture only.");

        _captureUpdates =
            new Button
            {
                Content =
                    "Capture update inventory",
                Classes =
                {
                    "primary"
                }
            };

        _parity =
            new StackPanel
            {
                Spacing =
                    3
            };

        _paritySummary =
            OperationsUi.Dim(
                "Parity matrix loading.");

        _parityTitle =
            OperationsUi.Subtitle(
                "No capability selected");

        _parityDetail =
            OperationsUi.Console(
                "Select a capability to inspect Linux and Windows implementation evidence.",
                160,
                330);

        WireEvents();

        Content =
            BuildWorkspace();

        Update(
            UnifiedToolsState.Empty);
    }

    public event EventHandler<UnifiedTerminalActionRequestedEventArgs>?
        TerminalActionRequested;

    public event EventHandler?
        DiagnosticsRequested;

    public event EventHandler?
        ValidationRequested;

    public event EventHandler<UnifiedFilesActionRequestedEventArgs>?
        FilesActionRequested;

    public event EventHandler<UnifiedScriptActionRequestedEventArgs>?
        ScriptActionRequested;

    public event EventHandler?
        UpdateRequested;

    public event EventHandler<UnifiedTextCopyRequestedEventArgs>?
        CopyRequested;

    public void Update(
        UnifiedToolsState state)
    {
        var selectedFile =
            _selectedFile?.Key;
        var selectedScript =
            _selectedScript?.Key;
        var selectedParity =
            _selectedParity?.Key;

        _state =
            state ?? UnifiedToolsState.Empty;

        _terminalStatus.Text =
            _state.TerminalStatus;

        _diagnosticsStatus.Text =
            _state.DiagnosticsStatus;

        _validationOutput.Text =
            _state.ValidationOutput;

        _filesStatus.Text =
            _state.FilesStatus;

        _scriptOutput.Text =
            _state.ScriptOutput;

        _scriptStatus.Text =
            _state.ScriptStatus;

        _updateOutput.Text =
            _state.UpdateOutput;

        _updateStatus.Text =
            _state.UpdateStatus;

        _paritySummary.Text =
            _state.ParitySummary;

        if (!_filesPathDirty)
        {
            _suppressFilesPath =
                true;

            _filesPath.Text =
                _state.FilesPath;

            _suppressFilesPath =
                false;
        }

        _selectedFile =
            _state.Files.FirstOrDefault(row =>
                row.Key.Equals(
                    selectedFile,
                    StringComparison.OrdinalIgnoreCase)) ??
            _state.Files.FirstOrDefault();

        _selectedScript =
            _state.Scripts.FirstOrDefault(row =>
                row.Key.Equals(
                    selectedScript,
                    StringComparison.OrdinalIgnoreCase)) ??
            _state.Scripts.FirstOrDefault();

        _selectedParity =
            _state.Parity.FirstOrDefault(row =>
                row.Key.Equals(
                    selectedParity,
                    StringComparison.OrdinalIgnoreCase)) ??
            _state.Parity.FirstOrDefault();

        SetCapabilityState();
        RenderFiles();
        RenderScripts();
        RenderParity();
    }

    private void WireEvents()
    {
        _homeTerminal.Click +=
            (_, _) =>
                RequestTerminal(
                    UnifiedTerminalAction.Home);

        _repositoryTerminal.Click +=
            (_, _) =>
                RequestTerminal(
                    UnifiedTerminalAction.Repository);

        _configTerminal.Click +=
            (_, _) =>
                RequestTerminal(
                    UnifiedTerminalAction.Config);

        _diagnosticsTerminal.Click +=
            (_, _) =>
                RequestTerminal(
                    UnifiedTerminalAction.Diagnostics);

        _sshTerminal.Click +=
            (_, _) =>
                RequestTerminal(
                    UnifiedTerminalAction.Ssh);

        _createDiagnostics.Click +=
            (_, _) =>
                DiagnosticsRequested?.Invoke(
                    this,
                    EventArgs.Empty);

        _runValidation.Click +=
            (_, _) =>
                ValidationRequested?.Invoke(
                    this,
                    EventArgs.Empty);

        _filesPath.TextChanged +=
            (_, _) =>
            {
                if (!_suppressFilesPath)
                    _filesPathDirty = true;
            };

        _filesRefresh.Click +=
            (_, _) =>
                RequestFiles(
                    UnifiedFilesAction.Refresh);

        _filesParent.Click +=
            (_, _) =>
                RequestFiles(
                    UnifiedFilesAction.Parent);

        _filesOpen.Click +=
            (_, _) =>
                RequestFiles(
                    UnifiedFilesAction.OpenSelected);

        _sftp.Click +=
            (_, _) =>
                RequestFiles(
                    UnifiedFilesAction.Sftp);

        _runScript.Click +=
            (_, _) =>
        {
            if (_selectedScript is null)
                return;

            ScriptActionRequested?.Invoke(
                this,
                new UnifiedScriptActionRequestedEventArgs(
                    UnifiedScriptAction.Run,
                    _selectedScript));
        };

        _copyScript.Click +=
            (_, _) =>
        {
            if (_selectedScript is null)
                return;

            CopyRequested?.Invoke(
                this,
                new UnifiedTextCopyRequestedEventArgs(
                    _selectedScript.Command,
                    "Operator script command copied."));
        };

        _captureUpdates.Click +=
            (_, _) =>
                UpdateRequested?.Invoke(
                    this,
                    EventArgs.Empty);
    }

    private Control BuildWorkspace()
    {
        var content =
            new StackPanel
            {
                Spacing =
                    8,
                Margin =
                    new Thickness(
                        0,
                        0,
                        4,
                        4)
            };

        content.Children.Add(
            new StackPanel
            {
                Children =
                {
                    OperationsUi.Title(
                        "Operator workspace",
                        18),
                    OperationsUi.Subtitle(
                        "Terminal, diagnostics, files, curated scripts, update inventory and Windows-to-Linux parity.")
                }
            });

        content.Children.Add(
            new TabControl
            {
                ItemsSource =
                    new object[]
                    {
                        new TabItem
                        {
                            Header =
                                "Terminal",
                            Content =
                                BuildTerminalTab()
                        },
                        new TabItem
                        {
                            Header =
                                "Diagnostics",
                            Content =
                                BuildDiagnosticsTab()
                        },
                        new TabItem
                        {
                            Header =
                                "Files / SFTP",
                            Content =
                                BuildFilesTab()
                        },
                        new TabItem
                        {
                            Header =
                                "Script Library",
                            Content =
                                BuildScriptsTab()
                        },
                        new TabItem
                        {
                            Header =
                                "Updates",
                            Content =
                                BuildUpdatesTab()
                        },
                        new TabItem
                        {
                            Header =
                                "Parity",
                            Content =
                                BuildParityTab()
                        }
                    }
            });

        return
            OperationsUi.Scroll(
                content);
    }

    private Control BuildTerminalTab()
    {
        var buttons =
            new WrapPanel
            {
                Children =
                {
                    _homeTerminal,
                    _repositoryTerminal,
                    _configTerminal,
                    _diagnosticsTerminal,
                    _sshTerminal
                }
            };

        foreach (var child in
                 buttons.Children)
        {
            child.Margin =
                new Thickness(
                    0,
                    0,
                    8,
                    8);
        }

        var status =
            OperationsUi.Inset(
                _terminalStatus);

        var grid =
            new Grid
            {
                Margin =
                    new Thickness(
                        10),
                RowDefinitions =
                    new RowDefinitions(
                        "Auto,Auto,*"),
                RowSpacing =
                    10,
                Children =
                {
                    new StackPanel
                    {
                        Children =
                        {
                            OperationsUi.Title(
                                "Terminal handoff"),
                            OperationsUi.Subtitle(
                                "Open a local terminal in a trusted GraveOps path or use the active remote profile for SSH.")
                        }
                    },
                    buttons,
                    status
                }
            };

        Grid.SetRow(
            buttons,
            1);

        Grid.SetRow(
            status,
            2);

        return grid;
    }

    private Control BuildDiagnosticsTab()
    {
        var left =
            OperationsUi.Module(
                new StackPanel
                {
                    Spacing =
                        7,
                    Children =
                    {
                        new StackPanel
                        {
                            Children =
                            {
                                OperationsUi.Title(
                                    "Diagnostics export"),
                                OperationsUi.Subtitle(
                                    "Creates a redacted local ZIP without configuration contents, API keys or passwords.")
                            }
                        },
                        OperationsUi.Inset(
                            OperationsUi.Muted(
                                "Home paths, IP addresses and common secret patterns are redacted. Browser data and media files are excluded.")),
                        _createDiagnostics,
                        _diagnosticsStatus
                    }
                });

        var right =
            OperationsUi.Module(
                new Grid
                {
                    RowDefinitions =
                        new RowDefinitions(
                            "Auto,Auto,*"),
                    RowSpacing =
                        7,
                    Children =
                    {
                        new StackPanel
                        {
                            Children =
                            {
                                OperationsUi.Title(
                                    "Read-only validation"),
                                OperationsUi.Subtitle(
                                    "Checks paths, JSON stores, repository state and required launchers without changing services or policies.")
                            }
                        },
                        _runValidation,
                        _validationOutput
                    }
                });

        var rightGrid =
            (Grid)right.Child!;

        Grid.SetRow(
            _runValidation,
            1);

        Grid.SetRow(
            _validationOutput,
            2);

        Grid.SetColumn(
            right,
            1);

        return
            new Grid
            {
                Margin =
                    new Thickness(
                        10),
                ColumnDefinitions =
                    new ColumnDefinitions(
                        "1.05*,0.95*"),
                ColumnSpacing =
                    8,
                Children =
                {
                    left,
                    right
                }
            };
    }

    private Control BuildFilesTab()
    {
        var controls =
            new Grid
            {
                ColumnDefinitions =
                    new ColumnDefinitions(
                        "*,Auto,Auto,Auto"),
                ColumnSpacing =
                    7,
                Children =
                {
                    _filesPath,
                    _filesParent,
                    _filesRefresh,
                    _sftp
                }
            };

        Grid.SetColumn(
            _filesParent,
            1);

        Grid.SetColumn(
            _filesRefresh,
            2);

        Grid.SetColumn(
            _sftp,
            3);

        var list =
            OperationsUi.Module(
                OperationsUi.Scroll(
                    _files,
                    390));

        var footer =
            new Grid
            {
                ColumnDefinitions =
                    new ColumnDefinitions(
                        "*,Auto"),
                Children =
                {
                    _filesStatus,
                    _filesOpen
                }
            };

        Grid.SetColumn(
            _filesOpen,
            1);

        var grid =
            new Grid
            {
                Margin =
                    new Thickness(
                        10),
                RowDefinitions =
                    new RowDefinitions(
                        "Auto,Auto,*,Auto"),
                RowSpacing =
                    8,
                Children =
                {
                    new StackPanel
                    {
                        Children =
                        {
                            OperationsUi.Title(
                                "Files / SFTP"),
                            OperationsUi.Subtitle(
                                "Browse local paths or hand off the active remote profile to an SFTP terminal without storing a new credential.")
                        }
                    },
                    controls,
                    list,
                    footer
                }
            };

        Grid.SetRow(
            controls,
            1);

        Grid.SetRow(
            list,
            2);

        Grid.SetRow(
            footer,
            3);

        return grid;
    }

    private Control BuildScriptsTab()
    {
        var list =
            OperationsUi.Module(
                new Grid
                {
                    RowDefinitions =
                        new RowDefinitions(
                            "Auto,*,Auto"),
                    RowSpacing =
                        8,
                    Children =
                    {
                        new StackPanel
                        {
                            Children =
                            {
                                OperationsUi.Title(
                                    "Curated scripts"),
                                OperationsUi.Subtitle(
                                    "Read-only operator commands. Mutating additions remain Safe-Mode and confirmation gated.")
                            }
                        },
                        OperationsUi.Scroll(
                            _scripts,
                            350),
                        new WrapPanel
                        {
                            Children =
                            {
                                _runScript,
                                _copyScript
                            }
                        }
                    }
                });

        var listGrid =
            (Grid)list.Child!;

        Grid.SetRow(
            listGrid.Children[1],
            1);

        Grid.SetRow(
            listGrid.Children[2],
            2);

        var output =
            OperationsUi.Module(
                new Grid
                {
                    RowDefinitions =
                        new RowDefinitions(
                            "Auto,*,Auto"),
                    RowSpacing =
                        8,
                    Children =
                    {
                        OperationsUi.Title(
                            "Output"),
                        _scriptOutput,
                        _scriptStatus
                    }
                });

        var outputGrid =
            (Grid)output.Child!;

        Grid.SetRow(
            _scriptOutput,
            1);

        Grid.SetRow(
            _scriptStatus,
            2);

        Grid.SetColumn(
            output,
            1);

        return
            new Grid
            {
                Margin =
                    new Thickness(
                        10),
                ColumnDefinitions =
                    new ColumnDefinitions(
                        "0.75*,1.25*"),
                ColumnSpacing =
                    8,
                Children =
                {
                    list,
                    output
                }
            };
    }

    private Control BuildUpdatesTab()
    {
        var grid =
            new Grid
            {
                Margin =
                    new Thickness(
                        10),
                RowDefinitions =
                    new RowDefinitions(
                        "Auto,Auto,*,Auto"),
                RowSpacing =
                    8,
                Children =
                {
                    new StackPanel
                    {
                        Children =
                        {
                            OperationsUi.Title(
                                "Read-only update inventory"),
                            OperationsUi.Subtitle(
                                "Reports package, container-image and .NET state. GraveOps never installs an update from this view.")
                        }
                    },
                    _captureUpdates,
                    _updateOutput,
                    _updateStatus
                }
            };

        Grid.SetRow(
            _captureUpdates,
            1);

        Grid.SetRow(
            _updateOutput,
            2);

        Grid.SetRow(
            _updateStatus,
            3);

        return grid;
    }

    private Control BuildParityTab()
    {
        var list =
            OperationsUi.Module(
                OperationsUi.Scroll(
                    _parity,
                    360));

        var detail =
            OperationsUi.Module(
                new Grid
                {
                    RowDefinitions =
                        new RowDefinitions(
                            "Auto,*"),
                    RowSpacing =
                        7,
                    Children =
                    {
                        _parityTitle,
                        _parityDetail
                    }
                });

        var detailGrid =
            (Grid)detail.Child!;

        Grid.SetRow(
            _parityDetail,
            1);

        Grid.SetColumn(
            detail,
            1);

        var body =
            new Grid
            {
                ColumnDefinitions =
                    new ColumnDefinitions(
                        "1.1*,0.9*"),
                ColumnSpacing =
                    8,
                Children =
                {
                    list,
                    detail
                }
            };

        var grid =
            new Grid
            {
                Margin =
                    new Thickness(
                        10),
                RowDefinitions =
                    new RowDefinitions(
                        "Auto,*,Auto"),
                RowSpacing =
                    8,
                Children =
                {
                    new StackPanel
                    {
                        Children =
                        {
                            OperationsUi.Title(
                                "Windows-to-Linux capability parity"),
                            OperationsUi.Subtitle(
                                "Linux identity, telemetry, logging and provider behavior remains authoritative.")
                        }
                    },
                    body,
                    _paritySummary
                }
            };

        Grid.SetRow(
            body,
            1);

        Grid.SetRow(
            _paritySummary,
            2);

        return grid;
    }

    private void SetCapabilityState()
    {
        _homeTerminal.IsEnabled =
            _state.CanOpenLocalTerminal;

        _repositoryTerminal.IsEnabled =
            _state.CanOpenLocalTerminal;

        _configTerminal.IsEnabled =
            _state.CanOpenLocalTerminal;

        _diagnosticsTerminal.IsEnabled =
            _state.CanOpenLocalTerminal;

        _sshTerminal.IsEnabled =
            _state.CanOpenSsh;

        _createDiagnostics.IsEnabled =
            _state.CanCreateDiagnostics;

        _runValidation.IsEnabled =
            _state.CanRunValidation;

        _filesPath.IsEnabled =
            _state.CanBrowseFiles;

        _filesParent.IsEnabled =
            _state.CanBrowseFiles;

        _filesRefresh.IsEnabled =
            _state.CanBrowseFiles;

        _filesOpen.IsEnabled =
            _state.CanBrowseFiles &&
            _selectedFile is not null;

        _sftp.IsEnabled =
            _state.CanOpenSftp;

        _runScript.IsEnabled =
            _selectedScript?.CanRun ==
            true;

        _copyScript.IsEnabled =
            _selectedScript is not null;

        _captureUpdates.IsEnabled =
            _state.CanCaptureUpdates;
    }

    private void RenderFiles()
    {
        _files.Children.Clear();

        foreach (var row in
                 _state.Files)
        {
            var grid =
                new Grid
                {
                    ColumnDefinitions =
                        new ColumnDefinitions(
                            "*,100,100,150"),
                    ColumnSpacing =
                        8,
                    Children =
                    {
                        OperationsUi.Cell(
                            row.Name,
                            0,
                            true),
                        OperationsUi.Cell(
                            row.Kind,
                            1,
                            false,
                            "muted"),
                        OperationsUi.Cell(
                            row.Size,
                            2,
                            false,
                            "muted"),
                        OperationsUi.Cell(
                            row.Modified,
                            3,
                            false,
                            "dim")
                    }
                };

            var button =
                OperationsUi.RowButton(
                    grid);

            button.DoubleTapped +=
                (_, _) =>
                {
                    _selectedFile =
                        row;

                    if (row.IsDirectory)
                    {
                        _filesPath.Text =
                            row.FullPath;
                        RequestFiles(
                            UnifiedFilesAction.Refresh);
                    }
                    else
                    {
                        RequestFiles(
                            UnifiedFilesAction.OpenSelected);
                    }
                };

            button.Click +=
                (_, _) =>
                {
                    _selectedFile =
                        row;
                    _filesOpen.IsEnabled =
                        _state.CanBrowseFiles;
                };

            _files.Children.Add(
                button);
        }

        if (_state.Files.Count == 0)
        {
            _files.Children.Add(
                OperationsUi.Muted(
                    "No local file entries are available."));
        }

        _filesOpen.IsEnabled =
            _state.CanBrowseFiles &&
            _selectedFile is not null;
    }

    private void RenderScripts()
    {
        _scripts.Children.Clear();

        foreach (var row in
                 _state.Scripts)
        {
            var button =
                OperationsUi.RowButton(
                    new StackPanel
                    {
                        Spacing =
                            3,
                        Children =
                        {
                            new TextBlock
                            {
                                Text =
                                    row.Name,
                                FontWeight =
                                    global::Avalonia.Media.FontWeight.SemiBold
                            },
                            OperationsUi.Muted(
                                row.Description)
                        }
                    });

            button.Click +=
                (_, _) =>
                {
                    _selectedScript =
                        row;

                    _scriptOutput.Text =
                        $"{row.Description}\n\n$ {row.Command}";

                    _runScript.IsEnabled =
                        row.CanRun;

                    _copyScript.IsEnabled =
                        true;
                };

            _scripts.Children.Add(
                button);
        }

        if (_state.Scripts.Count == 0)
        {
            _scripts.Children.Add(
                OperationsUi.Muted(
                    "No curated scripts are available."));
        }

        _runScript.IsEnabled =
            _selectedScript?.CanRun ==
            true;

        _copyScript.IsEnabled =
            _selectedScript is not null;
    }

    private void RenderParity()
    {
        _parity.Children.Clear();

        foreach (var row in
                 _state.Parity)
        {
            var grid =
                new Grid
                {
                    ColumnDefinitions =
                        new ColumnDefinitions(
                            "180,160,*,170"),
                    ColumnSpacing =
                        8,
                    Children =
                    {
                        OperationsUi.Cell(
                            row.Capability,
                            0,
                            true),
                        OperationsUi.Cell(
                            row.Classification,
                            1),
                        OperationsUi.Cell(
                            row.LinuxImplementation,
                            2),
                        OperationsUi.Cell(
                            row.WindowsReference,
                            3,
                            false,
                            "dim")
                    }
                };

            var button =
                OperationsUi.RowButton(
                    grid);

            button.Click +=
                (_, _) =>
                {
                    _selectedParity =
                        row;
                    RenderParityDetail();
                };

            _parity.Children.Add(
                button);
        }

        if (_state.Parity.Count == 0)
        {
            _parity.Children.Add(
                OperationsUi.Muted(
                    "No parity entries are available."));
        }

        RenderParityDetail();
    }

    private void RenderParityDetail()
    {
        if (_selectedParity is null)
        {
            _parityTitle.Text =
                "No capability selected";

            _parityDetail.Text =
                "Select a capability to inspect Linux and Windows implementation evidence.";

            return;
        }

        _parityTitle.Text =
            $"{_selectedParity.Capability} | " +
            _selectedParity.Classification;

        _parityDetail.Text =
            "Windows reference\n" +
            _selectedParity.WindowsReference +
            "\n\nLinux implementation\n" +
            _selectedParity.LinuxImplementation +
            "\n\nEvidence\n" +
            _selectedParity.Evidence;
    }

    private void RequestTerminal(
        UnifiedTerminalAction action)
    {
        TerminalActionRequested?.Invoke(
            this,
            new UnifiedTerminalActionRequestedEventArgs(
                action));
    }

    private void RequestFiles(
        UnifiedFilesAction action)
    {
        FilesActionRequested?.Invoke(
            this,
            new UnifiedFilesActionRequestedEventArgs(
                action,
                _filesPath.Text ??
                string.Empty,
                _selectedFile));

        if (action is
            UnifiedFilesAction.Refresh or
            UnifiedFilesAction.Parent)
        {
            _filesPathDirty =
                false;
        }
    }
}
