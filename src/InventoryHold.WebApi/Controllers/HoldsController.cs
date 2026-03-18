using InventoryHold.Contracts.Requests;
using InventoryHold.Contracts.Responses;
using InventoryHold.Domain.Exceptions;
using InventoryHold.Infrastructure.Services;
using Microsoft.AspNetCore.Mvc;

namespace InventoryHold.WebApi.Controllers;

[ApiController]
[Route("api/holds")]
public sealed class HoldsController : ControllerBase
{
    private readonly HoldService _holdService;
    private readonly ILogger<HoldsController> _logger;

    public HoldsController(HoldService holdService, ILogger<HoldsController> logger)
    {
        _holdService = holdService;
        _logger = logger;
    }

    /// <summary>Creates a new inventory hold.</summary>
    [HttpPost]
    [ProducesResponseType(typeof(HoldResponse), StatusCodes.Status201Created)]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status409Conflict)]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> CreateHold(
        [FromBody] CreateHoldRequest request,
        CancellationToken ct)
    {
        if (request.Quantity <= 0)
            return BadRequest(new ErrorResponse { Code = "INVALID_QUANTITY", Message = "Quantity must be greater than zero." });

        try
        {
            var result = await _holdService.CreateHoldAsync(request, ct);
            return CreatedAtAction(nameof(GetHold), new { holdId = result.HoldId }, result);
        }
        catch (InsufficientInventoryException ex)
        {
            return Conflict(new ErrorResponse { Code = "INSUFFICIENT_INVENTORY", Message = ex.Message });
        }
    }

    /// <summary>Gets the current state of a hold.</summary>
    [HttpGet("{holdId}")]
    [ProducesResponseType(typeof(HoldResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetHold(string holdId, CancellationToken ct)
    {
        try
        {
            var result = await _holdService.GetHoldAsync(holdId, ct);
            return Ok(result);
        }
        catch (HoldNotFoundException ex)
        {
            return NotFound(new ErrorResponse { Code = "HOLD_NOT_FOUND", Message = ex.Message });
        }
    }

    /// <summary>Releases an active hold.</summary>
    [HttpPost("{holdId}/release")]
    [ProducesResponseType(typeof(HoldResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status409Conflict)]
    public async Task<IActionResult> ReleaseHold(
        string holdId,
        [FromBody] ReleaseHoldRequest request,
        CancellationToken ct)
    {
        try
        {
            var result = await _holdService.ReleaseHoldAsync(holdId, request, ct);
            return Ok(result);
        }
        catch (HoldNotFoundException ex)
        {
            return NotFound(new ErrorResponse { Code = "HOLD_NOT_FOUND", Message = ex.Message });
        }
        catch (HoldAlreadyReleasedException ex)
        {
            return Conflict(new ErrorResponse { Code = "HOLD_ALREADY_RELEASED", Message = ex.Message });
        }
        catch (HoldAlreadyExpiredException ex)
        {
            return Conflict(new ErrorResponse { Code = "HOLD_ALREADY_EXPIRED", Message = ex.Message });
        }
    }
}
