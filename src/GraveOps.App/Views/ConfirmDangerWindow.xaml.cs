using System.Windows;
using MessageBox = GraveOps.App.Windows.GraveOpsMessageBox;

namespace GraveOps.App.Views;

public partial class ConfirmDangerWindow : Window
{
    private readonly string _phrase;
    public ConfirmDangerWindow(string action, string command, string phrase) { InitializeComponent(); _phrase = phrase; ActionText.Text = action; CommandText.Text = command; PromptText.Text = $"Type {_phrase} to confirm:"; }
    private void Cancel_Click(object sender, RoutedEventArgs e) { DialogResult = false; Close(); }
    private void Confirm_Click(object sender, RoutedEventArgs e) { if (!string.Equals(ConfirmBox.Text.Trim(), _phrase, StringComparison.Ordinal)) { MessageBox.Show($"Type {_phrase} exactly."); return; } DialogResult = true; Close(); }
}
