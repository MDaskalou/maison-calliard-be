using MaisonCalliard.Application.Orders;
using MaisonCalliard.Application.Payments;
using MaisonCalliard.Application.Payments.Dtos;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Stripe;

namespace MaisonCalliard.Api.Controllers;

[ApiController]
[Route("api/payments")]
public sealed class PaymentsController : ControllerBase
{
    private readonly IPaymentService _paymentService;
    private readonly IOrderService _orderService;
    private readonly ILogger<PaymentsController> _logger;

    public PaymentsController(
        IPaymentService paymentService,
        IOrderService orderService,
        ILogger<PaymentsController> logger)
    {
        _paymentService = paymentService;
        _orderService = orderService;
        _logger = logger;
    }

    [AllowAnonymous]
    [HttpPost("create-session")]
    public async Task<IActionResult> CreateSession([FromBody] CreatePaymentSessionRequest request, CancellationToken cancellationToken)
    {
        try
        {
            var result = await _paymentService.CreateSessionAsync(request, cancellationToken);
            return Ok(result);
        }
        catch (ArgumentException ex)
        {
            return BadRequest(new { title = ex.Message });
        }
        catch (InvalidOperationException ex)
        {
            _logger.LogError(ex, "Payment session creation failed because backend configuration is invalid.");
            return StatusCode(StatusCodes.Status500InternalServerError, new { title = ex.Message });
        }
        catch (StripeException ex)
        {
            _logger.LogError(ex, "Stripe Checkout session could not be created.");
            return StatusCode(StatusCodes.Status502BadGateway, new
            {
                title = "Stripe Checkout session could not be created.",
                detail = ex.Message
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unexpected error while creating Stripe Checkout session.");
            return StatusCode(StatusCodes.Status500InternalServerError, new
            {
                title = "Unexpected error while creating Stripe Checkout session.",
                traceId = HttpContext.TraceIdentifier
            });
        }
    }

    [AllowAnonymous]
    [HttpPost("create-intent")]
    public async Task<IActionResult> CreateIntent([FromBody] CreatePaymentIntentRequest request, CancellationToken cancellationToken)
    {
        try
        {
            var result = await _paymentService.CreatePaymentIntentAsync(request, cancellationToken);
            return Ok(result);
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { title = ex.Message });
        }
    }

    [AllowAnonymous]
    [HttpPost("confirm-intent")]
    public async Task<IActionResult> ConfirmIntent([FromBody] ConfirmPaymentIntentRequest request, CancellationToken cancellationToken)
    {
        if (request.OrderId is null && string.IsNullOrWhiteSpace(request.PaymentIntentId))
        {
            return BadRequest(new { title = "OrderId or PaymentIntentId is required." });
        }

        try
        {
            var orderId = await _paymentService.ConfirmPaymentIntentAsync(request, cancellationToken);
            var order = await _orderService.GetByIdAsync(orderId, cancellationToken);
            return order is null ? NotFound() : Ok(order);
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { title = ex.Message });
        }
    }

    [AllowAnonymous]
    [HttpPost("webhook")]
    public async Task<IActionResult> Webhook(CancellationToken cancellationToken)
    {
        using var reader = new StreamReader(Request.Body);
        var payload = await reader.ReadToEndAsync(cancellationToken);
        var stripeSignature = Request.Headers["Stripe-Signature"].ToString();

        try
        {
            await _paymentService.HandleWebhookAsync(payload, stripeSignature, cancellationToken);
            return Ok();
        }
        catch (Stripe.StripeException)
        {
            return BadRequest();
        }
    }
}
