using System.Security.Cryptography;
using System.Text;

namespace WhatsAppCampaignApi.Helpers;

/// <summary>
/// Configuration fix-ups applied once at startup, before anything binds options: derived secrets,
/// shared defaults, and the production guard against placeholder secrets.
/// </summary>
public static class StartupConfiguration
{
    /// <summary>Values that must never reach production — sample or placeholder secrets.</summary>
    private static readonly string[] PlaceholderMarkers = ["change-me", "changeme", "your-", "replace-me", "sample", "example"];

    /// <summary>
    /// Derives <paramref name="key"/> from Encryption:Key with HKDF when it is unset. Domain
    /// separation by <paramref name="purpose"/> means the derived key reveals nothing about the root
    /// key and cannot be substituted for it — one managed secret instead of several.
    /// </summary>
    public static void DeriveSecret(IConfiguration configuration, string key, string purpose)
    {
        // Only when unset. An existing value — even a placeholder — keeps signing links already sitting
        // in recipients' inboxes; production refuses placeholders separately below.
        if (!string.IsNullOrWhiteSpace(configuration[key])) return;

        var rootKey = configuration["Encryption:Key"];
        if (string.IsNullOrWhiteSpace(rootKey)) return;

        var derived = HKDF.DeriveKey(
            HashAlgorithmName.SHA256,
            ikm: Encoding.UTF8.GetBytes(rootKey),
            outputLength: 32,
            info: Encoding.UTF8.GetBytes(purpose));

        configuration[key] = Convert.ToBase64String(derived);
    }

    /// <summary>The secrets the process cannot start without, in every environment.</summary>
    private static readonly string[] RequiredSecrets = ["ConnectionStrings:DefaultConnection", "Auth:Jwt:Key", "Encryption:Key"];

    /// <summary>
    /// Loads the developer's user-secrets file in any non-Production environment.
    /// </summary>
    /// <remarks>
    /// The host only loads user-secrets when the environment is exactly "Development", and finds
    /// the file through the APPDATA variable. A terminal that runs under another environment name,
    /// or that does not carry APPDATA (some editor terminals do not), then starts without any
    /// secret and fails on the first one it needs. The file is looked up at both the location the
    /// SDK uses and the real per-user application-data folder. Environment variables and
    /// command-line arguments are re-added after it, so they still take precedence.
    /// </remarks>
    public static void AddDeveloperSecrets(WebApplicationBuilder builder, string[] args)
    {
        if (builder.Environment.IsProduction()) return;

        // The project-local file first, so user-secrets (if this process can see them) still win.
        // It exists for terminals that cannot read %APPDATA%: some editor and agent terminals run
        // commands in a sandbox confined to the project folder, where the user-secrets file looks
        // "not found" although it is there. Git-ignored by the appsettings.*.local.json rule.
        var localPath = LocalSecretsPath(builder.Environment);
        var added = false;
        if (File.Exists(localPath))
        {
            builder.Configuration.AddJsonFile(localPath, optional: true, reloadOnChange: false);
            added = true;
        }

        var path = FindUserSecretsFile(typeof(StartupConfiguration).Assembly).FirstOrDefault(File.Exists);
        if (path is not null)
        {
            builder.Configuration.AddJsonFile(path, optional: true, reloadOnChange: false);
            added = true;
        }

        if (!added) return;
        builder.Configuration.AddEnvironmentVariables();
        builder.Configuration.AddCommandLine(args);
    }

    /// <summary>Developer secrets inside the project, for terminals that cannot read %APPDATA%.</summary>
    public const string LocalSecretsFileName = "appsettings.secrets.local.json";

    private static string LocalSecretsPath(IHostEnvironment environment) =>
        Path.Combine(environment.ContentRootPath, LocalSecretsFileName);

    /// <summary>
    /// Whether a file is there and readable — told apart, because File.Exists answers false for a
    /// file this process may not read, which made a sandboxed terminal report a present file as
    /// "not found".
    /// </summary>
    private static string Describe(string path)
    {
        try
        {
            using var stream = File.OpenRead(path);
            return "found";
        }
        catch (FileNotFoundException) { return "not found"; }
        catch (DirectoryNotFoundException) { return "not found"; }
        catch (UnauthorizedAccessException) { return "EXISTS BUT THIS PROCESS MAY NOT READ IT (access denied — a sandboxed terminal?)"; }
        catch (IOException ex) { return $"cannot be read: {ex.GetType().Name}"; }
    }

