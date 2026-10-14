using System.Text.Json.Nodes;

namespace WhatsAppCampaignApi.Services.WhatsApp.Flows;

/// <summary>A decrypted data-exchange request (Meta's "data_api_version" 3.0).</summary>
/// <param name="Action">INIT, BACK or data_exchange (ping and error notifications are answered before a handler sees them).</param>
/// <param name="Screen">The screen the customer is on; absent for INIT.</param>
/// <param name="Data">The payload of the screen's action; absent for INIT and BACK.</param>
public sealed record FlowRequest(string Action, string? Screen, JsonObject? Data, string FlowToken, int ConnectionId);

/// <summary>What a handler answers: a body to encrypt and send, or one of Meta's endpoint error codes.</summary>
public sealed record FlowHandlerResult(int StatusCode, JsonObject? Body)
{
    /// <summary>
    /// 427: the flow token is unknown or no longer valid. WhatsApp tells the customer the Flow can
    /// no longer be used, rather than retrying.
    /// </summary>
    public const int InvalidFlowToken = 427;

    public static FlowHandlerResult Ok(JsonObject body) => new(200, body);
    public static FlowHandlerResult TokenInvalid() => new(InvalidFlowToken, null);

    /// <summary>Navigates to <paramref name="screen"/> with its data.</summary>
    public static FlowHandlerResult Screen(string screen, JsonObject data) => Ok(new JsonObject { ["screen"] = screen, ["data"] = data });

    /// <summary>Closes the Flow; <paramref name="parameters"/> reach our messages webhook in the nfm_reply.</summary>
    public static FlowHandlerResult Complete(JsonObject parameters) => Ok(new JsonObject
    {
        ["screen"] = "SUCCESS",
        ["data"] = new JsonObject { ["extension_message_response"] = new JsonObject { ["params"] = parameters } }
    });
}

/// <summary>
/// The server side of one WhatsApp Flow. Flow tokens carry the handler's prefix
/// (<c>{TokenPrefix}_…</c>), which is how a request is routed to the right handler.
/// </summary>
public interface IFlowScreenHandler
{
    string TokenPrefix { get; }

    /// <summary>The <see cref="Models.Entities.WhatsAppFlowRegistration.Purpose"/> this handler serves.</summary>
    string Purpose { get; }

    Task<FlowHandlerResult> HandleAsync(FlowRequest request, CancellationToken ct);
}
