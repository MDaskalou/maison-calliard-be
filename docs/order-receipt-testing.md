# Orderbekräftelse (Resend) – test i Stripe test mode

## Resend

1. Skapa konto på [resend.com](https://resend.com) och en API-nyckel (`re_...`).
2. I utveckling kan du använda avsändaren `onboarding@resend.dev` (Resends testdomän).
3. Sätt i `appsettings.Development.json` (eller User Secrets):

```json
"Resend": {
  "ApiKey": "re_din_nyckel",
  "Enabled": true
}
```

4. Med `"Enabled": false` loggas mailet i konsolen istället för att skickas och ordern markeras inte som mailad.

## Stripe webhooks lokalt

Stripe skickar `payment_intent.succeeded` till er API. Utan forwarding når bara `confirm-intent` kvittologiken.

```bash
stripe listen --forward-to http://localhost:5089/api/payments/webhook
```

Kopiera webhook signing secret (`whsec_...`) till `Stripe:WebhookSecret` i `appsettings.Development.json`.

## Testkort

`4242 4242 4242 4242` – godkänd betalning. Kunden får:

- Stripes betalningskvitto (via `ReceiptEmail` på PaymentIntent)
- Er orderbekräftelse (Resend), högst ett tack vare `ReceiptSentAt`

## Verifiera idempotens

1. Genomför en beställning med webhook + confirm – kontrollera att bara **ett** ordermail skickas.
2. I databasen: `Orders.ReceiptSentAt` ska vara satt efter lyckat utskick.
