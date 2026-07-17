using System.Threading.Tasks;

namespace WhatsAppCampaignApi.Services.Interfaces;

public interface IAiProvider
{
    string ProviderName { get; }
    Task<string> GenerateResponseAsync(string systemPrompt, string userMessage, string model, string apiKey, double temperature = 0.7);
}
