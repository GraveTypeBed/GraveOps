using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Media;
using Path = Avalonia.Controls.Shapes.Path;

namespace GraveOps.Presentation.Avalonia.Shell;

internal enum LegacyNavigationNodeKind
{
    Section,
    Group,
    Item
}

internal sealed record LegacyNavigationNode(
    LegacyNavigationNodeKind Kind,
    string Key,
    string Label,
    string Section,
    string? GroupKey,
    Geometry? IconGeometry,
    bool FillIcon,
    Button? SourceButton,
    Panel? SourceGroupPanel);

internal static class LegacyNavigationProjection
{
    public static IReadOnlyList<LegacyNavigationNode> Project(
        ScrollViewer source)
    {
        if (source.Content is not Panel root)
        {
            throw new InvalidOperationException(
                "Legacy navigation source must contain a panel.");
        }

        var result =
            new List<LegacyNavigationNode>();

        var section =
            string.Empty;

        string? pendingGroup =
            null;

        foreach (var child in root.Children)
        {
            if (child is TextBlock heading &&
                heading.Classes.Contains("eyebrow") &&
                !string.IsNullOrWhiteSpace(heading.Text))
            {
                section =
                    heading.Text.Trim();

                result.Add(
                    new LegacyNavigationNode(
                        LegacyNavigationNodeKind.Section,
                        $"section:{section}",
                        section,
                        section,
                        null,
                        null,
                        false,
                        null,
                        null));

                pendingGroup =
                    null;

                continue;
            }

            if (child is Button button &&
                button.Classes.Contains("navGroup") &&
                !string.IsNullOrWhiteSpace(button.Name))
            {
                var label =
                    FindLabel(button);

                if (string.IsNullOrWhiteSpace(label))
                    continue;

                pendingGroup =
                    button.Name;

                result.Add(
                    new LegacyNavigationNode(
                        LegacyNavigationNodeKind.Group,
                        button.Name,
                        label,
                        section,
                        null,
                        null,
                        false,
                        button,
                        null));

                continue;
            }

            if (child is Button navigationButton &&
                navigationButton.Classes.Contains("nav"))
            {
                AddButton(
                    navigationButton,
                    section,
                    null,
                    result);

                pendingGroup =
                    null;

                continue;
            }

            if (child is Panel groupPanel &&
                !string.IsNullOrWhiteSpace(groupPanel.Name) &&
                groupPanel.Name.EndsWith(
                    "NavGroup",
                    StringComparison.Ordinal))
            {
                var groupKey =
                    pendingGroup ??
                    InferGroupKey(
                        groupPanel.Name);

                ProjectGroup(
                    groupPanel,
                    section,
                    groupKey,
                    result);

                var groupIndex =
                    result.FindIndex(
                        item =>
                            item.Kind ==
                                LegacyNavigationNodeKind.Group &&
                            item.Key.Equals(
                                groupKey,
                                StringComparison.Ordinal));

                if (groupIndex >= 0)
                {
                    result[groupIndex] =
                        result[groupIndex] with
                        {
                            SourceGroupPanel =
                                groupPanel
                        };
                }

                pendingGroup =
                    null;
            }
        }

        return result;
    }

    private static void ProjectGroup(
        Panel panel,
        string section,
        string groupKey,
        ICollection<LegacyNavigationNode> result)
    {
        foreach (var button in
                 Descendants<Button>(
                     panel))
        {
            if (!button.Classes.Contains("nav"))
                continue;

            AddButton(
                button,
                section,
                groupKey,
                result);
        }
    }

    private static void AddButton(
        Button button,
        string section,
        string? groupKey,
        ICollection<LegacyNavigationNode> result)
    {
        if (string.IsNullOrWhiteSpace(
                button.Name))
        {
            return;
        }

        var label =
            FindLabel(button);

        if (string.IsNullOrWhiteSpace(label))
            return;

        var icon =
            FindFirst<Path>(
                button);

        result.Add(
            new LegacyNavigationNode(
                LegacyNavigationNodeKind.Item,
                button.Name,
                label,
                section,
                groupKey,
                icon?.Data,
                icon is not null &&
                icon.Fill is not null &&
                icon.StrokeThickness <= 0.1,
                button,
                null));
    }

    private static string FindLabel(
        Button button) =>
        Descendants<TextBlock>(
                button)
            .Select(
                text =>
                    text.Text?.Trim())
            .FirstOrDefault(
                value =>
                    !string.IsNullOrWhiteSpace(
                        value)) ??
        string.Empty;

    private static string InferGroupKey(
        string panelName)
    {
        var prefix =
            panelName.EndsWith(
                "NavGroup",
                StringComparison.Ordinal)
                ? panelName[..^"NavGroup".Length]
                : panelName;

        return
            $"{prefix}GroupButton";
    }

    private static T? FindFirst<T>(
        Control root)
        where T : Control =>
        Descendants<T>(
                root)
            .FirstOrDefault();

    private static IEnumerable<T> Descendants<T>(
        Control root)
        where T : Control
    {
        foreach (var child in
                 ChildControls(
                     root))
        {
            if (child is T match)
                yield return match;

            foreach (var descendant in
                     Descendants<T>(
                         child))
            {
                yield return descendant;
            }
        }
    }

    private static IEnumerable<Control> ChildControls(
        Control control)
    {
        if (control is Panel panel)
        {
            foreach (var child in
                     panel.Children)
            {
                yield return child;
            }

            yield break;
        }

        if (control is Decorator decorator &&
            decorator.Child is Control decorated)
        {
            yield return decorated;
            yield break;
        }

        if (control is ContentControl content &&
            content.Content is Control contentChild)
        {
            yield return contentChild;
        }
    }
}

public sealed record UnifiedShellFooterState(
    string Left,
    string Right);

public sealed class UnifiedShellNavigationRequestedEventArgs(
    string navigationKey)
    : EventArgs
{
    public string NavigationKey { get; } =
        navigationKey;
}

public sealed class UnifiedShellCommandRequestedEventArgs(
    string commandKey)
    : EventArgs
{
    public string CommandKey { get; } =
        commandKey;
}

internal sealed class DelegateObserver<T>(
    Action<T> onNext)
    : IObserver<T>
{
    public void OnCompleted()
    {
    }

    public void OnError(
        Exception error)
    {
    }

    public void OnNext(
        T value) =>
        onNext(
            value);
}