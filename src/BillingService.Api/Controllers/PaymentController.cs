using BillingService.Application.DTOs;
using BillingService.Application.Services;
using Microsoft.AspNetCore.Mvc;

namespace BillingService.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
[Produces("application/json")]
public class PaymentController(PaymentAppService appService) : ControllerBase
{
    /// <summary>
    /// [Override manual] Inicia checkout Mercado Pago (Checkout Pro) para a OS.
    /// No fluxo normal do Saga, o checkout é criado automaticamente pelo
    /// ExecutionCompletedHandler quando o ExecutionService conclui o serviço.
    /// Idempotente: retorna o checkout existente se o Payment já estiver Pending.
    /// </summary>
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
    /// Query: topic=payment e id (no stub: approved:{paymentId}).
    /// </summary>
    [HttpPost("webhook")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public async Task<IActionResult> Webhook(
        [FromQuery] string? topic,
        [FromQuery] string? id,
        [FromQuery(Name = "data.id")] string? dataId,
        CancellationToken ct)
    {
        var mpPaymentId = id ?? dataId;
        var notificationTopic = topic;

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
