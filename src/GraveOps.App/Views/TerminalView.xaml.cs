using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using GraveOps.App.Models;
using GraveOps.App.Services;
using MessageBox = GraveOps.App.Windows.GraveOpsMessageBox;

namespace GraveOps.App.Views;

public partial class TerminalView : UserControl
{
    private sealed class TabState
    {
        public required ITerminalSession Session { get; init; }
        public required TextBox Output { get; init; }
        public required TextBox Input { get; init; }
        public List<string> History { get; } = new();
        public int HistoryIndex { get; set; }
        public bool Running { get; set; }
    }

    private readonly Dictionary<TabItem, TabState> _sessions = new();
    private AppServices S => App.Services;

    private static readonly object HandoffLock = new();
    private static (Guid ServerId, string Path)? PendingSshHandoff;

    public static void QueueSshHandoff(ServerProfile server, string path)
    {
        lock (HandoffLock)
            PendingSshHandoff = (server.Id, string.IsNullOrWhiteSpace(path) ? "/" : path);
    }

    public TerminalView()
    {
        InitializeComponent();
        ServerCombo.ItemsSource = S.Config.Current.Servers;
        ServerCombo.SelectedItem = S.Context.Current ?? S.Config.GetSelectedServer();
        Loaded += TerminalView_Loaded;
        Unloaded += async (_, _) => await CloseAllAsync();
    }

    private async void TerminalView_Loaded(object sender, RoutedEventArgs e)
    {
        (Guid ServerId, string Path)? pending;
        lock (HandoffLock)
        {
            pending = PendingSshHandoff;
            PendingSshHandoff = null;
        }

        if (pending is not { } handoff) return;

        var server = S.Config.Current.Servers.FirstOrDefault(x => x.Id == handoff.ServerId);
        if (server is null)
        {
            MessageBox.Show(
                "The server selected by Files / SFTP is no longer available.",
                "GraveOps Terminal");
            return;
        }

        ServerCombo.SelectedItem = server;
        await AddSessionAsync(
            new SshTerminalSession(S.Ssh, server),
            $"cd -- {ShellQuote(handoff.Path)}");
    }

    private static string ShellQuote(string value)
        => "'" + value.Replace("'", "'\\''") + "'";

    private async void PowerShell_Click(object sender, RoutedEventArgs e)
        => await AddSessionAsync(new LocalTerminalSession("PowerShell", "powershell.exe"));

    private async void Cmd_Click(object sender, RoutedEventArgs e)
        => await AddSessionAsync(new LocalTerminalSession("CMD", "cmd.exe"));

    private async void Ssh_Click(object sender, RoutedEventArgs e)
    {
        if (ServerCombo.SelectedItem is not ServerProfile p)
        {
            MessageBox.Show("Select a saved server first.");
            return;
        }

        await AddSessionAsync(new SshTerminalSession(S.Ssh, p));
    }

