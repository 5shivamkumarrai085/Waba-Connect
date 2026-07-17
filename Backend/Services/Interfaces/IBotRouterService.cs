using System.Threading.Tasks;
using WhatsAppCampaignApi.Models.Entities;

namespace WhatsAppCampaignApi.Services.Interfaces;

public interface IBotRouterService
{
    Task<bool> RouteMessageAsync(string phoneNumber, string incomingMessage, Contact contact);
}
