using System.Globalization;
using System.IO;
using System.Text;
using ProgramDock.Models;

namespace ProgramDock.Services;

public static class SearchEngine
{
    public static IReadOnlyList<ProgramEntry> Search(
        IEnumerable<ProgramEntry> entries,
        string query,
        string filter)
    {
        var source = entries.Where(entry => !string.IsNullOrWhiteSpace(entry.Path));
        switch (filter)
        {
            case "Favorites":
                source = source.Where(entry => entry.IsFavorite);
                break;
            case "Recent":
                source = source.OrderByDescending(entry => entry.LastLaunchedAt ?? DateTime.MinValue);
                break;
            case "Direct":
                source = source.Where(entry => entry.Category == ProgramCategory.DirectUse);
                break;
            case "System":
                source = source.Where(entry => entry.Category != ProgramCategory.DirectUse);
                break;
            case "Unclassified":
                source = source.Where(entry => entry.Category == ProgramCategory.Unclassified);
                break;
            case "Installed":
                source = source.Where(entry => entry.Source == ProgramSource.Installed);
                break;
            case "Portable":
                source = source.Where(entry => entry.Source != ProgramSource.Installed);
                break;
        }

        var normalizedQuery = Normalize(query);
        if (normalizedQuery.Length == 0)
        {
            if (filter == "Recent")
            {
                return source
                    .OrderByDescending(entry => entry.LastLaunchedAt ?? DateTime.MinValue)
                    .ThenBy(entry => entry.Name, StringComparer.CurrentCultureIgnoreCase)
                    .ToList();
            }

            return source
                .OrderByDescending(entry => entry.IsFavorite)
                .ThenByDescending(entry => entry.LastLaunchedAt ?? DateTime.MinValue)
                .ThenBy(entry => entry.Name, StringComparer.CurrentCultureIgnoreCase)
                .ToList();
        }

        return source
            .Select(entry => (Entry: entry, Rank: Rank(entry, normalizedQuery)))
            .Where(item => item.Rank < int.MaxValue)
            .OrderBy(item => item.Rank)
            .ThenByDescending(item => item.Entry.IsFavorite)
            .ThenBy(item => item.Entry.Name, StringComparer.CurrentCultureIgnoreCase)
            .Select(item => item.Entry)
            .ToList();
    }

    private static int Rank(ProgramEntry entry, string query)
    {
        var name = Normalize(entry.Name);
        var fileName = Normalize(Path.GetFileNameWithoutExtension(entry.Path));
        var path = Normalize(entry.Path);
        var description = Normalize(entry.Description);
        var category = Normalize(entry.CategoryLabel);
        var compactName = Compact(name);

        if (name == query || fileName == query)
        {
            return 0;
        }

        if (name.StartsWith(query, StringComparison.Ordinal) || fileName.StartsWith(query, StringComparison.Ordinal))
        {
            return 1;
        }

        if (name.Contains(query, StringComparison.Ordinal) || fileName.Contains(query, StringComparison.Ordinal))
        {
            return 2;
        }

        if (category.Contains(query, StringComparison.Ordinal))
        {
            return 4;
        }

        if (path.Contains(query, StringComparison.Ordinal) || description.Contains(query, StringComparison.Ordinal))
        {
            return 5;
        }

        if (ContainsInOrder(query, compactName))
        {
            return 3;
        }

        return int.MaxValue;
    }

    private static bool ContainsInOrder(string query, string value)
    {
        if (query.Length == 0 || value.Length == 0)
        {
            return false;
        }

        var queryIndex = 0;
        foreach (var character in value)
        {
            if (character == query[queryIndex])
            {
                queryIndex++;
                if (queryIndex == query.Length)
                {
                    return true;
                }
            }
        }

        return false;
    }

    private static string Normalize(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        var normalized = value.Trim().ToLowerInvariant().Normalize(NormalizationForm.FormD);
        var builder = new StringBuilder(normalized.Length);
        foreach (var character in normalized)
        {
            if (CharUnicodeInfo.GetUnicodeCategory(character) != UnicodeCategory.NonSpacingMark)
            {
                builder.Append(character);
            }
        }

        return builder.ToString().Normalize(NormalizationForm.FormC);
    }

    private static string Compact(string value)
    {
        return string.Concat(value.Where(character => char.IsLetterOrDigit(character)));
    }
}
