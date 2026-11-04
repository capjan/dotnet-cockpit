using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace DotnetCockpit.Tools;

internal static partial class ToolOutputParser
{
    [GeneratedRegex(@"\s{2,}")]
    private static partial Regex Columns();

    public static DotnetTool[] Parse(string output)
    {
        var lines = output.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var separator = Array.FindIndex(lines, line => line.All(character => character == '-'));
        if (separator < 0) throw new FormatException("The tool list does not contain a table.");
        return lines.Skip(separator + 1).Select(line =>
        {
            var columns = Columns().Split(line, 4);
            if (columns.Length < 3) throw new FormatException("The tool list contains an incomplete row.");
            return new DotnetTool(columns[0], columns[1],
                columns[2].Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries),
                columns.Length == 4 ? columns[3] : null);
        }).ToArray();
    }

    public static ToolSearchResult[] ParseNuGetSearch(JsonElement root)
    {
        if (!root.TryGetProperty("data", out var data) || data.ValueKind != JsonValueKind.Array) return [];
        return data.EnumerateArray()
            .Where(package => package.TryGetProperty("id", out _) && package.TryGetProperty("version", out _))
            .Select(package => new ToolSearchResult(
                package.GetProperty("id").GetString() ?? "",
                package.GetProperty("version").GetString() ?? "",
                package.TryGetProperty("authors", out var authors) && authors.ValueKind == JsonValueKind.Array
                    ? string.Join(", ", authors.EnumerateArray().Select(author => author.GetString()))
                    : "",
                package.TryGetProperty("totalDownloads", out var downloads) && downloads.TryGetInt64(out var count)
                    ? count.ToString(CultureInfo.InvariantCulture)
                    : "",
                package.TryGetProperty("verified", out var verified) && verified.ValueKind == JsonValueKind.True))
            .Where(result => result.PackageId.Length > 0)
            .ToArray();
    }

    public static ToolSearchResult[] ParseSearch(string output)
    {
        var lines = output.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var separator = Array.FindIndex(lines, line => line.All(character => character == '-'));
        if (separator < 0) return [];

        return lines.Skip(separator + 1).Select(line => Columns().Split(line, 5))
            .Where(columns => columns.Length >= 2)
            .Select(columns => new ToolSearchResult(
                columns[0],
                columns[1],
                columns.Length > 2 ? columns[2] : "",
                columns.Length > 3 ? columns[3] : "",
                columns.Length > 4 && columns[4].Equals("x", StringComparison.OrdinalIgnoreCase)))
            .ToArray();
    }
}