    private async Task AddSessionAsync(ITerminalSession session, string? initialCommand = null)
    {
        var output = new TextBox
        {
            IsReadOnly = true,
            AcceptsReturn = true,
            FontFamily = new FontFamily("Cascadia Mono, Consolas"),
            FontSize = 12,
            Background = new SolidColorBrush(Color.FromRgb(5, 9, 15)),
            Foreground = (Brush)FindResource("Text"),
            BorderThickness = new Thickness(0),
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
            TextWrapping = TextWrapping.NoWrap
        };

        var input = new TextBox
        {
            FontFamily = new FontFamily("Cascadia Mono, Consolas"),
            FontSize = 12,
            AcceptsReturn = true,
            AcceptsTab = true,
            TextWrapping = TextWrapping.NoWrap,
            MinHeight = 46,
            MaxHeight = 180,
            MaxLength = 2000000,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
            Padding = new Thickness(8, 6, 8, 6)
        };

        var run = new Button
        {
            Content = "Run Block",
            MinWidth = 92,
            Margin = new Thickness(8, 0, 0, 0),
            Padding = new Thickness(10, 7, 10, 7),
            VerticalAlignment = VerticalAlignment.Stretch
        };
        run.SetResourceReference(StyleProperty, "PrimaryButton");

        var hint = new TextBlock
        {
            Text = "Enter: run single line  |  Shift+Enter: newline  |  Ctrl+Enter: run full block  |  Up/Down: history",
            Foreground = (Brush)FindResource("Dim"),
            FontSize = 9,
            Margin = new Thickness(2, 5, 0, 0)
        };

        var composerGrid = new Grid();
        composerGrid.ColumnDefinitions.Add(new ColumnDefinition());
        composerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        composerGrid.Children.Add(input);
        Grid.SetColumn(run, 1);
        composerGrid.Children.Add(run);

        var composer = new Grid();
        composer.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        composer.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        composer.Children.Add(composerGrid);
        Grid.SetRow(hint, 1);
        composer.Children.Add(hint);

        var root = new Grid();
        root.RowDefinitions.Add(new RowDefinition());
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.Children.Add(output);
        Grid.SetRow(composer, 1);
        root.Children.Add(composer);

        var tab = new TabItem
        {
            Header = session.Title,
            Content = root,
            Padding = new Thickness(10, 5, 10, 5)
        };

        var state = new TabState
        {
            Session = session,
            Output = output,
            Input = input,
            HistoryIndex = 0
        };

        TerminalTabs.Items.Add(tab);
        TerminalTabs.SelectedItem = tab;
        _sessions[tab] = state;

        session.OutputReceived += text => Dispatcher.Invoke(() =>
        {
            if (string.IsNullOrEmpty(text)) return;
            output.AppendText(text);
            output.ScrollToEnd();
        });

        async Task RunComposerAsync()
        {
            if (state.Running) return;

            var text = input.Text;
            if (string.IsNullOrWhiteSpace(text)) return;

            state.Running = true;
            run.IsEnabled = false;

            try
            {
                if (state.History.Count == 0 ||
                    !string.Equals(state.History[^1], text, StringComparison.Ordinal))
                    state.History.Add(text);

                while (state.History.Count > 100)
                    state.History.RemoveAt(0);

                state.HistoryIndex = state.History.Count;
                input.Clear();

                await session.WriteLineAsync(text);
            }
            catch (Exception ex)
            {
                output.AppendText($"ERROR: {ex.Message}{Environment.NewLine}");
                output.ScrollToEnd();
            }
            finally
            {
                state.Running = false;
                run.IsEnabled = true;
                input.Focus();
            }
        }

        run.Click += async (_, _) => await RunComposerAsync();

        input.PreviewKeyDown += async (_, e) =>
        {
            var ctrl = (Keyboard.Modifiers & ModifierKeys.Control) != 0;
            var shift = (Keyboard.Modifiers & ModifierKeys.Shift) != 0;

            if (ctrl && e.Key == Key.Enter)
            {
                e.Handled = true;
                await RunComposerAsync();
                return;
            }

            if (ctrl && e.Key == Key.L)
            {
                e.Handled = true;
                output.Clear();
                return;
            }

            // Once a composer contains more than one line, plain Enter is an
            // editor key. Ctrl+Enter or Run Block executes the complete paste.
            if (e.Key == Key.Enter && !shift && !ctrl &&
                !input.Text.Contains('\n') && !input.Text.Contains('\r'))
            {
                e.Handled = true;
                await RunComposerAsync();
                return;
            }

            if (!ctrl && !shift && e.Key == Key.Up &&
                !input.Text.Contains('\n') && state.History.Count > 0)
            {
                state.HistoryIndex = Math.Max(0, state.HistoryIndex - 1);
                input.Text = state.History[state.HistoryIndex];
                input.CaretIndex = input.Text.Length;
                e.Handled = true;
                return;
            }

            if (!ctrl && !shift && e.Key == Key.Down &&
                !input.Text.Contains('\n') && state.History.Count > 0)
            {
                state.HistoryIndex = Math.Min(state.History.Count, state.HistoryIndex + 1);
                input.Text = state.HistoryIndex >= state.History.Count
                    ? ""
                    : state.History[state.HistoryIndex];
                input.CaretIndex = input.Text.Length;
                e.Handled = true;
            }
        };

        try
        {
            output.Text = $"Connecting {session.Title}...{Environment.NewLine}";
            await session.StartAsync();

            if (!string.IsNullOrWhiteSpace(initialCommand))
                await session.WriteLineAsync(initialCommand);

            input.Focus();
        }
        catch (Exception ex)
        {
            output.AppendText($"ERROR: {ex.Message}{Environment.NewLine}");
        }
    }

    private void Clear_Click(object sender, RoutedEventArgs e)
    {
        if (TerminalTabs.SelectedItem is not TabItem tab ||
            !_sessions.TryGetValue(tab, out var data))
            return;

        data.Output.Clear();
        data.Input.Focus();
    }

    private async void Close_Click(object sender, RoutedEventArgs e)
    {
        if (TerminalTabs.SelectedItem is not TabItem tab ||
            !_sessions.TryGetValue(tab, out var data))
            return;

        await data.Session.StopAsync();
        data.Session.Dispose();
        _sessions.Remove(tab);
        TerminalTabs.Items.Remove(tab);
    }

    private async Task CloseAllAsync()
    {
        foreach (var data in _sessions.Values.ToList())
        {
            try
            {
                await data.Session.StopAsync();
                data.Session.Dispose();
            }
            catch { }
        }

        _sessions.Clear();
    }
}