    /// <summary>
    /// Fails the boot with one message naming every missing secret, the environment the process
    /// is running as, and where it looked — so "it works in one terminal and not another" can be
    /// diagnosed from the error alone.
    /// </summary>
    public static void RequireSecrets(IConfiguration configuration, IHostEnvironment environment)
    {
        var missing = RequiredSecrets.Where(k => string.IsNullOrWhiteSpace(configuration[k])).ToList();
        if (missing.Count == 0) return;

        var nl = Environment.NewLine;
        var candidates = FindUserSecretsFile(typeof(StartupConfiguration).Assembly)
            .Prepend(LocalSecretsPath(environment))
            .Select(p => $"    {p}  ({Describe(p)})");

        var message =
            $"Required secret(s) not configured: {string.Join(", ", missing)}.{nl}" +
            $"  Environment: {environment.EnvironmentName}   Content root: {environment.ContentRootPath}{nl}" +
            $"  APPDATA: {Environment.GetEnvironmentVariable("APPDATA") ?? "(not set)"}{nl}" +
            $"  Secret files checked (loaded outside Production only):{nl}{string.Join(nl, candidates)}{nl}" +
            $"  Fix (development, any terminal): copy appsettings.secrets.local.example.json to {LocalSecretsFileName} in the Backend folder and fill it in (git-ignored).{nl}" +
            $"  Fix (development): dotnet user-secrets set \"<key>\" \"<value>\" --project Backend{nl}" +
            $"  Fix (servers): set environment variables, e.g. Encryption__Key, Auth__Jwt__Key, ConnectionStrings__DefaultConnection.{nl}" +
            "  Reuse the SAME Encryption:Key as before: a new key makes stored credentials (WhatsApp tokens, SMTP passwords) unreadable.";

        throw new InvalidOperationException(message);
    }

    private static IEnumerable<string> FindUserSecretsFile(System.Reflection.Assembly assembly)
    {
        var id = assembly.GetCustomAttributes(typeof(Microsoft.Extensions.Configuration.UserSecrets.UserSecretsIdAttribute), false)
            .OfType<Microsoft.Extensions.Configuration.UserSecrets.UserSecretsIdAttribute>()
            .FirstOrDefault()?.UserSecretsId;
        if (string.IsNullOrWhiteSpace(id)) yield break;

        var sdkPath = Microsoft.Extensions.Configuration.UserSecrets.PathHelper.GetSecretsPathFromSecretsId(id);
        yield return sdkPath;

        var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        if (!string.IsNullOrEmpty(appData))
        {
            var windowsPath = Path.Combine(appData, "Microsoft", "UserSecrets", id, "secrets.json");
            if (!string.Equals(windowsPath, sdkPath, StringComparison.OrdinalIgnoreCase)) yield return windowsPath;
        }
    }

    /// <summary>Copies <paramref name="sourceKey"/> into <paramref name="key"/> when the latter is unset.</summary>
    public static void DefaultFrom(IConfiguration configuration, string key, string sourceKey)
    {
        if (!string.IsNullOrWhiteSpace(configuration[key])) return;
        var source = configuration[sourceKey];
        if (!string.IsNullOrWhiteSpace(source)) configuration[key] = source.TrimEnd('/');
    }

    public static void ValidateProductionSecrets(IConfiguration configuration, IHostEnvironment environment)
    {
        if (!environment.IsProduction()) return;

        var problems = new List<string>();

        void Require(string key, int minLength = 1)
        {
            var value = configuration[key];
            if (string.IsNullOrWhiteSpace(value)) problems.Add($"{key} is not set");
            else if (value.Length < minLength) problems.Add($"{key} is shorter than {minLength} characters");
            else if (IsPlaceholder(value)) problems.Add($"{key} is still a placeholder value");
        }

        Require("ConnectionStrings:DefaultConnection");
        Require("Auth:Jwt:Key", 32);
        Require("Encryption:Key", 32);
        Require("Email:Tracking:SigningSecret", 32);
        Require("Email:Unsubscribe:SigningKey", 32);

        var connectionString = configuration["ConnectionStrings:DefaultConnection"] ?? string.Empty;
        if (connectionString.Contains("Trust Server Certificate=true", StringComparison.OrdinalIgnoreCase))
        {
            problems.Add("ConnectionStrings:DefaultConnection disables TLS certificate validation (Trust Server Certificate=true)");
        }

        if (configuration.GetValue("Email:Enabled", false))
        {
            var publicBase = configuration["Email:Unsubscribe:PublicBaseUrl"] ?? string.Empty;
            if (!Uri.TryCreate(publicBase, UriKind.Absolute, out var uri) || uri.IsLoopback || uri.Host.Equals("localhost", StringComparison.OrdinalIgnoreCase))
            {
                problems.Add("App:PublicBaseUrl must be a public https URL — unsubscribe and tracking links in sent mail point at it");
            }
            else if (uri.Scheme != Uri.UriSchemeHttps)
            {
                problems.Add("App:PublicBaseUrl must use https");
            }
        }

        var adminPassword = configuration["Auth:SuperAdmin:Password"];
        if (!string.IsNullOrEmpty(adminPassword) && (adminPassword == "Admin@123" || IsPlaceholder(adminPassword)))
        {
            problems.Add("Auth:SuperAdmin:Password is the documented default");
        }

        if (problems.Count > 0)
        {
            throw new InvalidOperationException(
                "Refusing to start in Production with insecure configuration: " + string.Join("; ", problems)
              + ". Supply these through environment variables or your secret store.");
        }
    }

    private static bool IsPlaceholder(string? value) =>
        !string.IsNullOrEmpty(value) && PlaceholderMarkers.Any(m => value.Contains(m, StringComparison.OrdinalIgnoreCase));
}
