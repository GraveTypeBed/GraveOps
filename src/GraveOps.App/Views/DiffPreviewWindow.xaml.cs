using System.Windows;

namespace GraveOps.App.Views;

public partial class DiffPreviewWindow : Window
{
    public bool Approved { get; private set; }
    public DiffPreviewWindow(string oldText, string newText)
    {
        InitializeComponent();
        var oldLines = oldText.Replace("\r", "").Split('\n'); var newLines = newText.Replace("\r", "").Split('\n');
        var max = Math.Max(oldLines.Length, newLines.Length); var sb = new StringBuilder(); var changed = 0;
        for (var i = 0; i < max; i++)
        {
            var a = i < oldLines.Length ? oldLines[i] : null; var b = i < newLines.Length ? newLines[i] : null;
            if (a == b) continue; changed++;
            if (a is not null) sb.AppendLine($"- {i + 1,4}: {a}");
            if (b is not null) sb.AppendLine($"+ {i + 1,4}: {b}");
        }
        SummaryText.Text = changed == 0 ? "No changed lines." : $"{changed} changed line positions. A local rollback copy will be created before upload.";
        DiffBox.Text = sb.Length == 0 ? "No changes." : sb.ToString().TrimEnd();
    }
    private void Cancel_Click(object sender, RoutedEventArgs e) { DialogResult = false; Close(); }
    private void Save_Click(object sender, RoutedEventArgs e) { Approved = true; DialogResult = true; Close(); }
}