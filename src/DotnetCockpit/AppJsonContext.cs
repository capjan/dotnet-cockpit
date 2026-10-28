using System.Text.Json.Serialization;
using DotnetCockpit.Installation;

namespace DotnetCockpit;

[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
[JsonSerializable(typeof(InstallationSnapshot))]
[JsonSerializable(typeof(HealthStatus))]
internal partial class AppJsonContext : JsonSerializerContext;

internal sealed record HealthStatus(string Status);
