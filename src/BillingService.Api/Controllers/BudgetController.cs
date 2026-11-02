using BillingService.Application.DTOs;
using BillingService.Application.Services;
using Microsoft.AspNetCore.Mvc;

namespace BillingService.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
[Produces("application/json")]
public class BudgetController(BudgetAppService appService) : ControllerBase
{
    /// <summary>Busca orçamento por ID.</summary>
    [HttpGet("{id:guid}")]
    [ProducesResponseType(typeof(BudgetResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetById(Guid id, CancellationToken ct)
    {
        try
        {
            return Ok(await appService.GetByIdAsync(id, ct));
        }
        catch (KeyNotFoundException)
        {
            return NotFound();
        }
    }

    /// <summary>Busca orçamento pela OS.</summary>
    [HttpGet("os/{osId:guid}")]
    [ProducesResponseType(typeof(BudgetResponse), StatusCodes.Status200OK)]
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

    /// <summary>Envia orçamento ao cliente (Draft → Sent) e publica BudgetGenerated.</summary>
    [HttpPost("{id:guid}/send")]
    [ProducesResponseType(typeof(BudgetResponse), StatusCodes.Status200OK)]
    public async Task<IActionResult> Send(Guid id, CancellationToken ct)
        => await Execute(() => appService.SendAsync(id, ct));

    /// <summary>Aprova orçamento (cliente).</summary>
    [HttpPost("{id:guid}/approve")]
    [ProducesResponseType(typeof(BudgetResponse), StatusCodes.Status200OK)]
    public async Task<IActionResult> Approve(Guid id, CancellationToken ct)
        => await Execute(() => appService.ApproveAsync(id, ct));

    /// <summary>Rejeita orçamento (cliente).</summary>
    [HttpPost("{id:guid}/reject")]
    [ProducesResponseType(typeof(BudgetResponse), StatusCodes.Status200OK)]
    public async Task<IActionResult> Reject(Guid id, [FromBody] RejectBudgetRequest request, CancellationToken ct)
        => await Execute(() => appService.RejectAsync(id, request.Reason, ct));

    private async Task<IActionResult> Execute(Func<Task<BudgetResponse>> action)
    {
        try
        {
            return Ok(await action());
        }
        catch (KeyNotFoundException) { return NotFound(); }
        catch (InvalidOperationException ex) { return BadRequest(new { error = ex.Message }); }
        catch (ArgumentException ex) { return BadRequest(new { error = ex.Message }); }
    }
}
