using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using BillingService.Application.Interfaces;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace BillingService.Infrastructure.MercadoPago;

/// <summary>
/// Integração com Mercado Pago Checkout Pro via REST API.
/// Docs: https://www.mercadopago.com.br/developers/pt
/// </summary>
public class MercadoPagoService(
    HttpClient httpClient,
    IConfiguration configuration,
    ILogger<MercadoPagoService> logger) : IMercadoPagoService
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    public async Task<string> CreatePreferenceAsync(
        Guid paymentId,
        Guid osId,
        decimal amount,
        string description,
        CancellationToken ct = default)
    {
        EnsureAccessToken();

        var notificationUrl = configuration["MercadoPago:NotificationUrl"];
        var backUrl = configuration["MercadoPago:BackUrl"] ?? "https://localhost";

        var payload = new
        {
            items = new[]
            {
                new
                {
                    title = description,
                    quantity = 1,
                    currency_id = "BRL",
                    unit_price = amount
                }
            },
            external_reference = paymentId.ToString(),
            notification_url = notificationUrl,
            back_urls = new
            {
                success = $"{backUrl}/payment/success",
                failure = $"{backUrl}/payment/failure",
                pending = $"{backUrl}/payment/pending"
            },
            auto_return = "approved",
            metadata = new { os_id = osId.ToString() }
        };

        using var request = new HttpRequestMessage(HttpMethod.Post, "https://api.mercadopago.com/checkout/preferences");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", GetAccessToken());
        request.Content = JsonContent.Create(payload);

        var response = await httpClient.SendAsync(request, ct);
        var body = await response.Content.ReadAsStringAsync(ct);

        if (!response.IsSuccessStatusCode)
        {
            logger.LogError("Falha ao criar Preference MP: {Status} {Body}", response.StatusCode, body);
            throw new InvalidOperationException($"Mercado Pago Preference falhou: {response.StatusCode}");
        }

        var preference = JsonSerializer.Deserialize<PreferenceResponse>(body, JsonOptions)
            ?? throw new InvalidOperationException("Resposta inválida do Mercado Pago ao criar Preference.");

        var initPoint = preference.InitPoint ?? preference.SandboxInitPoint
            ?? throw new InvalidOperationException("Preference sem init_point.");

        logger.LogInformation("Preference criada para Payment {PaymentId} / OS {OsId}", paymentId, osId);
        return initPoint;
    }

    public async Task<MpPaymentInfo> GetPaymentInfoAsync(string mpPaymentId, CancellationToken ct = default)
    {
        EnsureAccessToken();

        using var request = new HttpRequestMessage(HttpMethod.Get, $"https://api.mercadopago.com/v1/payments/{mpPaymentId}");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", GetAccessToken());

        var response = await httpClient.SendAsync(request, ct);
        var body = await response.Content.ReadAsStringAsync(ct);

        if (!response.IsSuccessStatusCode)
        {
            logger.LogError("Falha ao consultar Payment MP {Id}: {Status} {Body}", mpPaymentId, response.StatusCode, body);
            throw new InvalidOperationException($"Mercado Pago GetPayment falhou: {response.StatusCode}");
        }

        var payment = JsonSerializer.Deserialize<PaymentResponseDto>(body, JsonOptions)
            ?? throw new InvalidOperationException("Resposta inválida do Mercado Pago ao consultar Payment.");

        var status = MapStatus(payment.Status);
        return new MpPaymentInfo(status, payment.ExternalReference);
    }

    private string GetAccessToken()
        => configuration["MercadoPago:AccessToken"]
           ?? throw new InvalidOperationException("MercadoPago:AccessToken não configurado.");

    private void EnsureAccessToken() => _ = GetAccessToken();

    private static MpPaymentStatus MapStatus(string? status) => status?.ToLowerInvariant() switch
    {
        "approved" => MpPaymentStatus.Approved,
        "pending" or "in_process" or "in_mediation" => MpPaymentStatus.Pending,
        "rejected" => MpPaymentStatus.Rejected,
        "cancelled" or "canceled" => MpPaymentStatus.Cancelled,
        _ => MpPaymentStatus.Unknown
    };

    private sealed class PreferenceResponse
    {
        [JsonPropertyName("init_point")]
        public string? InitPoint { get; set; }

        [JsonPropertyName("sandbox_init_point")]
        public string? SandboxInitPoint { get; set; }
    }

    private sealed class PaymentResponseDto
    {
        [JsonPropertyName("status")]
        public string? Status { get; set; }

        [JsonPropertyName("external_reference")]
        public string? ExternalReference { get; set; }
    }
}

/// <summary>
/// Stub para desenvolvimento local sem credenciais Mercado Pago.
/// Ativado via "MercadoPago:UseStub": true
/// </summary>
public class StubMercadoPagoService(ILogger<StubMercadoPagoService> logger) : IMercadoPagoService
{
    public Task<string> CreatePreferenceAsync(
        Guid paymentId,
        Guid osId,
        decimal amount,
        string description,
        CancellationToken ct = default)
    {
        var url = $"https://sandbox.mercadopago.com.br/checkout/v1/redirect?pref_id=stub-{paymentId}";
        logger.LogInformation(
            "[STUB MP] Preference criada — PaymentId={PaymentId} OsId={OsId} Amount={Amount} Url={Url}",
            paymentId, osId, amount, url);
        return Task.FromResult(url);
    }

    public Task<MpPaymentInfo> GetPaymentInfoAsync(string mpPaymentId, CancellationToken ct = default)
    {
        // Formato stub: "approved:{paymentGuid}" ou "rejected:{paymentGuid}"
        var parts = mpPaymentId.Split(':', 2);
        var status = parts[0].ToLowerInvariant() switch
        {
            "approved" => MpPaymentStatus.Approved,
            "rejected" => MpPaymentStatus.Rejected,
            "cancelled" => MpPaymentStatus.Cancelled,
            _ => MpPaymentStatus.Pending
        };
        var externalRef = parts.Length > 1 ? parts[1] : null;

        logger.LogInformation("[STUB MP] GetPaymentInfo {Id} → {Status}", mpPaymentId, status);
        return Task.FromResult(new MpPaymentInfo(status, externalRef));
    }
}
