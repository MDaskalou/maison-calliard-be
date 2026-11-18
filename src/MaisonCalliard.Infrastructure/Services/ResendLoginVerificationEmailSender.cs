using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json.Serialization;
using MaisonCalliard.Application.Auth;
using MaisonCalliard.Infrastructure.Options;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

namespace MaisonCalliard.Infrastructure.Services;

internal sealed class ResendLoginVerificationEmailSender : ILoginVerificationEmailSender
{
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ResendOptions _resendOptions;
    private readonly ReceiptOptions _receiptOptions;
    private readonly IHostEnvironment _environment;

    public ResendLoginVerificationEmailSender(
        IHttpClientFactory httpClientFactory,
        IOptions<ResendOptions> resendOptions,
        IOptions<ReceiptOptions> receiptOptions,
        IHostEnvironment environment)
    {
        _httpClientFactory = httpClientFactory;
        _resendOptions = resendOptions.Value;
        _receiptOptions = receiptOptions.Value;
        _environment = environment;
    }

    public async Task<bool> SendCodeAsync(
        Guid adminUserId,
        string recipientEmail,
        string code,
        DateTimeOffset expiresAt,
        CancellationToken cancellationToken = default)
    {
        if (_environment.IsDevelopment()
            || !_resendOptions.Enabled
            || string.IsNullOrWhiteSpace(_resendOptions.ApiKey)
            || string.IsNullOrWhiteSpace(_receiptOptions.FromEmail)
            || string.IsNullOrWhiteSpace(recipientEmail))
        {
            return false;
        }

        var from = string.IsNullOrWhiteSpace(_receiptOptions.FromName)
            ? _receiptOptions.FromEmail
            : $"{_receiptOptions.FromName} <{_receiptOptions.FromEmail}>";
        var payload = new ResendEmailRequest
        {
            From = from,
            To = [recipientEmail.Trim()],
            Subject = "Extra inloggningsverifiering",
            Html = $"""
                <p>Din extra inloggningsverifiering för Maison Caillard är:</p>
                <p><strong>{code}</strong></p>
                <p>Koden gäller i fem minuter och kan användas en gång. Om du inte försökte logga in kan du bortse från det här meddelandet.</p>
                """
        };

        var client = _httpClientFactory.CreateClient("Resend");
        using var request = new HttpRequestMessage(HttpMethod.Post, "emails");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _resendOptions.ApiKey);
        request.Content = JsonContent.Create(payload);
        var response = await client.SendAsync(request, cancellationToken);
        return response.IsSuccessStatusCode;
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
