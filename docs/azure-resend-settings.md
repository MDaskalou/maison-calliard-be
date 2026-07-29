# Azure Resend App Settings

Sätt i Azure Portal → App Service `maison-calliard-be` → Configuration → Application settings, eller med Azure CLI:

```bash
az webapp config appsettings set \
  --name maison-calliard-be \
  --resource-group <resource-group> \
  --settings \
    Resend__ApiKey='re_...' \
    Resend__Enabled='true' \
    ORDER_REQUEST_TO_EMAIL='info@maisoncaillard.com' \
    ORDER_REQUEST_FROM_EMAIL='info@maisoncaillard.com' \
    ORDER_NOTIFICATION_EMAIL='info@maisoncaillard.com' \
    Receipt__FromEmail='info@maisoncaillard.com'
```

Verifiera avsändardomän i [Resend Dashboard](https://resend.com) innan go-live.

| Setting | Används av |
|---------|------------|
| `Resend__ApiKey` / `Resend__Enabled` | Alla utskick via Resend |
| `ORDER_REQUEST_TO_EMAIL` / `ORDER_REQUEST_FROM_EMAIL` | `POST /api/order-requests` |
| `Receipt__FromEmail` / `ORDER_NOTIFICATION_EMAIL` | Betalda kvitton + intern notifiering + `receipt/resend` |
