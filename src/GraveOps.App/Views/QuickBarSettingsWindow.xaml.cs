using System.Windows;
using System.Windows.Controls;
using GraveOps.App.Models;

namespace GraveOps.App.Views;

public partial class QuickBarSettingsWindow : Window
{
    private readonly List<SearchEntry> _pinned = new();
    private readonly List<SearchEntry> _available = new();

    public bool Changed { get; private set; }

    public QuickBarSettingsWindow()
    {
        InitializeComponent();
        Reload();
    }

    private void Reload()
    {
        _pinned.Clear();
        _pinned.AddRange(
            App.Services.Config.Current.FavoriteKeys
                .Select(App.Services.Search.ResolveSemanticKey)
                .Where(x => x is not null)
                .Cast<SearchEntry>());

        RefreshAvailable();
        RefreshPinned();
    }

    private void RefreshPinned()
    {
        PinnedList.ItemsSource = null;
        PinnedList.ItemsSource = _pinned.ToList();
    }

    private void RefreshAvailable()
    {
        var query = SearchBox?.Text ?? "";
        _available.Clear();
        _available.AddRange(
            App.Services.Search.Search(query)
                .Where(x => _pinned.All(p => !string.Equals(p.Key, x.Key, StringComparison.OrdinalIgnoreCase)))
                .GroupBy(x => x.Key, StringComparer.OrdinalIgnoreCase)
                .Select(g => g.First())
                .Take(100));

        AvailableList.ItemsSource = null;
        AvailableList.ItemsSource = _available.ToList();
    }

    private void SearchBox_TextChanged(object sender, TextChangedEventArgs e)
        => RefreshAvailable();

    private void Add_Click(object sender, RoutedEventArgs e)
    {
        if (AvailableList.SelectedItem is not SearchEntry item) return;
        _pinned.Add(item);
        Save();
    }

    private void Remove_Click(object sender, RoutedEventArgs e)
    {
        if (PinnedList.SelectedItem is not SearchEntry item) return;
        _pinned.RemoveAll(x => string.Equals(x.Key, item.Key, StringComparison.OrdinalIgnoreCase));
        Save();
    }

    private void MoveUp_Click(object sender, RoutedEventArgs e)
    {
        if (PinnedList.SelectedItem is not SearchEntry item) return;
        var index = _pinned.FindIndex(x => string.Equals(x.Key, item.Key, StringComparison.OrdinalIgnoreCase));
        if (index <= 0) return;
        (_pinned[index - 1], _pinned[index]) = (_pinned[index], _pinned[index - 1]);
        Save();
        PinnedList.SelectedIndex = index - 1;
    }

    private void MoveDown_Click(object sender, RoutedEventArgs e)
    {
        if (PinnedList.SelectedItem is not SearchEntry item) return;
        var index = _pinned.FindIndex(x => string.Equals(x.Key, item.Key, StringComparison.OrdinalIgnoreCase));
        if (index < 0 || index >= _pinned.Count - 1) return;
        (_pinned[index + 1], _pinned[index]) = (_pinned[index], _pinned[index + 1]);
        Save();
        PinnedList.SelectedIndex = index + 1;
    }

    private void Reset_Click(object sender, RoutedEventArgs e)
    {
        App.Services.Config.Current.FavoriteKeys = new List<string>
        {
            "page:Terminal"
        };
        App.Services.Config.Save();
        Changed = true;
        Reload();
    }

    private void Save()
    {
        App.Services.Config.Current.FavoriteKeys = _pinned.Select(x => x.Key).ToList();
        App.Services.Config.Save();
        Changed = true;
        RefreshPinned();
        RefreshAvailable();
    }

    private void Done_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = true;
        Close();
    }
}