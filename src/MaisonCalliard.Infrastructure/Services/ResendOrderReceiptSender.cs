using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json.Serialization;
using MaisonCalliard.Application.Receipts;
using MaisonCalliard.Infrastructure.Options;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace MaisonCalliard.Infrastructure.Services;

internal sealed class ResendOrderReceiptSender : IOrderReceiptSender
{
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ResendOptions _resendOptions;
    private readonly ReceiptOptions _receiptOptions;
    private readonly ILogger<ResendOrderReceiptSender> _logger;

    public ResendOrderReceiptSender(
        IHttpClientFactory httpClientFactory,
        IOptions<ResendOptions> resendOptions,
        IOptions<ReceiptOptions> receiptOptions,
        ILogger<ResendOrderReceiptSender> logger)
    {
        _httpClientFactory = httpClientFactory;
        _resendOptions = resendOptions.Value;
        _receiptOptions = receiptOptions.Value;
        _logger = logger;
    }

    public async Task<bool> SendAsync(string toEmail, string subject, string html, CancellationToken cancellationToken)
    {
        if (!_resendOptions.Enabled)
        {
            _logger.LogInformation(
                "Resend disabled. Receipt not sent to {Email}. Subject: {Subject}. HTML length: {Length}",
                toEmail,
                subject,
                html.Length);
            return false;
        }

        if (string.IsNullOrWhiteSpace(_resendOptions.ApiKey))
        {
            _logger.LogWarning("Resend ApiKey is not configured. Receipt not sent to {Email}.", toEmail);
            return false;
        }

        var from = string.IsNullOrWhiteSpace(_receiptOptions.FromName)
            ? _receiptOptions.FromEmail
            : $"{_receiptOptions.FromName} <{_receiptOptions.FromEmail}>";

        var payload = new ResendEmailRequest
        {
            From = from,
            To = [toEmail],
            Subject = subject,
            Html = html
        };

        var client = _httpClientFactory.CreateClient("Resend");
        using var request = new HttpRequestMessage(HttpMethod.Post, "emails");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _resendOptions.ApiKey);
        request.Content = JsonContent.Create(payload);

        var response = await client.SendAsync(request, cancellationToken);
        if (response.IsSuccessStatusCode)
        {
            _logger.LogInformation("Resend accepted email to {Email}. Subject: {Subject}.", toEmail, subject);
            return true;
        }

        var body = await response.Content.ReadAsStringAsync(cancellationToken);
        _logger.LogError(
            "Resend API failed ({StatusCode}) for {Email}: {Body}",
            (int)response.StatusCode,
            toEmail,
            body);
        return false;
    }

    private sealed class ResendEmailRequest
    {
        [JsonPropertyName("from")]
        public string From { get; set; } = string.Empty;

        [JsonPropertyName("to")]
        public List<string> To { get; set; } = [];

        [JsonPropertyName("subject")]
        public string Subject { get; set; } = string.Empty;

        [JsonPropertyName("html")]
        public string Html { get; set; } = string.Empty;
    }
}
