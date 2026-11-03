using System.Text.Json.Serialization;
using DotnetCockpit.Installation;
using DotnetCockpit.Tools;

namespace DotnetCockpit;

[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
[JsonSerializable(typeof(InstallationSnapshot))]
[JsonSerializable(typeof(HealthStatus))]
[JsonSerializable(typeof(ToolsSnapshot))]
[JsonSerializable(typeof(ToolSearch))]
internal partial class AppJsonContext : JsonSerializerContext;

internal sealed record HealthStatus(string Status);
