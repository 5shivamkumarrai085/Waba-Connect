using System.Collections.Generic;
using WhatsAppCampaignApi.Services.Interfaces;

namespace WhatsAppCampaignApi.Executors;

public class NodeExecutionResult
{
    public string? NextNodeId { get; set; }
    public Dictionary<string, string> CollectVariables { get; set; } = new();
    public bool IsCompleted { get; set; } = false;
    public bool IsWaitingForReply { get; set; } = false;
    public string? OutboundMessageText { get; set; }

    /// <summary>
    /// Set by executors that send their own WhatsApp message (buttons, list, location, contact
    /// card). The engine then only files that message in the inbox, under its WhatsApp message id,
    /// instead of sending the node's text a second time as plain text.
    /// </summary>
    public WhatsAppSendResult? SentByExecutor { get; set; }

    /// <summary>
    /// A menu stopped waiting because the customer kept replying with something that is not one
    /// of its options. The message was not an answer, so the engine offers it to the other flows'
    /// triggers instead of swallowing it.
    /// </summary>
    public bool GaveUp { get; set; }

    public static NodeExecutionResult Pause(string waitingNodeId) => new()
    {
        NextNodeId = waitingNodeId,
        IsWaitingForReply = true
    };

    public static NodeExecutionResult Next(string nextNodeId) => new()
    {
        NextNodeId = nextNodeId
    };

    public static NodeExecutionResult Complete() => new()
    {
        IsCompleted = true
    };
}
