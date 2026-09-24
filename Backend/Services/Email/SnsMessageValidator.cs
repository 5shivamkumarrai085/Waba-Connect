using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;
using WhatsAppCampaignApi.Models.Options;

namespace WhatsAppCampaignApi.Services.Email;

/// <summary>
/// Verifies that an SNS notification genuinely came from AWS, and from a topic we accept.
/// </summary>
public interface ISnsMessageValidator
{
    /// <summary>
    /// Returns true only for a notification whose signature verifies against a real Amazon
    /// signing certificate and whose topic is allow-listed.
    /// </summary>
    Task<bool> IsValidAsync(SnsEnvelope envelope, CancellationToken ct = default);

    /// <summary>
    /// Completes an SNS subscription by fetching its confirmation URL.
    ///
    /// <para>
    /// The URL is checked against the same host rules as the certificate before it is fetched. An
    /// unvalidated <c>SubscribeURL</c> is a server-side request forgery vector: the endpoint is
    /// necessarily anonymous, so anyone could post a notification naming an internal address and
    /// have us fetch it.
    /// </para>
    /// </summary>
    Task<bool> ConfirmSubscriptionAsync(SnsEnvelope envelope, CancellationToken ct = default);
}

/// <inheritdoc />
public class SnsMessageValidator : ISnsMessageValidator
{
    public const string HttpClientName = "sns-validator";

    /// <summary>
    /// Certificates are cached because SNS rotates them rarely and a delivery burst would
    /// otherwise fetch the same certificate thousands of times.
    /// </summary>
    private static readonly TimeSpan CertificateCacheDuration = TimeSpan.FromHours(12);

    private readonly IHttpClientFactory _httpClientFactory;
    private readonly IMemoryCache _cache;
    private readonly IOptionsMonitor<EmailOptions> _options;
    private readonly ILogger<SnsMessageValidator> _logger;

    public SnsMessageValidator(
        IHttpClientFactory httpClientFactory,
        IMemoryCache cache,
        IOptionsMonitor<EmailOptions> options,
        ILogger<SnsMessageValidator> logger)
    {
        _httpClientFactory = httpClientFactory;
        _cache = cache;
        _options = options;
        _logger = logger;
    }

    public async Task<bool> IsValidAsync(SnsEnvelope envelope, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(envelope.Signature)
            || string.IsNullOrWhiteSpace(envelope.SigningCertURL)
            || string.IsNullOrWhiteSpace(envelope.TopicArn))
        {
            _logger.LogWarning("Rejected an SNS notification with no signature, certificate URL or topic.");
            return false;
        }

        // The topic allowlist. Signature verification alone proves only that *some* AWS account
        // sent this — anyone with an AWS account could sign a notification claiming a recipient
        // bounced, and have us suppress a real customer's address.
        var allowed = _options.CurrentValue.Ses.AllowedSnsTopicArns;
        if (allowed.Length == 0)
        {
            _logger.LogError(
                "Rejected an SNS notification because Email:Ses:AllowedSnsTopicArns is empty. Without an "
              + "allowlist this endpoint would accept validly-signed notifications from any AWS account.");
            return false;
        }

        if (!allowed.Contains(envelope.TopicArn, StringComparer.Ordinal))
        {
            _logger.LogWarning(
                "Rejected an SNS notification from unexpected topic {TopicArn}.", envelope.TopicArn);
            return false;
        }

        if (!IsAmazonSigningHost(envelope.SigningCertURL))
        {
            // Without this, an attacker supplies their own certificate URL, signs the payload
            // with the matching private key, and the signature verifies perfectly.
            _logger.LogWarning(
                "Rejected an SNS notification whose signing certificate URL is not an Amazon host: {Url}.",
                envelope.SigningCertURL);
            return false;
        }

