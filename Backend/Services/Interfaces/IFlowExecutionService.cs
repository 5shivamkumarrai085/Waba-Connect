using System.Threading.Tasks;

namespace WhatsAppCampaignApi.Services.Interfaces;

public interface IFlowExecutionService
{
    Task ExecuteFlowStepAsync(string phoneNumber, string incomingMessage);
}
