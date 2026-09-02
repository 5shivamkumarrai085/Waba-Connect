using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using WhatsAppCampaignApi.Services.Interfaces;

namespace WhatsAppCampaignApi.Services;

/// <summary>
/// Chat completions against OpenAI.
///
/// <para>
/// Deliberately the same shape as <see cref="GroqProvider"/> — same retry count, same backoff,
/// same timeout, same response parsing — because Groq's API is OpenAI-compatible and the two
/// differ only in endpoint and default model. Keeping them parallel means the assistant behaves
/// identically whichever one the operator has selected, and a fix to one is an obvious fix to the
/// other.
/// </para>
/// <para>
/// The key arrives as a parameter and is never logged. It is decrypted from settings at the call
/// site and lives only for the duration of the request.
/// </para>
/// </summary>
public class OpenAiProvider : IAiProvider
{
    private readonly HttpClient _httpClient;
    private readonly ILogger<OpenAiProvider> _logger;

    public OpenAiProvider(HttpClient httpClient, ILogger<OpenAiProvider> logger)
    {
        _httpClient = httpClient;
        _logger = logger;
    }

    public string ProviderName => "OpenAI";

    public async Task<string> GenerateResponseAsync(
        string systemPrompt,
        string userMessage,
        string model,
        string apiKey,
        double temperature = 0.7)
    {
        if (string.IsNullOrWhiteSpace(apiKey))
        {
            throw new ArgumentException("API key cannot be empty.", nameof(apiKey));
        }

        const int maxRetries = 3;
        const int delayMs = 1000;

        for (var attempt = 1; attempt <= maxRetries; attempt++)
        {
            try
            {
                using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));
                var request = new HttpRequestMessage(HttpMethod.Post, "https://api.openai.com/v1/chat/completions");
                request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);

                var payloadObj = new
                {
                    model = string.IsNullOrWhiteSpace(model) ? "gpt-4o-mini" : model,
                    messages = new[]
                    {
                        new { role = "system", content = systemPrompt },
                        new { role = "user", content = userMessage }
                    },
                    temperature
                };

                request.Content = new StringContent(
                    JsonSerializer.Serialize(payloadObj), Encoding.UTF8, "application/json");

                var response = await _httpClient.SendAsync(request, cts.Token);

                if (!response.IsSuccessStatusCode)
                {
                    var errorContent = await response.Content.ReadAsStringAsync();

                    // The status and OpenAI's own message, never the request — the request carries
                    // the customer's text and the header carries the key.
                    _logger.LogWarning(
                        "OpenAI returned {StatusCode}: {ErrorResponse}", response.StatusCode, errorContent);

                    // 401/403/404 are configuration problems: a wrong key or a model this account
                    // cannot use. Retrying cannot fix either, and each retry is another rejected
                    // call against the customer's quota.
                    var status = (int)response.StatusCode;
                    if (status is 401 or 403 or 404)
                    {
                        throw new HttpRequestException($"OpenAI rejected the request: {response.StatusCode} - {errorContent}");
                    }

                    if (attempt == maxRetries)
                    {
                        throw new HttpRequestException($"OpenAI completion failed: {response.StatusCode} - {errorContent}");
                    }
                }
                else
                {
                    var responseBody = await response.Content.ReadAsStringAsync();
                    using var doc = JsonDocument.Parse(responseBody);

                    if (doc.RootElement.TryGetProperty("choices", out var choices) && choices.GetArrayLength() > 0)
                    {
                        var first = choices[0];
                        if (first.TryGetProperty("message", out var message) &&
                            message.TryGetProperty("content", out var content))
                        {
                            return content.GetString() ?? string.Empty;
                        }
                    }

                    throw new Exception("Unexpected response format from OpenAI.");
                }
            }
            catch (OperationCanceledException)
            {
                _logger.LogWarning("OpenAI call timed out on attempt {Attempt}/{MaxRetries}", attempt, maxRetries);
                if (attempt == maxRetries)
                {
                    throw new TimeoutException("OpenAI call timed out after multiple attempts.");
                }
            }
            catch (HttpRequestException)
            {
                // Already classified above as not worth retrying.
                throw;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error during OpenAI call on attempt {Attempt}/{MaxRetries}", attempt, maxRetries);
                if (attempt == maxRetries) throw;
            }

            await Task.Delay(delayMs * (int)Math.Pow(2, attempt - 1));
        }

        throw new Exception("OpenAI call failed after retries.");
    }
}
