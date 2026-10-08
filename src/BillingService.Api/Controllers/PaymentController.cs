using BillingService.Application.DTOs;
using BillingService.Application.Services;
using Microsoft.AspNetCore.Mvc;

namespace BillingService.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
[Produces("application/json")]
public class PaymentController(PaymentAppService appService) : ControllerBase
{
    /// <summary>Inicia checkout Mercado Pago (Checkout Pro) para a OS.</summary>
    [HttpPost("checkout/{osId:guid}")]
    [ProducesResponseType(typeof(CheckoutResponse), StatusCodes.Status200OK)]
    public async Task<IActionResult> StartCheckout(Guid osId, CancellationToken ct)
    {
        try
        {
            return Ok(await appService.StartCheckoutAsync(osId, ct));
        }
        catch (KeyNotFoundException) { return NotFound(); }
        catch (InvalidOperationException ex) { return BadRequest(new { error = ex.Message }); }
    }

    /// <summary>Consulta pagamento da OS.</summary>
    [HttpGet("os/{osId:guid}")]
    [ProducesResponseType(typeof(PaymentResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetByOsId(Guid osId, CancellationToken ct)
    {
        try
        {
            return Ok(await appService.GetByOsIdAsync(osId, ct));
        }
        catch (KeyNotFoundException)
        {
            return NotFound();
        }
    }

    /// <summary>
    /// Webhook Mercado Pago (público).
    /// Aceita query string (?topic=payment&amp;id=...) e body JSON.
    /// Docs: https://www.mercadopago.com.br/developers/pt
    /// </summary>
    [HttpPost("webhook")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public async Task<IActionResult> Webhook(
        [FromQuery] string? topic,
        [FromQuery] string? id,
        [FromQuery(Name = "data.id")] string? dataId,
        [FromBody] WebhookNotificationRequest? body,
        CancellationToken ct)
    {
        var mpPaymentId = id
            ?? dataId
            ?? body?.Id
            ?? body?.Data?.Id;

        var notificationTopic = topic ?? body?.Topic ?? body?.Action;

        if (string.IsNullOrWhiteSpace(mpPaymentId))
            return Ok(new { received = true, processed = false, reason = "missing payment id" });

        // Só processa notificações de payment
        if (!string.IsNullOrWhiteSpace(notificationTopic) &&
            !notificationTopic.Contains("payment", StringComparison.OrdinalIgnoreCase))
        {
            return Ok(new { received = true, processed = false, reason = "ignored topic" });
        }

        try
        {
            await appService.ProcessWebhookAsync(mpPaymentId, ct);
            return Ok(new { received = true, processed = true });
        }
        catch (KeyNotFoundException ex)
        {
            // Retorna 200 para o MP não reenviar indefinidamente em cenários de race;
            // logamos via mensagem no body para diagnóstico.
            return Ok(new { received = true, processed = false, reason = ex.Message });
        }
        catch (InvalidOperationException ex)
        {
            return Ok(new { received = true, processed = false, reason = ex.Message });
        }
    }
}
