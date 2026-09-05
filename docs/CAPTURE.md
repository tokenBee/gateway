# Interaction capture

TokenBee retains AI interactions when capture is enabled. Requests are always proxied to the provider.

## Precedence

1. Per-request `X-TokenBee-Capture` / SDK `capture`
2. Project setting (`capture_settings`)
3. Default: on

When content is not retained, TokenBee still stores tokens, cost, latency, model, provider, and status.

## Retention

| Plan | Captured interactions / month | Max retention |
| ---- | ----------------------------- | ------------- |
| Free | 1,000 | 3 days |
| Pro (`paid`) | 25,000 | 30 days |
| Team | 100,000 | 90 days |

Over-limit requests are not blocked. Content is simply not stored.

## Migration

Run `db/migrate_capture.sql` on existing databases.

## Stripe

Existing `Stripe:PriceId` is Pro ($19). Optional `Stripe:TeamPriceId` for Team ($49).
If Team price is unset, Team checkout uses the Pro price ID.