        try
        {
            var certificate = await GetCertificateAsync(envelope.SigningCertURL, ct);
            if (certificate is null) return false;

            var payload = Encoding.UTF8.GetBytes(BuildStringToSign(envelope));
            var signature = Convert.FromBase64String(envelope.Signature);

            // Version 1 is SHA1, version 2 SHA256. Both are in use; AWS has been migrating to 2.
            var algorithm = envelope.SignatureVersion == "2" ? HashAlgorithmName.SHA256 : HashAlgorithmName.SHA1;

            using var rsa = certificate.GetRSAPublicKey();
            if (rsa is null)
            {
                _logger.LogWarning("The SNS signing certificate carries no RSA public key.");
                return false;
            }

            var verified = rsa.VerifyData(payload, signature, algorithm, RSASignaturePadding.Pkcs1);

            if (!verified)
            {
                _logger.LogWarning(
                    "Rejected an SNS notification whose signature did not verify (message {MessageId}).",
                    envelope.MessageId);
            }

            return verified;
        }
        catch (Exception ex)
        {
            // Any failure here is a rejection. Treating an unverifiable notification as valid
            // would defeat the entire check.
            _logger.LogError(ex, "Could not verify an SNS notification signature.");
            return false;
        }
    }

    public async Task<bool> ConfirmSubscriptionAsync(SnsEnvelope envelope, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(envelope.SubscribeURL))
        {
            _logger.LogWarning("A SubscriptionConfirmation arrived with no SubscribeURL.");
            return false;
        }

        // Same host rules as the certificate. Fetching an arbitrary URL supplied by an anonymous
        // caller is exactly the shape of a server-side request forgery.
        if (!IsAmazonSnsHost(envelope.SubscribeURL))
        {
            _logger.LogWarning(
                "Refused to fetch a SubscribeURL that is not an Amazon SNS host: {Url}.", envelope.SubscribeURL);
            return false;
        }

        try
        {
            var client = _httpClientFactory.CreateClient(HttpClientName);
            var response = await client.GetAsync(envelope.SubscribeURL, ct);

            if (!response.IsSuccessStatusCode)
            {
                _logger.LogError(
                    "SNS subscription confirmation failed with {StatusCode}.", response.StatusCode);
                return false;
            }

            _logger.LogInformation("Confirmed the SNS subscription for topic {TopicArn}.", envelope.TopicArn);
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Could not confirm the SNS subscription.");
            return false;
        }
    }

    /// <summary>
    /// Builds the exact string AWS signed.
    ///
    /// <para>
    /// The field set and their order are fixed by the SNS specification, and differ between a
    /// notification and a subscription confirmation. Every field is emitted as name then value,
    /// each newline-terminated; an optional field absent from the message is omitted entirely
    /// rather than included as empty, because including it changes the bytes and the signature
    /// then fails for a legitimate message.
    /// </para>
    /// </summary>
    private static string BuildStringToSign(SnsEnvelope envelope)
    {
        var builder = new StringBuilder();

        void Append(string name, string? value)
        {
            if (value is null) return;
            builder.Append(name).Append('\n').Append(value).Append('\n');
        }

        if (envelope.Type is "SubscriptionConfirmation" or "UnsubscribeConfirmation")
        {
            Append("Message", envelope.Message);
            Append("MessageId", envelope.MessageId);
            Append("SubscribeURL", envelope.SubscribeURL);
            Append("Timestamp", envelope.Timestamp);
            Append("Token", envelope.Token);
            Append("TopicArn", envelope.TopicArn);
            Append("Type", envelope.Type);
        }
        else
        {
            Append("Message", envelope.Message);
            Append("MessageId", envelope.MessageId);
            Append("Subject", envelope.Subject);
            Append("Timestamp", envelope.Timestamp);
            Append("TopicArn", envelope.TopicArn);
            Append("Type", envelope.Type);
        }

        return builder.ToString();
    }

    private async Task<X509Certificate2?> GetCertificateAsync(string url, CancellationToken ct)
    {
        if (_cache.TryGetValue(url, out X509Certificate2? cached) && cached is not null) return cached;

        var client = _httpClientFactory.CreateClient(HttpClientName);
        var pem = await client.GetStringAsync(url, ct);

        var certificate = X509Certificate2.CreateFromPem(pem);
        _cache.Set(url, certificate, CertificateCacheDuration);

        return certificate;
    }

    /// <summary>
    /// Whether a URL is an Amazon SNS signing-certificate host.
    ///
    /// <para>
    /// Parsed and compared on the host component rather than matched as a substring. A check like
    /// <c>url.Contains("amazonaws.com")</c> accepts
    /// <c>https://evil.example.com/?x=sns.amazonaws.com</c>, which is the standard way this
    /// validation is got wrong.
    /// </para>
    /// </summary>
    private static bool IsAmazonSigningHost(string url) =>
        IsAmazonHost(url, requirePemPath: true);

    private static bool IsAmazonSnsHost(string url) =>
        IsAmazonHost(url, requirePemPath: false);

    private static bool IsAmazonHost(string url, bool requirePemPath)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri)) return false;

        // HTTPS only: over plain HTTP the certificate itself could be swapped in transit.
        if (uri.Scheme != Uri.UriSchemeHttps) return false;

        var host = uri.Host.ToLowerInvariant();

        // Must be an sns.* subdomain of amazonaws.com — either regional
        // (sns.eu-west-1.amazonaws.com) or the China partition (sns.cn-north-1.amazonaws.com.cn).
        var isAmazonDomain = host.EndsWith(".amazonaws.com", StringComparison.Ordinal)
                          || host.EndsWith(".amazonaws.com.cn", StringComparison.Ordinal);

        if (!isAmazonDomain || !host.StartsWith("sns.", StringComparison.Ordinal)) return false;

        return !requirePemPath || uri.AbsolutePath.EndsWith(".pem", StringComparison.OrdinalIgnoreCase);
    }
}

/// <summary>
/// The SNS message envelope, as posted to the webhook.
///
/// <para>
/// Every field is a string, including <c>Timestamp</c>. SNS sends them as strings, and the
/// signature is computed over those exact strings — parsing <c>Timestamp</c> into a
/// <c>DateTime</c> and re-serialising it would produce a different byte sequence and fail
/// verification for a legitimate message.
/// </para>
/// </summary>
public class SnsEnvelope
{
    [JsonPropertyName("Type")]
    public string? Type { get; set; }

    [JsonPropertyName("MessageId")]
    public string? MessageId { get; set; }

    [JsonPropertyName("TopicArn")]
    public string? TopicArn { get; set; }

    [JsonPropertyName("Subject")]
    public string? Subject { get; set; }

    /// <summary>The SES event, as a JSON string that must be parsed separately.</summary>
    [JsonPropertyName("Message")]
    public string? Message { get; set; }

    [JsonPropertyName("Timestamp")]
    public string? Timestamp { get; set; }

    [JsonPropertyName("SignatureVersion")]
    public string? SignatureVersion { get; set; }

    [JsonPropertyName("Signature")]
    public string? Signature { get; set; }

    [JsonPropertyName("SigningCertURL")]
    public string? SigningCertURL { get; set; }

    [JsonPropertyName("SubscribeURL")]
    public string? SubscribeURL { get; set; }

    [JsonPropertyName("Token")]
    public string? Token { get; set; }

    [JsonPropertyName("UnsubscribeURL")]
    public string? UnsubscribeURL { get; set; }
}
