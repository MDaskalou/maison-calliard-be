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

    public PaymentsController(IPaymentService paymentService, IOrderService orderService)
    {
        _paymentService = paymentService;
        _orderService = orderService;
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
            return Problem(title: ex.Message, statusCode: StatusCodes.Status500InternalServerError);
        }
        catch (StripeException ex)
        {
            return Problem(title: "Stripe Checkout session could not be created.", detail: ex.Message, statusCode: StatusCodes.Status502BadGateway);
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
