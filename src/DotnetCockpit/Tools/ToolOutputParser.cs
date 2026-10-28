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
}
