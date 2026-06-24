using System.Net;
using System.Text;
using MaisonCalliard.Application.Receipts;

namespace MaisonCalliard.Infrastructure.Services;

internal static class InternalOrderNotificationEmailRenderer
{
    public static string RenderSubject(OrderReceiptModel order)
    {
        var reference = string.IsNullOrWhiteSpace(order.ReceiptNumber)
            ? order.ShortOrderId
            : order.ReceiptNumber;

        return $"Ny beställning har kommit in - {reference}";
    }

    public static string RenderHtml(OrderReceiptModel order)
    {
        var sb = new StringBuilder();
        sb.Append("""
            <!DOCTYPE html>
            <html lang="sv">
            <head>
              <meta charset="utf-8" />
              <meta name="viewport" content="width=device-width, initial-scale=1" />
            </head>
            <body style="margin:0;padding:24px;background:#f6f2ed;font-family:Arial,sans-serif;color:#2f2924;">
              <table role="presentation" width="100%" cellpadding="0" cellspacing="0">
                <tr>
                  <td align="center">
                    <table role="presentation" width="100%" style="max-width:720px;background:#ffffff;border:1px solid #e8ded2;border-radius:12px;padding:24px;">
            """);

        sb.Append($"""
                      <tr><td>
                        <h1 style="margin:0 0 6px;font-size:24px;font-weight:700;">Ny beställning har kommit in</h1>
                        <p style="margin:0 0 20px;color:#7b6757;">{Value(order.ReceiptNumber)} · {WebUtility.HtmlEncode(order.OrderId.ToString())}</p>
                      </td></tr>
            """);

        AppendSection(sb, "Kund", [
            ("Namn", order.CustomerName),
            ("Adress", order.CustomerAddress),
            ("Telefon", order.Phone),
            ("E-post", order.CustomerEmail)
        ]);

        sb.Append("""
                      <tr><td style="padding:18px 0 8px;">
                        <h2 style="margin:0 0 10px;font-size:15px;text-transform:uppercase;letter-spacing:.08em;color:#8d6d54;">Produkter</h2>
                        <table role="presentation" width="100%" cellpadding="0" cellspacing="0" style="border-collapse:collapse;">
                          <tr>
                            <th align="left" style="padding:8px;border-bottom:1px solid #eadfd4;">Produkt</th>
                            <th align="left" style="padding:8px;border-bottom:1px solid #eadfd4;">Val</th>
                            <th align="right" style="padding:8px;border-bottom:1px solid #eadfd4;">Antal</th>
                            <th align="right" style="padding:8px;border-bottom:1px solid #eadfd4;">Rad</th>
                          </tr>
            """);

        foreach (var line in order.Lines)
        {
            sb.Append($"""
                          <tr>
                            <td style="padding:8px;border-bottom:1px solid #f1e9e1;">{Value(line.Name)}</td>
                            <td style="padding:8px;border-bottom:1px solid #f1e9e1;">{Value(line.OptionLabel)}</td>
                            <td align="right" style="padding:8px;border-bottom:1px solid #f1e9e1;">{line.Quantity}</td>
                            <td align="right" style="padding:8px;border-bottom:1px solid #f1e9e1;">{line.Price * line.Quantity:0.##} kr</td>
                          </tr>
                """);
        }

        sb.Append("""
                        </table>
                      </td></tr>
            """);

        AppendSection(sb, "Upphämtning och betalning", [
            ("Hämtas", $"{order.PickupDate} {order.PickupTime}"),
            ("Plats", order.Location),
            ("Kommentar", order.Message),
            ("Totalbelopp", $"{order.Total:0.##} kr"),
            ("Betalningsmetod", order.PaymentMethod)
        ]);

        sb.Append("""
                    </table>
                  </td>
                </tr>
              </table>
            </body>
            </html>
            """);

        return sb.ToString();
    }

    private static void AppendSection(StringBuilder sb, string title, IReadOnlyList<(string Label, string? Value)> rows)
    {
        sb.Append($"""
                      <tr><td style="padding:18px 0 8px;">
                        <h2 style="margin:0 0 10px;font-size:15px;text-transform:uppercase;letter-spacing:.08em;color:#8d6d54;">{WebUtility.HtmlEncode(title)}</h2>
                        <table role="presentation" width="100%" cellpadding="0" cellspacing="0" style="border-collapse:collapse;">
            """);

        foreach (var row in rows)
        {
            sb.Append($"""
                          <tr>
                            <td style="width:180px;padding:7px 8px;border-bottom:1px solid #f1e9e1;color:#7b6757;">{WebUtility.HtmlEncode(row.Label)}</td>
                            <td style="padding:7px 8px;border-bottom:1px solid #f1e9e1;">{Value(row.Value)}</td>
                          </tr>
                """);
        }

        sb.Append("""
                        </table>
                      </td></tr>
            """);
    }

    private static string Value(string? value) =>
        string.IsNullOrWhiteSpace(value) ? "-" : WebUtility.HtmlEncode(value);
}
