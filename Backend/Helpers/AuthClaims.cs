namespace WhatsAppCampaignApi.Helpers;

/// <summary>
/// Claim names carried in the JWT.
///
/// <para>
/// Note what is deliberately absent: the permission list. Packing 100+ permission claims would
/// bloat every request header, and — more importantly — a revoked permission would remain live
/// until the token expired. Permissions are resolved per-request from the database instead
/// (cached briefly), so a change takes effect on the very next call.
/// </para>
/// </summary>
public static class AuthClaims
{
    public const string UserId = "sub";
    public const string Email = "email";
    public const string Name = "name";
    public const string RoleName = "role";
    public const string IsAdministrator = "is_admin";
    public const string MustChangePassword = "must_change_pw";

    /// <summary>The user's security stamp at issue time; a mismatch means the session was ended.</summary>
    public const string SecurityStamp = "sstamp";
}
