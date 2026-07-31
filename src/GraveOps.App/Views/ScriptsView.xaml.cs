using System.Windows;
using MessageBox = GraveOps.App.Windows.GraveOpsMessageBox;
using System.Windows.Controls;
using GraveOps.App.Models;

namespace GraveOps.App.Views;

public partial class ScriptsView : UserControl
{
    private QuickAction? _editing; private Services.AppServices S => App.Services;
    public ScriptsView() { InitializeComponent(); RiskCombo.SelectedIndex = 0; ServerCombo.ItemsSource = S.Config.Current.Servers; ServerCombo.SelectedItem = S.Config.GetSelectedServer(); Reload(); }
    private void Reload() { ScriptList.ItemsSource = null; ScriptList.ItemsSource = S.Config.Current.Actions.OrderBy(x => x.Category).ThenBy(x => x.Name).ToList(); }
    private void ScriptList_SelectionChanged(object sender, SelectionChangedEventArgs e) { if (ScriptList.SelectedItem is not QuickAction a) return; _editing = a; NameBox.Text = a.Name; CategoryBox.Text = a.Category; CommandBox.Text = a.Command; RiskCombo.SelectedIndex = (int)a.Risk; }
    private void New_Click(object sender, RoutedEventArgs e) { _editing = null; ScriptList.SelectedItem = null; NameBox.Text = ""; CategoryBox.Text = "Custom"; CommandBox.Text = ""; RiskCombo.SelectedIndex = 0; }
    private void Save_Click(object sender, RoutedEventArgs e) { var a = _editing ?? new QuickAction(); a.Name = NameBox.Text.Trim(); a.Category = CategoryBox.Text.Trim(); a.Command = CommandBox.Text; a.Risk = (ActionRisk)Math.Max(0, RiskCombo.SelectedIndex); if (string.IsNullOrWhiteSpace(a.Name) || string.IsNullOrWhiteSpace(a.Command)) { OutputBox.Text = "Name and command are required."; return; } if (_editing is null) S.Config.Current.Actions.Add(a); _editing = a; S.Config.Save(); Reload(); ScriptList.SelectedItem = a; OutputBox.Text = "Saved."; }
    private void Delete_Click(object sender, RoutedEventArgs e) { if (_editing is null) return; if (MessageBox.Show($"Delete script '{_editing.Name}'?", "Delete", MessageBoxButton.YesNo) != MessageBoxResult.Yes) return; S.Config.Current.Actions.RemoveAll(x => x.Id == _editing.Id); S.Config.Save(); _editing = null; Reload(); New_Click(sender, e); }
    private async void Run_Click(object sender, RoutedEventArgs e) { if (_editing is null || ServerCombo.SelectedItem is not ServerProfile p) return; if (_editing.Risk == ActionRisk.Dangerous) { var d = new ConfirmDangerWindow(_editing.Name, _editing.Command, "RUN"); if (d.ShowDialog() != true) return; } OutputBox.Text = "Running..."; try { OutputBox.Text = (await S.Ssh.ExecuteAsync(p, _editing.Command, 600)).Combined; } catch (Exception ex) { OutputBox.Text = ex.ToString(); } }
}
