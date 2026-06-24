using System.Net;
using MaisonCalliard.Application.Receipts;
using MaisonCalliard.Infrastructure.Options;
using MaisonCalliard.Infrastructure.Services;

namespace MaisonCalliard.UnitTests.Receipts;

public sealed class OrderReceiptEmailRendererTests
{
    [Fact]
    public void RenderHtml_includes_order_lines_and_total()
    {
        var model = new OrderReceiptModel
        {
            OrderId = Guid.Parse("aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee"),
            ShortOrderId = "AABBCCDD",
            CustomerName = "Anna Test",
            CustomerEmail = "anna@example.com",
            Location = "Maison Caillard, Mölndal",
            PickupDate = "2026-05-25",
            PickupTime = "11:00",
            Total = 450m,
            Lines =
            [
                new OrderReceiptLineModel
                {
                    Name = "Chokladtarta",
                    OptionLabel = "8 bitar",
                    Price = 450m,
                    Quantity = 1
                }
            ]
        };

        var options = new ReceiptOptions
        {
            CompanyName = "Maison Caillard",
            SupportEmail = "info@maisoncalliard.com"
        };

        var html = OrderReceiptEmailRenderer.RenderHtml(model, options);

        Assert.Contains("Anna Test", html);
        Assert.Contains(WebUtility.HtmlEncode("Chokladtarta"), html);
        Assert.Contains("450 kr", html);
        Assert.Contains("AABBCCDD", html);
    }

    [Fact]
    public void RenderSubject_contains_short_order_id()
    {
        var model = new OrderReceiptModel { ShortOrderId = "1234ABCD" };
        var subject = OrderReceiptEmailRenderer.RenderSubject(model);
        Assert.Contains("1234ABCD", subject);
    }

    [Fact]
    public void RenderInternalHtml_includes_customer_pickup_payment_and_receipt_data()
    {
        var model = new OrderReceiptModel
        {
            OrderId = Guid.Parse("aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee"),
            ShortOrderId = "AABBCCDD",
            ReceiptNumber = "MC-2026-000123",
            CustomerName = "Anna Test",
            CustomerAddress = "Testgatan 1",
            CustomerEmail = "anna@example.com",
            Phone = "070-123 45 67",
            Message = "Ring vid ankomst",
            Location = "Maison Caillard, Molndal",
            PickupDate = "2026-05-25",
            PickupTime = "11:00",
            Total = 450m,
            PaymentMethod = "Stripe kortbetalning",
            Lines =
            [
                new OrderReceiptLineModel
                {
                    Name = "Chokladtarta",
                    OptionLabel = "8 bitar",
                    Price = 450m,
                    Quantity = 1
                }
            ]
        };

        var html = InternalOrderNotificationEmailRenderer.RenderHtml(model);

        Assert.Contains("MC-2026-000123", html);
        Assert.Contains(model.OrderId.ToString(), html);
        Assert.Contains("Anna Test", html);
        Assert.Contains("Testgatan 1", html);
        Assert.Contains("070-123 45 67", html);
        Assert.Contains("anna@example.com", html);
        Assert.Contains(WebUtility.HtmlEncode("Chokladtarta"), html);
        Assert.Contains("8 bitar", html);
        Assert.Contains("2026-05-25 11:00", html);
        Assert.Contains("Maison Caillard, Molndal", html);
        Assert.Contains("Ring vid ankomst", html);
        Assert.Contains("450 kr", html);
        Assert.Contains("Stripe kortbetalning", html);
    }
}
