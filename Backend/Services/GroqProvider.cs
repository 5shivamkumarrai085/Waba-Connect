using System;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using WhatsAppCampaignApi.Services.Interfaces;

namespace WhatsAppCampaignApi.Services;

public class GroqProvider : IAiProvider
{
    private readonly HttpClient _httpClient;
    private readonly ILogger<GroqProvider> _logger;

    public GroqProvider(HttpClient httpClient, ILogger<GroqProvider> logger)
    {
        _httpClient = httpClient;
        _logger = logger;
    }

    public string ProviderName => "Groq";

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

        int maxRetries = 3;
        int delayMs = 1000;

        for (int attempt = 1; attempt <= maxRetries; attempt++)
        {
            try
            {
                using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(15));
                var request = new HttpRequestMessage(HttpMethod.Post, "https://api.groq.com/openai/v1/chat/completions");
                request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);

                var payloadObj = new
                {
                    model = string.IsNullOrWhiteSpace(model) ? "llama3-8b-8192" : model,
                    messages = new[]
                    {
                        new { role = "system", content = systemPrompt },
                        new { role = "user", content = userMessage }
                    },
                    temperature = temperature
                };

                string payloadJson = JsonSerializer.Serialize(payloadObj);
                request.Content = new StringContent(payloadJson, Encoding.UTF8, "application/json");

                var response = await _httpClient.SendAsync(request, cts.Token);

                if (!response.IsSuccessStatusCode)
                {
                    string errorContent = await response.Content.ReadAsStringAsync();
                    _logger.LogWarning("Groq API returned failure status code {StatusCode}: {ErrorResponse}", response.StatusCode, errorContent);
                    
                    if (attempt == maxRetries)
                    {
                        throw new HttpRequestException($"Groq API completion failed: {response.StatusCode} - {errorContent}");
                    }
                }
                else
                {
                    string responseBody = await response.Content.ReadAsStringAsync();
                    using var doc = JsonDocument.Parse(responseBody);
                    if (doc.RootElement.TryGetProperty("choices", out var choicesProp) && 
                        choicesProp.GetArrayLength() > 0)
                    {
                        var firstChoice = choicesProp[0];
                        if (firstChoice.TryGetProperty("message", out var msgProp) &&
                            msgProp.TryGetProperty("content", out var contentProp))
                        {
                            return contentProp.GetString() ?? string.Empty;
                        }
                    }
                    throw new Exception("Unexpected response format from Groq API.");
                }
            }
            catch (OperationCanceledException)
            {
                _logger.LogWarning("Groq API call timed out on attempt {Attempt}/{MaxRetries}", attempt, maxRetries);
                if (attempt == maxRetries)
                {
                    throw new TimeoutException("Groq API call timed out after multiple attempts.");
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error occurred during Groq API call on attempt {Attempt}/{MaxRetries}", attempt, maxRetries);
                if (attempt == maxRetries)
                {
                    throw;
                }
            }

            // Exponential backoff delay
            await Task.Delay(delayMs * (int)Math.Pow(2, attempt - 1));
        }

        throw new Exception("Groq API call failed after retries.");
    }
}
