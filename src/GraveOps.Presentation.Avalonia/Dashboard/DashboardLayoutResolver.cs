namespace GraveOps.Presentation.Avalonia.Dashboard;

public static class DashboardLayoutResolver
{
    public static IReadOnlyList<DashboardCardPreference> Resolve(
        IReadOnlyList<UnifiedDashboardCard> cards,
        IReadOnlyList<DashboardCardPreference>? saved)
    {
        ArgumentNullException.ThrowIfNull(cards);

        var cardsByKey =
            cards.ToDictionary(
                card => card.Key,
                StringComparer.OrdinalIgnoreCase);

        var resolved =
            (saved ?? Array.Empty<DashboardCardPreference>())
                .Where(item =>
                    cardsByKey.ContainsKey(
                        item.Key))
                .GroupBy(
                    item => item.Key,
                    StringComparer.OrdinalIgnoreCase)
                .Select(group =>
                    group.First())
                .OrderBy(item =>
                    item.Order)
                .Select(item =>
                    item with
                    {
                        Key =
                            cardsByKey[
                                    item.Key]
                                .Key
                    })
                .ToList();

        foreach (var card in cards)
        {
            if (resolved.Any(item =>
                    item.Key.Equals(
                        card.Key,
                        StringComparison.OrdinalIgnoreCase)))
            {
                continue;
            }

            resolved.Add(
                new DashboardCardPreference(
                    card.Key,
                    card.DefaultVisible,
                    resolved.Count));
        }

        return resolved
            .Select(
                (item, index) =>
                    item with
                    {
                        Order = index
                    })
            .ToArray();
    }

    public static IReadOnlyList<UnifiedDashboardCard> VisibleCards(
        IReadOnlyList<UnifiedDashboardCard> cards,
        IReadOnlyList<DashboardCardPreference> layout)
    {
        var cardsByKey =
            cards.ToDictionary(
                card => card.Key,
                StringComparer.OrdinalIgnoreCase);

        return layout
            .Where(item => item.IsVisible)
            .OrderBy(item => item.Order)
            .Select(item =>
                cardsByKey.TryGetValue(
                    item.Key,
                    out var card)
                    ? card
                    : null)
            .Where(card => card is not null)
            .Cast<UnifiedDashboardCard>()
            .ToArray();
    }
}