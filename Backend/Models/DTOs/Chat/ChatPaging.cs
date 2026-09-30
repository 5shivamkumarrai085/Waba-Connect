using System.Globalization;
using System.Text;
using Microsoft.AspNetCore.WebUtilities;

namespace WhatsAppCampaignApi.Models.DTOs.Chat;

/// <summary>One page of a keyset-paged chat list.</summary>
public sealed record ChatPage<T>(List<T> Items, string? NextCursor, bool HasMore);

/// <summary>Page sizes and the opaque cursor format for the chat endpoints.</summary>
public static class ChatPaging
{
    public const int DefaultConversationPageSize = 30;
    public const int MaxConversationPageSize = 100;
    public const int DefaultMessagePageSize = 50;
    public const int MaxMessagePageSize = 200;

    /// <summary>
    /// Opaque to the client: last-activity ticks and id of the final row of the previous page.
    /// Base64url so it survives a query string untouched.
    /// </summary>
    public static string EncodeConversationCursor(DateTime lastActivity, int id) =>
        WebEncoders.Base64UrlEncode(Encoding.UTF8.GetBytes(
            $"{DateTime.SpecifyKind(lastActivity, DateTimeKind.Utc).Ticks}:{id}"));

    public static bool TryDecodeConversationCursor(string? cursor, out DateTime lastActivity, out int id)
    {
        lastActivity = default;
        id = 0;
        if (string.IsNullOrWhiteSpace(cursor)) return false;

        try
        {
            var parts = Encoding.UTF8.GetString(WebEncoders.Base64UrlDecode(cursor)).Split(':');
            if (parts.Length != 2
                || !long.TryParse(parts[0], NumberStyles.None, CultureInfo.InvariantCulture, out var ticks)
                || !int.TryParse(parts[1], NumberStyles.None, CultureInfo.InvariantCulture, out id)
                || ticks < DateTime.MinValue.Ticks || ticks > DateTime.MaxValue.Ticks)
            {
                return false;
            }

            lastActivity = new DateTime(ticks, DateTimeKind.Utc);
            return true;
        }
        catch (FormatException)
        {
            // A malformed cursor starts from the top rather than failing the request.
            return false;
        }
    }
}
