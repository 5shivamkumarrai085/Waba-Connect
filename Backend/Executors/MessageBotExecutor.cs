using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using WhatsAppCampaignApi.Models.Entities;
using WhatsAppCampaignApi.Services.Interfaces;

namespace WhatsAppCampaignApi.Executors;

public class MessageBotExecutor
{
    private readonly IWhatsAppService _whatsAppService;
    private readonly ILogger<MessageBotExecutor> _logger;
    private readonly WhatsAppCampaignApi.Data.AppDbContext _dbContext;

    public MessageBotExecutor(IWhatsAppService whatsAppService, ILogger<MessageBotExecutor> logger, WhatsAppCampaignApi.Data.AppDbContext dbContext)
    {
        _whatsAppService = whatsAppService;
        _logger = logger;
        _dbContext = dbContext;
    }

    public async Task<WhatsAppSendResult> ExecuteReplyAsync(MessageBot bot, string recipientPhone, int? connectionId = null)
    {
        try
        {
            string? resolvedPhoneNumberId = null;
            if (connectionId.HasValue)
            {
                var phone = await Microsoft.EntityFrameworkCore.EntityFrameworkQueryableExtensions.FirstOrDefaultAsync(_dbContext.WabaPhoneNumbers, p => p.ConnectionId == connectionId.Value);
                resolvedPhoneNumberId = phone?.PhoneNumberId;
            }

            if (string.Equals(bot.OptionType, "ReplyButtons", StringComparison.OrdinalIgnoreCase))
            {
                var buttonsList = new List<object>();

                if (!string.IsNullOrWhiteSpace(bot.Button1))
                {
                    buttonsList.Add(new
                    {
                        type = "reply",
                        reply = new
                        {
                            id = string.IsNullOrWhiteSpace(bot.Button1Id) ? "btn1" : bot.Button1Id,
                            title = bot.Button1.Length > 20 ? bot.Button1.Substring(0, 20) : bot.Button1
                        }
                    });
                }

                if (!string.IsNullOrWhiteSpace(bot.Button2))
                {
                    buttonsList.Add(new
                    {
                        type = "reply",
                        reply = new
                        {
                            id = string.IsNullOrWhiteSpace(bot.Button2Id) ? "btn2" : bot.Button2Id,
                            title = bot.Button2.Length > 20 ? bot.Button2.Substring(0, 20) : bot.Button2
                        }
                    });
                }

                if (!string.IsNullOrWhiteSpace(bot.Button3))
                {
                    buttonsList.Add(new
                    {
                        type = "reply",
                        reply = new
                        {
                            id = string.IsNullOrWhiteSpace(bot.Button3Id) ? "btn3" : bot.Button3Id,
                            title = bot.Button3.Length > 20 ? bot.Button3.Substring(0, 20) : bot.Button3
                        }
                    });
                }

                if (buttonsList.Count > 0)
                {
                    var interactiveObj = new Dictionary<string, object>
                    {
                        { "type", "button" },
                        { "body", new { text = bot.ReplyText } }
                    };

                    if (!string.IsNullOrWhiteSpace(bot.Header))
                    {
                        interactiveObj["header"] = new { type = "text", text = bot.Header };
                    }

                    if (!string.IsNullOrWhiteSpace(bot.Footer))
                    {
                        interactiveObj["footer"] = new { text = bot.Footer };
                    }

                    interactiveObj["action"] = new { buttons = buttonsList };

                    var payload = new
                    {
                        messaging_product = "whatsapp",
                        recipient_type = "individual",
                        to = recipientPhone,
                        type = "interactive",
                        interactive = interactiveObj
                    };

                    _logger.LogInformation("Sending interactive buttons payload to {Recipient}", recipientPhone);
                    return await _whatsAppService.SendCustomPayloadAsync(recipientPhone, payload, resolvedPhoneNumberId, connectionId);
                }
            }
            else if (string.Equals(bot.OptionType, "CtaUrl", StringComparison.OrdinalIgnoreCase) && 
                     !string.IsNullOrWhiteSpace(bot.CtaButtonLink))
            {
                var displayButtonName = string.IsNullOrWhiteSpace(bot.CtaButtonName) ? "Visit Link" : bot.CtaButtonName;
                if (displayButtonName.Length > 20)
                {
                    displayButtonName = displayButtonName.Substring(0, 20);
                }

                var interactiveObj = new Dictionary<string, object>
                {
                    { "type", "cta_url" },
                    { "body", new { text = bot.ReplyText } }
                };

                if (!string.IsNullOrWhiteSpace(bot.Header))
                {
                    interactiveObj["header"] = new { type = "text", text = bot.Header };
                }

                if (!string.IsNullOrWhiteSpace(bot.Footer))
                {
                    interactiveObj["footer"] = new { text = bot.Footer };
                }

                interactiveObj["action"] = new
                {
                    name = "cta_url",
                    parameters = new
                    {
                        display_text = displayButtonName,
                        url = bot.CtaButtonLink
                    }
                };

                var payload = new
                {
                    messaging_product = "whatsapp",
                    recipient_type = "individual",
                    to = recipientPhone,
                    type = "interactive",
                    interactive = interactiveObj
                };

                _logger.LogInformation("Sending CTA URL payload to {Recipient}", recipientPhone);
                return await _whatsAppService.SendCustomPayloadAsync(recipientPhone, payload, resolvedPhoneNumberId, connectionId);
            }
            else if (string.Equals(bot.OptionType, "Files", StringComparison.OrdinalIgnoreCase) && 
                     !string.IsNullOrWhiteSpace(bot.FileUrl))
            {
                _logger.LogInformation("Sending file attachment to {Recipient}: {FileUrl}", recipientPhone, bot.FileUrl);
                string fileType = string.IsNullOrWhiteSpace(bot.FileType) ? "document" : bot.FileType.ToLower();
                return await _whatsAppService.SendMediaMessageAsync(recipientPhone, bot.FileUrl, fileType, bot.FileName, bot.ReplyText, resolvedPhoneNumberId, connectionId);
            }

            // Fallback Text Message
            var sb = new StringBuilder();
            if (!string.IsNullOrWhiteSpace(bot.Header))
            {
                sb.AppendLine($"*{bot.Header}*");
                sb.AppendLine();
            }
            sb.AppendLine(bot.ReplyText);
            if (!string.IsNullOrWhiteSpace(bot.Footer))
            {
                sb.AppendLine();
                sb.AppendLine($"_{bot.Footer}_");
            }

            _logger.LogInformation("Sending standard fallback text message to {Recipient}", recipientPhone);
            return await _whatsAppService.SendTextMessageAsync(recipientPhone, sb.ToString().TrimEnd(), resolvedPhoneNumberId, connectionId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to execute MessageBot reply for bot {BotId}", bot.Id);
            return WhatsAppSendResult.Failed(ex.Message);
        }
    }
}
