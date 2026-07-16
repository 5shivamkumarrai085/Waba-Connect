using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using WhatsAppCampaignApi.Models.Entities;
using WhatsAppCampaignApi.Services.Interfaces;

namespace WhatsAppCampaignApi.Executors;

public class ContactCardNodeExecutor : INodeExecutor
{
    private readonly IWhatsAppService _whatsAppService;

    public ContactCardNodeExecutor(IWhatsAppService whatsAppService)
    {
        _whatsAppService = whatsAppService;
    }

    public string NodeType => "Contact Card";

    public async Task<NodeExecutionResult> ExecuteAsync(
        FlowNode node,
        ConversationState state,
        string incomingMessage,
        List<FlowEdge> outgoingEdges)
    {
        var contacts = new List<ContactItem>();

        try
        {
            using var doc = JsonDocument.Parse(node.DataJson);
            if (doc.RootElement.TryGetProperty("contacts", out var contactsProp))
            {
                var parsed = JsonSerializer.Deserialize<List<ContactItem>>(contactsProp.GetRawText(), new JsonSerializerOptions
                {
                    PropertyNamingPolicy = JsonNamingPolicy.CamelCase
                });
                if (parsed != null) contacts = parsed;
            }
        }
        catch
        {
            // Ignore parse errors
        }

        if (contacts.Any())
        {
            var metaContacts = contacts.Select(c => new
            {
                name = new
                {
                    first_name = c.FirstName,
                    last_name = c.LastName,
                    formatted_name = $"{c.FirstName} {c.LastName}".Trim()
                },
                phones = new[]
                {
                    new { phone = c.Phone, type = "WORK", wa_id = c.Phone.Replace("+", "").Replace(" ", "") }
                }.ToList(),
                emails = !string.IsNullOrEmpty(c.Email) ? new[]
                {
                    new { email = c.Email, type = "WORK" }
                }.ToList() : null,
                org = (!string.IsNullOrEmpty(c.Company) || !string.IsNullOrEmpty(c.Title)) ? new
                {
                    company = c.Company ?? "",
                    title = c.Title ?? "",
                    department = ""
                } : null
            }).ToList();

            var payload = new
            {
                messaging_product = "whatsapp",
                recipient_type = "individual",
                to = state.PhoneNumber,
                type = "contacts",
                contacts = metaContacts
            };

            await _whatsAppService.SendCustomPayloadAsync(state.PhoneNumber, payload);
        }

        var nextEdge = outgoingEdges.FirstOrDefault(e => e.Source == node.NodeId);
        if (nextEdge == null)
        {
            return NodeExecutionResult.Complete();
        }

        return NodeExecutionResult.Next(nextEdge.Target);
    }

    private class ContactItem
    {
        public string FirstName { get; set; } = string.Empty;
        public string LastName { get; set; } = string.Empty;
        public string Phone { get; set; } = string.Empty;
        public string? Email { get; set; }
        public string? Company { get; set; }
        public string? Title { get; set; }
    }
}
