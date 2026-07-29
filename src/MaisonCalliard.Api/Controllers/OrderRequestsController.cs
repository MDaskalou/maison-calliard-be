using MaisonCalliard.Application.OrderRequests;
using MaisonCalliard.Application.OrderRequests.Dtos;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace MaisonCalliard.Api.Controllers;

[ApiController]
[Route("api/order-requests")]
public sealed class OrderRequestsController : ControllerBase
{
    private readonly IOrderRequestService _orderRequestService;
    private readonly ILogger<OrderRequestsController> _logger;

    public OrderRequestsController(
        IOrderRequestService orderRequestService,
        ILogger<OrderRequestsController> logger)
    {
        _orderRequestService = orderRequestService;
        _logger = logger;
    }

    [AllowAnonymous]
    [HttpPost]
    public async Task<IActionResult> Create(
        [FromBody] CreateOrderRequestMailDto? request,
        CancellationToken cancellationToken)
    {
        if (request is null)
        {
            return BadRequest(new { message = "Missing order request details." });
        }

        try
        {
            var result = await _orderRequestService.SendAsync(request, cancellationToken);
            if (result.ConfirmationSent)
            {
                return Ok(new { ok = true, confirmationSent = true });
            }

            return Ok(new
            {
                ok = true,
                confirmationSent = false,
                confirmationError = result.ConfirmationError
            });
        }
        catch (ArgumentException)
        {
            return BadRequest(new { message = "Missing order request details." });
        }
        catch (OrderRequestConfigurationException ex)
        {
            _logger.LogError(ex, "Order-request configuration error.");
            return StatusCode(StatusCodes.Status500InternalServerError, new { message = ex.Message });
        }
        catch (OrderRequestDeliveryException ex)
        {
            _logger.LogError(ex, "Order-request cafe email delivery failed.");
            return StatusCode(StatusCodes.Status502BadGateway, new { message = "Kunde inte skicka beställningsförfrågan just nu." });
        }
    }

    [AllowAnonymous]
    [HttpGet]
    [HttpPut]
    [HttpPatch]
    [HttpDelete]
    public IActionResult MethodNotAllowed() =>
        StatusCode(StatusCodes.Status405MethodNotAllowed, new { message = "Method not allowed" });
}
