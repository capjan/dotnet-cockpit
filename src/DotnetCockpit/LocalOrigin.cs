namespace DotnetCockpit;

/// <summary>
/// Changes may only come from Cockpit's own pages. Browsers name the sending page in Origin and Sec-Fetch-Site,
/// so a form posted from any other site is refused. Clients without these headers are not browsers and cannot
/// be made to send a request by a web page.
/// </summary>
internal static class LocalOrigin
{
    public static bool AllowsChange(string? origin, string? fetchSite, string host)
    {
        if (!string.IsNullOrEmpty(origin))
            return string.Equals(origin, $"http://{host}", StringComparison.OrdinalIgnoreCase);
        return string.IsNullOrEmpty(fetchSite) || fetchSite is "same-origin" or "none";
    }
}
