using System.Net;
using System.Text;
using MaisonCalliard.Application.OrderRequests.Dtos;

namespace MaisonCalliard.Infrastructure.Services;

internal static class OrderRequestEmailRenderer
{
    public const string CafeSubject = "Beställningsförfrågan Maison Caillard";
    public const string CustomerSubject = "Vi har tagit emot din forfragan | Maison Caillard";

    public static string RenderCafeHtml(CreateOrderRequestMailDto request)
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
                      <tr><td>
                        <h1 style="margin:0 0 16px;font-size:24px;font-weight:700;">Beställningsförfrågan</h1>
                      </td></tr>
            """);

        AppendSection(sb, "Kund", [
            ("Namn", request.CustomerName),
            ("E-post", request.CustomerEmail),
            ("Telefon", request.CustomerPhone),
            ("Hämtas", request.PickupDate),
            ("Plats", request.PickupLocation),
            ("Meddelande", request.Message)
        ]);

        sb.Append("""
                      <tr><td style="padding:18px 0 8px;">
                        <h2 style="margin:0 0 10px;font-size:15px;text-transform:uppercase;letter-spacing:.08em;color:#8d6d54;">Önskemål</h2>
                        <table role="presentation" width="100%" cellpadding="0" cellspacing="0" style="border-collapse:collapse;">
                          <tr>
                            <th align="left" style="padding:8px;border-bottom:1px solid #eadfd4;">Produkt</th>
                            <th align="left" style="padding:8px;border-bottom:1px solid #eadfd4;">Storlek</th>
                          </tr>
            """);

        foreach (var item in request.Items)
        {
            sb.Append($"""
                          <tr>
                            <td style="padding:8px;border-bottom:1px solid #f1e9e1;">{Value(item.Wish)}</td>
                            <td style="padding:8px;border-bottom:1px solid #f1e9e1;">{Value(item.Size)}</td>
                          </tr>
                """);
        }

        sb.Append("""
                        </table>
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

    public static string RenderCustomerHtml(CreateOrderRequestMailDto request)
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
                      <tr><td>
                        <h1 style="margin:0 0 12px;font-size:24px;font-weight:700;">Tack för din förfrågan</h1>
                        <p style="margin:0 0 16px;line-height:1.5;">
                          Hej {name}, vi har tagit emot din beställningsförfrågan och återkommer så snart vi kan.
                        </p>
                      </td></tr>
            """.Replace("{name}", WebUtility.HtmlEncode(request.CustomerName.Trim())));

        AppendSection(sb, "Sammanfattning", [
            ("Hämtas", request.PickupDate),
            ("Plats", request.PickupLocation),
            ("Meddelande", request.Message)
        ]);

        sb.Append("""
                      <tr><td style="padding:18px 0 8px;">
                        <h2 style="margin:0 0 10px;font-size:15px;text-transform:uppercase;letter-spacing:.08em;color:#8d6d54;">Önskemål</h2>
                        <table role="presentation" width="100%" cellpadding="0" cellspacing="0" style="border-collapse:collapse;">
                          <tr>
                            <th align="left" style="padding:8px;border-bottom:1px solid #eadfd4;">Produkt</th>
                            <th align="left" style="padding:8px;border-bottom:1px solid #eadfd4;">Storlek</th>
                          </tr>
            """);

        foreach (var item in request.Items)
        {
            sb.Append($"""
                          <tr>
                            <td style="padding:8px;border-bottom:1px solid #f1e9e1;">{Value(item.Wish)}</td>
                            <td style="padding:8px;border-bottom:1px solid #f1e9e1;">{Value(item.Size)}</td>
                          </tr>
                """);
        }

        sb.Append("""
                        </table>
                      </td></tr>
                      <tr><td style="padding-top:16px;">
                        <p style="margin:0;color:#7b6757;">Maison Caillard</p>
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
