using System.Collections;
using System.Globalization;
using System.Reflection;

namespace GraveOps.Presentation.Avalonia.MediaWorkspaces;

public static class LegacyMediaProjection
{
    public static string Text(
        object? value,
        params string[] propertyNames)
    {
        if (value is null)
            return string.Empty;

        foreach (var propertyName in
                 propertyNames)
        {
            var property =
                value.GetType()
                    .GetProperty(
                        propertyName,
                        BindingFlags.Instance |
                        BindingFlags.Public |
                        BindingFlags.IgnoreCase);

            if (property is null)
                continue;

            var propertyValue =
                property.GetValue(
                    value);

            if (propertyValue is null)
                continue;

            return Convert.ToString(
                       propertyValue,
                       CultureInfo.InvariantCulture) ??
                   string.Empty;
        }

        return Convert.ToString(
                   value,
                   CultureInfo.InvariantCulture) ??
               string.Empty;
    }

    public static bool Bool(
        object? value,
        bool fallback,
        params string[] propertyNames)
    {
        if (value is null)
            return fallback;

        foreach (var propertyName in
                 propertyNames)
        {
            var property =
                value.GetType()
                    .GetProperty(
                        propertyName,
                        BindingFlags.Instance |
                        BindingFlags.Public |
                        BindingFlags.IgnoreCase);

            if (property is null)
                continue;

            var propertyValue =
                property.GetValue(
                    value);

            if (propertyValue is bool boolean)
                return boolean;

            if (bool.TryParse(
                    Convert.ToString(
                        propertyValue,
                        CultureInfo.InvariantCulture),
                    out var parsed))
            {
                return parsed;
            }
        }

        return fallback;
    }

    public static IReadOnlyList<object>
        Items(
            object? value)
    {
        if (value is null ||
            value is string)
        {
            return Array.Empty<object>();
        }

        if (value is not IEnumerable enumerable)
            return Array.Empty<object>();

        var rows =
            new List<object>();

        foreach (var item in enumerable)
        {
            if (item is not null)
                rows.Add(item);
        }

        return rows;
    }

    public static IReadOnlyList<object>
        PropertyItems(
            object? value,
            params string[] propertyNames)
    {
        if (value is null)
            return Array.Empty<object>();

        foreach (var propertyName in
                 propertyNames)
        {
            var property =
                value.GetType()
                    .GetProperty(
                        propertyName,
                        BindingFlags.Instance |
                        BindingFlags.Public |
                        BindingFlags.IgnoreCase);

            if (property is null)
                continue;

            return Items(
                property.GetValue(
                    value));
        }

        return Array.Empty<object>();
    }

    public static string First(
        object? value,
        string fallback,
        params string[] propertyNames)
    {
        var text =
            Text(
                value,
                propertyNames);

        return string.IsNullOrWhiteSpace(
            text)
            ? fallback
            : text;
    }
}
