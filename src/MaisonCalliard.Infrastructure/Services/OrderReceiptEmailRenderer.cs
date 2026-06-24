using System.Net;
using System.Text;
using MaisonCalliard.Application.Receipts;
using MaisonCalliard.Infrastructure.Options;

namespace MaisonCalliard.Infrastructure.Services;

internal static class OrderReceiptEmailRenderer
{
    public static string RenderSubject(OrderReceiptModel order) =>
        $"Orderbekräftelse – Maison Caillard (#{order.ShortOrderId})";

    public static string RenderHtml(OrderReceiptModel order, ReceiptOptions options)
    {
        var sb = new StringBuilder();
        sb.Append("""
            <!DOCTYPE html>
            <html lang="sv">
            <head>
              <meta charset="utf-8" />
              <meta name="viewport" content="width=device-width, initial-scale=1" />
            </head>
            <body style="margin:0;padding:0;background:#FDFBF7;font-family:Georgia,'Times New Roman',serif;color:#4A3728;">
              <table role="presentation" width="100%" cellpadding="0" cellspacing="0" style="background:#FDFBF7;padding:32px 16px;">
                <tr>
                  <td align="center">
                    <table role="presentation" width="100%" style="max-width:560px;background:#ffffff;border-radius:24px;border:1px solid rgba(74,55,40,0.08);padding:32px;">
            """);

        sb.Append($"""
                      <tr><td style="padding-bottom:24px;border-bottom:1px solid #FDFBF7;">
                        <h1 style="margin:0 0 8px;font-size:28px;font-weight:normal;">Beställningsbekräftelse</h1>
                        <p style="margin:0;font-size:10px;letter-spacing:0.3em;text-transform:uppercase;color:#A67B5B;font-family:Arial,sans-serif;font-weight:700;">
                          Kundkopia • {WebUtility.HtmlEncode(DateTime.Now.ToString("yyyy-MM-dd"))}
                        </p>
                      </td></tr>
            """);

        sb.Append($"""
                      <tr><td style="padding:24px 0;font-family:Arial,sans-serif;font-size:14px;">
                        <p style="margin:0 0 8px;">Hej {WebUtility.HtmlEncode(order.CustomerName)},</p>
                        <p style="margin:0;opacity:0.8;">Tack för din beställning hos {WebUtility.HtmlEncode(options.CompanyName)}. Här är en sammanfattning.</p>
                      </td></tr>
            """);

        sb.Append("""<tr><td style="padding-bottom:16px;">""");
        foreach (var line in order.Lines)
        {
            var lineTotal = line.Price * line.Quantity;
            sb.Append($"""
              <table role="presentation" width="100%" style="margin-bottom:16px;">
                <tr>
                  <td>
                    <p style="margin:0;font-size:18px;font-weight:bold;">{WebUtility.HtmlEncode(line.Name)}</p>
                    <p style="margin:4px 0 0;font-size:10px;letter-spacing:0.2em;text-transform:uppercase;color:#A67B5B;font-family:Arial,sans-serif;">{WebUtility.HtmlEncode(line.OptionLabel)}</p>
                  </td>
                  <td align="right" style="font-size:16px;font-weight:bold;white-space:nowrap;">{lineTotal:0} kr</td>
                </tr>
              </table>
            """);
        }
        sb.Append("</td></tr>");

        sb.Append($"""
                      <tr><td style="background:#FDFBF7;border-radius:16px;padding:20px;font-family:Arial,sans-serif;font-size:12px;">
                        <table role="presentation" width="100%">
                          <tr>
                            <td style="opacity:0.5;text-transform:uppercase;letter-spacing:0.2em;font-size:10px;font-weight:700;">Upphämtning</td>
                            <td align="right" style="font-weight:700;">{WebUtility.HtmlEncode(order.PickupDate)} @ {WebUtility.HtmlEncode(order.PickupTime)}</td>
                          </tr>
                          <tr><td colspan="2" style="height:12px;"></td></tr>
                          <tr>
                            <td style="opacity:0.5;text-transform:uppercase;letter-spacing:0.2em;font-size:10px;font-weight:700;">Plats</td>
                            <td align="right" style="font-weight:700;">{WebUtility.HtmlEncode(order.Location)}</td>
                          </tr>
                        </table>
                      </td></tr>
            """);

        if (order.TaxAmount > 0)
        {
            sb.Append($"""
                      <tr><td style="padding-top:16px;font-family:Arial,sans-serif;font-size:12px;">
                        <table role="presentation" width="100%">
                          <tr>
                            <td style="opacity:0.5;">Varav moms</td>
                            <td align="right">{order.TaxAmount:0} kr</td>
                          </tr>
                        </table>
                      </td></tr>
            """);
        }

        sb.Append($"""
                      <tr><td style="padding-top:24px;border-top:1px solid rgba(74,55,40,0.08);">
                        <table role="presentation" width="100%">
                          <tr>
                            <td style="font-size:10px;text-transform:uppercase;letter-spacing:0.4em;opacity:0.3;font-family:Arial,sans-serif;font-weight:700;">Totalt betalt</td>
                            <td align="right" style="font-size:36px;font-weight:bold;">{order.Total:0} kr</td>
                          </tr>
                        </table>
                      </td></tr>
                  <tr><td style="padding-top:24px;font-family:Arial,sans-serif;font-size:11px;opacity:0.7;line-height:1.6;">
                    <p style="margin:0 0 4px;"><strong>Order-ID:</strong> {WebUtility.HtmlEncode(order.ShortOrderId)}</p>
            """);

        if (!string.IsNullOrWhiteSpace(order.ReceiptNumber))
        {
            sb.Append($"<p style=\"margin:0 0 4px;\"><strong>Kvittonummer:</strong> {WebUtility.HtmlEncode(order.ReceiptNumber)}</p>");
        }

        if (!string.IsNullOrWhiteSpace(order.Message))
        {
            sb.Append($"<p style=\"margin:8px 0 0;\"><strong>Meddelande:</strong> {WebUtility.HtmlEncode(order.Message)}</p>");
        }

        sb.Append($"""
                        <p style="margin:16px 0 0;">Frågor? {WebUtility.HtmlEncode(options.SupportEmail)} · {WebUtility.HtmlEncode(options.Phone)}</p>
                        <p style="margin:8px 0 0;font-size:10px;">Mölndal: {WebUtility.HtmlEncode(options.MolndalAddress)}<br/>Järntorget: {WebUtility.HtmlEncode(options.JarntorgetAddress)}</p>
                      </td></tr>
                    </table>
                  </td>
                </tr>
              </table>
            </body>
            </html>
            """);

        return sb.ToString();
    }
}
