using System.Text.RegularExpressions;

namespace WhatsAppCampaignApi.Helpers;

public static class PhoneNumberHelper
{
    private static readonly Regex E164Regex = new(@"^\+[1-9]\d{6,14}$", RegexOptions.Compiled);

    public static bool IsValidE164(string phone)
    {
        if (string.IsNullOrWhiteSpace(phone)) return false;
        return E164Regex.IsMatch(phone);
    }

    public static string NormalizePhoneNumber(string phone)
    {
        if (string.IsNullOrWhiteSpace(phone)) return string.Empty;
        
        var normalized = Regex.Replace(phone, @"[\s\-\(\)]", string.Empty);
        
        if (!normalized.StartsWith("+") && normalized.All(char.IsDigit))
        {
            normalized = "+" + normalized;
        }
        
        return normalized;
    }

    public static string FormatForWhatsApp(string e164Phone)
    {
        if (string.IsNullOrWhiteSpace(e164Phone)) return string.Empty;
        return e164Phone.StartsWith("+") ? e164Phone.Substring(1) : e164Phone;
    }
}
