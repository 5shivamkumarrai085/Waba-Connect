using System.Reflection;
using WhatsAppCampaignApi.Services.Queue;

namespace WhatsAppCampaignApi.Helpers;

/// <summary>
/// Who this build is, for the Postgres application name and diagnostics. Several builds share one
/// database, and telling them apart in <c>pg_stat_activity</c> is how a foreign worker is found.
/// </summary>
public static class BuildIdentity
{
    /// <summary>The commit the build was made from (short), or "local" when unknown.</summary>
    public static string Commit { get; } = ReadCommit();

    /// <summary>
    /// <c>WabaConnect/{queue contract}-in{inbound contract}+{commit}</c>, e.g.
    /// <c>WabaConnect/v3-inv4+ef6a43f</c>. Postgres keeps at most 63 characters.
    /// </summary>
    public static string ApplicationName { get; } =
        Truncate($"WabaConnect/{QueueNames.ContractVersion}-in{QueueNames.InboundContractVersion}+{Commit}", 63);

    private static string ReadCommit()
    {
        // The SDK appends "+<full sha>" to the informational version when the repository is known.
        var version = typeof(BuildIdentity).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;
        var plus = version?.IndexOf('+') ?? -1;
        if (version is null || plus < 0 || plus == version.Length - 1) return "local";
        var sha = version[(plus + 1)..];
        return sha.Length > 7 ? sha[..7] : sha;
    }

    private static string Truncate(string value, int max) => value.Length <= max ? value : value[..max];
}
