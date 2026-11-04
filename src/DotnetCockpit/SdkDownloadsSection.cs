namespace DotnetCockpit;

internal static class SdkDownloadsSection
{
    internal sealed class SdkVersionComparer : IComparer<string>
    {
        public static SdkVersionComparer Instance { get; } = new();

        public int Compare(string? left, string? right)
        {
            if (ReferenceEquals(left, right)) return 0;
            if (left is null) return -1;
            if (right is null) return 1;

            var leftParts = SplitVersion(left);
            var rightParts = SplitVersion(right);
            for (var i = 0; i < Math.Max(leftParts.Core.Length, rightParts.Core.Length); i++)
            {
                var leftNumber = i < leftParts.Core.Length ? leftParts.Core[i] : 0;
                var rightNumber = i < rightParts.Core.Length ? rightParts.Core[i] : 0;
                var comparison = leftNumber.CompareTo(rightNumber);
                if (comparison != 0) return comparison;
            }

            if (leftParts.Prerelease is null) return rightParts.Prerelease is null ? 0 : 1;
            if (rightParts.Prerelease is null) return -1;
            return ComparePrerelease(leftParts.Prerelease, rightParts.Prerelease);
        }

        private static (int[] Core, string? Prerelease) SplitVersion(string version)
        {
            var withoutMetadata = version.Split('+', 2)[0];
            var separator = withoutMetadata.IndexOf('-');
            var core = separator < 0 ? withoutMetadata : withoutMetadata[..separator];
            var prerelease = separator < 0 ? null : withoutMetadata[(separator + 1)..];
            return (core.Split('.').Select(part => int.TryParse(part, out var number) ? number : 0).ToArray(), prerelease);
        }

        private static int ComparePrerelease(string left, string right)
        {
            var leftParts = left.Split('.');
            var rightParts = right.Split('.');
            for (var i = 0; i < Math.Min(leftParts.Length, rightParts.Length); i++)
            {
                var leftNumeric = int.TryParse(leftParts[i], out var leftNumber);
                var rightNumeric = int.TryParse(rightParts[i], out var rightNumber);
                var comparison = leftNumeric && rightNumeric
                    ? leftNumber.CompareTo(rightNumber)
                    : leftNumeric ? -1 : rightNumeric ? 1 : string.Compare(leftParts[i], rightParts[i], StringComparison.OrdinalIgnoreCase);
                if (comparison != 0) return comparison;
            }
            return leftParts.Length.CompareTo(rightParts.Length);
        }
    }
}
