using System.Threading.Tasks;

namespace WhatsAppCampaignApi.Services.Interfaces;

public interface IFlowExecutionService
{
    Task<bool> ExecuteFlowStepAsync(string phoneNumber, string incomingMessage);
}
