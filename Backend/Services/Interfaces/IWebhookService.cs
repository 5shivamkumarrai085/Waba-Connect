using System.Threading.Tasks;

namespace WhatsAppCampaignApi.Services.Interfaces
{
    public interface IWebhookService
    {
        bool VerifyToken(string hubMode, string hubVerifyToken, string hubChallenge, string configuredVerifyToken, out string challenge);
        Task<bool> TriggerVerificationAsync(string verifyToken);
    }
}
