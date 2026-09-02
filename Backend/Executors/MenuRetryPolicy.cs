using System.Text.Json;
using WhatsAppCampaignApi.Models.Entities;

namespace WhatsAppCampaignApi.Executors;

/// <summary>
/// How many times a menu re-asks before giving up on the conversation.
///
/// <para>
/// The button and list nodes used to re-send their options on <em>any</em> input they did not
/// recognise, with no limit. A customer who opened a menu and then went on talking normally got
/// the same menu back for every sentence they wrote, indefinitely — which is what made the bot
/// look like it was replying to everything rather than to its trigger keywords.
/// </para>
/// <para>
/// One re-prompt is genuinely helpful: people mistype, or reply in words instead of tapping. A
/// second unrecognised message means they are not answering the menu at all, so the flow ends and
/// the conversation goes back to normal — the next message is evaluated against the trigger
/// keywords like any other, and a human can take over.
/// </para>
/// <para>
/// The counter lives in the conversation's own variables rather than in a new column: it is
/// per-conversation, per-node state that dies with the session, which is exactly what
/// <c>VariablesJson</c> already holds.
/// </para>
/// </summary>
public static class MenuRetryPolicy
{
    /// <summary>Unrecognised replies tolerated before the flow ends. One re-prompt, then out.</summary>
    public const int MaxAttempts = 2;

    /// <summary>
    /// Variable key for one node's counter. Prefixed so it cannot collide with a variable the flow
    /// author defined — those are named after their own node ids.
    /// </summary>
    public static string KeyFor(string nodeId) => $"__menuRetries:{nodeId}";

    /// <summary>Unrecognised replies so far at this node.</summary>
    public static int AttemptsSoFar(ConversationState state, string nodeId)
    {
        if (string.IsNullOrWhiteSpace(state.VariablesJson)) return 0;

        try
        {
            var variables = JsonSerializer.Deserialize<Dictionary<string, string>>(state.VariablesJson);
            if (variables is null) return 0;

            return variables.TryGetValue(KeyFor(nodeId), out var raw) && int.TryParse(raw, out var count)
                ? count
                : 0;
        }
        catch
        {
            // A malformed variable bag should not make the menu unusable.
            return 0;
        }
    }

    /// <summary>
    /// The result to return when input was not recognised: either re-prompt, or end the flow.
    /// Returns null to mean "re-send the options", so the caller keeps its existing send path.
    /// </summary>
    public static NodeExecutionResult? OnUnrecognised(ConversationState state, string nodeId)
    {
        var attempts = AttemptsSoFar(state, nodeId) + 1;

        if (attempts >= MaxAttempts)
        {
            return new NodeExecutionResult
            {
                IsCompleted = true,
                // Cleared so the next run of this flow starts from zero rather than ending on its
                // first unrecognised reply.
                CollectVariables = new Dictionary<string, string> { [KeyFor(nodeId)] = "0" }
            };
        }

        return null;
    }

    /// <summary>The counter to carry when re-prompting, and to clear on a successful choice.</summary>
    public static Dictionary<string, string> Counter(string nodeId, int value) =>
        new() { [KeyFor(nodeId)] = value.ToString() };
}
