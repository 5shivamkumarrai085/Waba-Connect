using System.Collections.Generic;

namespace WhatsAppCampaignApi.Executors;

public class NodeExecutionResult
{
    public string? NextNodeId { get; set; }
    public Dictionary<string, string> CollectVariables { get; set; } = new();
    public bool IsCompleted { get; set; } = false;
    public bool IsWaitingForReply { get; set; } = false;

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
