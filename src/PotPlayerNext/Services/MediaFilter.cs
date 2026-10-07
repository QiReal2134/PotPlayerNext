namespace PotPlayerNext.Services;

public static class MediaFilter
{
    public static IReadOnlyList<MediaItem> Apply(IReadOnlyList<MediaItem> items, string? kind, string query)
    {
        query = query.Trim();
        if (kind is null && query.Length == 0) return items;
        var result = new List<MediaItem>();
        foreach (var item in items)
            if ((kind is null || item.Kind == kind) && item.Name.Contains(query, StringComparison.OrdinalIgnoreCase))
                result.Add(item);
        return result;
    }
}
