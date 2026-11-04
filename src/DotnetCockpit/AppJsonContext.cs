using System.Text.Json.Serialization;
using DotnetCockpit.Installation;
using DotnetCockpit.SdkDownloads;
using DotnetCockpit.Tools;

namespace DotnetCockpit;

[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
[JsonSerializable(typeof(InstallationSnapshot))]
[JsonSerializable(typeof(HealthStatus))]
[JsonSerializable(typeof(ToolsSnapshot))]
[JsonSerializable(typeof(ToolSearch))]
[JsonSerializable(typeof(AppSettings))]
[JsonSerializable(typeof(SystemAccentStatus))]
[JsonSerializable(typeof(StoredCatalog))]
[JsonSerializable(typeof(RevisionStatus))]
internal partial class AppJsonContext : JsonSerializerContext;

internal sealed record HealthStatus(string Status);
internal sealed record SystemAccentStatus(string? Accent);
internal sealed record RevisionStatus(string Revision);
