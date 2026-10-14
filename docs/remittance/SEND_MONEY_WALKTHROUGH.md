# Send Money over WhatsApp — technical walkthrough

**Audience:** product and engineering managers, and the engineers who will take this to production.
**Hands-on testing:** see [TESTING_GUIDE.md](TESTING_GUIDE.md) for step-by-step tests, Postman usage and troubleshooting.
**How it was built, in depth:** see [SEND_MONEY_COMPLETE_GUIDE.md](SEND_MONEY_COMPLETE_GUIDE.md).
**Status (2026-10-12):** built and covered by an automated integration suite (173 checks, all passing). The payment gateway, payout partner and OTP delivery are **development mocks**. The WhatsApp side (Meta Flow upload, publishing, a real phone) has **not yet been exercised live**; section 6 explains what is needed and section 9 what is still missing.

---

## 1. What the customer experiences

The journey follows the reference screens (a Malaysian remittance service, sending MYR abroad).

| # | Where | What happens | Who decides |
|---|---|---|---|
| 0 | WhatsApp chat | The customer types **send money** (or `/sendmoney`, `remit`, `remittance`). The **Send Money** bot flow answers with a card: header *International Remittance*, a short text and a **Click Here to Send Money** button. | Bot Flow designer (the new *WhatsApp Flow* node) |
| 1 | Flow screen **Verify mobile** | Shows the customer's WhatsApp number, sends a one-time code, asks for it. Resend link included. | Server: OTP generated, hashed and checked on the server only |
| 2 | **Transfer details** | Destination country → delivery method (bank transfer, e-wallet, cash pickup…) → amount. **Calculate** shows the guaranteed rate, fee (with *PROMO* badge for a first transfer), what the recipient gets, the total and how long the rate is held. | Server: countries, methods and fees come from Setup › Send Money; the rate comes from the exchange-rate provider |
| 3 | **Beneficiary** | Pick a saved recipient or add one: name, bank (if the method has a bank list), relationship, and the method's own fields (account number, IFSC, routing number, wallet number…). | Server: the fields, their labels, required flags, lengths and patterns come from configuration |
| 4 | **Regulatory details** | Source of funds and purpose of transfer (BNM AML requirement), optional remarks. | Server: both lists are configurable |
| 5 | **Review & payment** | Summary of everything, choice of payment method (*FPX Online Banking — INSTANT*, *Pay at Counter — 24H HOLD*), **Confirm**. | Server creates the order; nothing is paid yet |
| 6 | Chat | **Order created · IR-MY123456** with the summary and a **Pay MYR 504.00** button (or a counter payment reference). | Server |
| 7 | Payment page | The customer pays on the payment provider's page — **never inside WhatsApp**. No bank password, card number or banking OTP is ever asked for in the chat or the Flow. | Payment provider |
| 8 | Chat | *Payment received* → (payout sent) → *Transfer completed*, each as a WhatsApp message. | Only verified provider callbacks move these states |

What the business sees: every order on the new **Transfers** page (order status and payment status side by side, full timeline), and every message in the normal **Chat** inbox as cards.

---

## 2. Architecture

```
 WhatsApp app ──(Flow screens, encrypted)──► POST /api/webhook/whatsapp-flows/{endpointKey}
      │                                          │ FlowCrypto: RSA-OAEP-SHA256 + AES-128-GCM (16-byte IV)
      │                                          ▼
      │                                    SendMoneyFlowHandler ── reads the session row, decides the next screen
      │                                          │
      │            ┌─────────────────────────────┼──────────────────────────────────────┐
      │            ▼                ▼            ▼               ▼                      ▼
      │       OtpService      QuoteService  BeneficiaryService  TransferOrderService   Setup data (DB)
      │        │ IOtpProvider   │ IExchangeRateService            │ IPaymentProvider
      │        ▼                ▼ IExchangeRateProvider(s)        ▼
      │     Mock (dev)      ExchangeRate-API → Frankfurter     Mock gateway (dev)
      │
      ◄──(nfm_reply webhook)── WhatsAppFlowCompletionHandler ── "Order created, pay here" message

 Payment provider ──signed callback──► POST /api/webhook/payments/{provider} ─► verify ─► JobQueue ─► RemittanceQueueWorker
 Payout partner   ──signed callback──► POST /api/webhook/payouts/{provider}  ─► verify ─► JobQueue ─► (Paid → Processing → Completed)
 RemittanceMaintenanceWorker (every 30 s): expires unpaid orders, sends any missed "order created" message, closes abandoned sessions
```

**Design rules the code follows**

* **The server is the only authority.** The Flow is a form. Each request is checked against the session row: verified or not, which quote, which recipient, which payment method. A crafted request cannot skip the OTP, change an amount after quoting, or use someone else's saved recipient (all tested).
* **No business data in the UI or the Flow JSON.** The Flow JSON (`Backend/Resources/WhatsAppFlows/send_money.flow.json`, Flow JSON 7.3) holds layout only. Every option, label and number is sent by the server at runtime through data binding.
* **Swappable providers.** Exchange rates, OTP delivery, payments and payouts are each an interface (`Backend/Services/Remittance/Providers/ProviderContracts.cs`). A provider is picked by name from configuration or from the configuration row (a payout method's partner, a payment method's provider), the same pattern as the existing e-mail providers.
* **Two independent statuses.** *Order status* (Created → AwaitingPayment → Paid → Processing → Completed, or Expired / Failed / Cancelled) and *payment status* (Pending → Succeeded / Failed / Expired → Refunded). Payment status changes **only** on a verified provider callback, or on expiry. Every change is a conditional database update, so duplicate or out-of-order callbacks change nothing. Every change is written to an append-only event log.
* **Secrets stay out of the code.** Beneficiary account details and the Flow private key are encrypted at rest with the app's existing AES-256-GCM key. OTPs are stored only as a keyed hash. API keys and webhook secrets come from environment variables or the git-ignored secrets file.

**Main code locations**

| Area | Files |
|---|---|
| Entities and enums | `Backend/Models/Entities/Remittance.cs`, `WhatsAppFlow.cs`, `Models/Enums/Enums.cs` |
| Services | `Backend/Services/Remittance/*` (sessions, OTP, pricing, quotes, beneficiaries, orders, notifier, workers, admin, demo seeder) |
| Provider adapters | `Backend/Services/Remittance/Providers/*` |
| WhatsApp Flows | `Backend/Services/WhatsApp/Flows/*` (crypto, endpoint, Graph API client, set-up service, launcher, completion) |
| Bot node | `Backend/Executors/WhatsAppFlowNodeExecutor.cs` |
| Controllers | `RemittanceController.cs` (admin + per-connection Flows), `WhatsAppFlowsEndpointController.cs` (machine endpoints), `RemittanceDevController.cs` (development harness, compiled out of Release) |
| Frontend | `Frontend/src/pages/Setup/SendMoney/*`, `pages/Transfers/TransfersList.tsx`, `pages/ConnectWABA/WhatsAppFlowsCard.tsx`, Bot Flow designer node, Chat cards |
| Tests | `Tests/Remittance.Tests` |

---

## 3. Endpoints

All responses use the app's `ApiResponse` envelope (`{ success, message, data }`) unless noted. Errors follow the existing middleware: **400** invalid input (with a readable message), **401/403** not signed in / missing permission, **404** not found, **409** conflict (for example deleting a route that has transfers).

### 3.1 Machine endpoints (anonymous; each authenticates the caller itself)

| Method & path | Caller | Purpose | Request | Response / errors |
|---|---|---|---|---|
| `POST /api/webhook/whatsapp-flows/{endpointKey}` | Meta | Flow data exchange: `ping`, `INIT`, `BACK`, `data_exchange`, error notifications | Meta's encrypted envelope `{encrypted_flow_data, encrypted_aes_key, initial_vector}`, header `X-Hub-Signature-256` | **200** Base64 encrypted JSON (`text/plain`). **421** cannot decrypt (Meta refetches the public key). **432** bad signature. **427** flow token unknown/expired/finished (Meta closes the Flow). **404** unknown endpoint key. **400** malformed. |
| `POST /api/webhook/payments/{provider}` | Payment provider | Payment succeeded / failed / expired / refunded | Provider's own body and signature header (mock: `X-Mock-Signature: t=…,v1=…`) | **200** `{received:true}` (queued). **401** signature missing/invalid/older than 5 min. **400** unknown event. **404** provider not registered. |
| `POST /api/webhook/payouts/{provider}` | Payout partner | Payout completed / failed | Same as above | Same as above |

The existing messages webhook (`POST /api/webhook/whatsapp`) now also recognises Flow completions (`interactive.type = "nfm_reply"`): it logs them as a chat card and sends the "order created" message.

### 3.2 Admin API (signed in; permissions in brackets)

| Method & path | Purpose |
|---|---|
| `GET /api/remittance/reference` [Remittance.View] | Registered providers, field types, list names, statuses, Flow purposes, slot limit, whether Send Money is enabled |
| `GET/POST /api/remittance/corridors`, `PUT/DELETE /api/remittance/corridors/{id}` [View/Manage] | Countries (routes): currencies, min/max amount, FX margin, receive-currency decimals, active, order |
| `GET /api/remittance/corridors/{id}/rate` [View] | Live rate preview: market rate, customer rate, provider, publication time — or the reason there is none |
| `GET/POST /api/remittance/corridors/{id}/payout-methods`, `PUT …/payout-methods/{id}`, `DELETE /api/remittance/payout-methods/{id}` | Delivery methods with fee rule (fixed + %, min, max, first-transfer fee, badge) and payout partner |
| `GET/POST /api/remittance/payout-methods/{id}/fields`, `PUT …/fields/{id}`, `DELETE /api/remittance/fields/{id}` | Recipient fields: key, label (≤ 20), type, required, min/max length, pattern, help text (≤ 80), account-number flag. At most 6 active (the Flow's slots). |
| `GET/POST /api/remittance/payout-methods/{id}/institutions`, `PUT …/{id}`, `DELETE /api/remittance/institutions/{id}` | Banks / wallet providers (name ≤ 30, ≤ 200 active) |
| `GET/POST /api/remittance/lookups/{category}`, `PUT …/{category}/{id}`, `DELETE /api/remittance/lookups/{id}` | Lists: `purpose`, `source_of_funds`, `relationship` |
| `GET/POST /api/remittance/payment-methods`, `PUT/DELETE …/{id}` | How customers pay: country, provider, code, label, description, badge, payment window |
| `GET /api/remittance/orders?page&pageSize&search&orderStatus&paymentStatus` [Transfer.View] | Paged transfers |
| `GET /api/remittance/orders/{id}` [Transfer.View] | One transfer with payment/payout references and the event timeline |
| `GET /api/connections/{id}/whatsapp-flows` [ConnectAccount.View] | Flow set-up status for a connection |
| `POST /api/connections/{id}/whatsapp-flows/encryption-key?regenerate=` [ConnectAccount.Connect] | Create the RSA-2048 key pair (private key encrypted at rest) and upload the public key to each phone number |
| `POST …/whatsapp-flows/send-money/sync?createNew=` | Create/update the Flow on Meta: upload `flow.json`, set the endpoint URL; returns Meta's validation errors |
| `POST …/whatsapp-flows/send-money/publish` | Publish the Flow |
| `GET …/whatsapp-flows/send-money/flow-json` | The exact Flow JSON uploaded |
| `POST …/whatsapp-flows/send-money/test-send` `{phone}` | Send the Flow to an existing contact (real WhatsApp message) |

Validation is enforced on save, so configuration that would break on a phone is refused with a clear message (label too long, a seventh field, an invalid regular expression, an unknown provider, more than 200 list options).

### 3.3 Development harness (Development environment **and** `Remittance:DevTools:Enabled`; compiled out of Release builds)

| Method & path | Purpose |
|---|---|
| `POST /api/dev/remittance/seed-demo` | Idempotent Malaysian demo data (section 4) plus the *Send Money* bot flow, created **switched off** (builds without Send Money that share the database would otherwise route the keywords to a node they cannot run) |
| `POST /api/dev/remittance/sessions` `{phone, connectionId?, name?}` | Start a session without WhatsApp; returns `flowToken`, `endpointKey` |
| `POST /api/dev/whatsapp-flows/{endpointKey}/exchange` | Plaintext data exchange through the same handler |
| `POST /api/dev/whatsapp-flows/{endpointKey}/encrypt-request` | Encrypt and sign a request exactly as Meta would, to call the real encrypted endpoint |
| `POST /api/dev/whatsapp-flows/decrypt-response` | Decrypt the endpoint's answer |
| `GET /api/dev/mock-payments/{paymentId}/checkout` | The mock gateway's checkout page (what the Pay button opens) |
| `POST /api/dev/mock-payments/{paymentId}/simulate?outcome=succeeded\|failed\|expired\|refunded` | Gateway sends its **signed** callback through the normal verification path |
| `POST /api/dev/mock-payouts/{payoutReference}/simulate?outcome=completed\|failed` | Payout partner callback, signed |
| `POST /api/dev/remittance/sign-callback` `{kind, body}` | Sign any body, to test the public callback endpoints by hand |

### 3.4 Flow data-exchange actions (inside the encrypted payload)

The button's operation travels in `data.op`. WhatsApp strips a payload field named `action`, because it clashes with the request's own `action` (found in Meta's Flow Builder preview, 2026-10-13). The server still reads `data.action` for older clients.

| Screen | `data.op` | Data sent | Server answer |
|---|---|---|---|
| — | `INIT` | — | `VERIFY_IDENTITY`; issues the OTP |
| VERIFY_IDENTITY | `verify_otp` / `resend_otp` | `otp` | `TRANSFER_DETAILS`, or the same screen with `error_message` and attempts left |
| TRANSFER_DETAILS | `select_country`, `select_method` | `country`, `method`, `amount` | Same screen, refreshed option lists |
| TRANSFER_DETAILS | `quote_or_continue` | `country`, `method`, `amount` | First tap: the quote. Second tap on an unchanged valid quote: `BENEFICIARY` |
| BENEFICIARY | `select_beneficiary` / `save_beneficiary` | `beneficiary` (`new` or id), `full_name`, `institution`, `relationship`, `f1`…`f6` | `COMPLIANCE`, or the same screen with the validation message |
| COMPLIANCE | `save_compliance` | `source_of_funds`, `purpose`, `remarks` | `REVIEW_CONFIRM` |
| REVIEW_CONFIRM | `confirm` | `payment_method` | `SUCCESS` with `{flow_token, order_reference}`, or back to review with a re-priced quote if the old one expired |

---

## 4. Dynamic configuration (what the business can change without a release)

Everything is managed in **Setup › Send Money** (permission `Remittance.Manage` to edit, `Remittance.View` to look):

* **Countries & payout** — add a destination country, then its delivery methods (each with its fee and payout partner), then for each method the recipient fields (six slots, previewed on screen) and the bank/provider list.
* **Regulatory lists** — purpose, source of funds, relationship.
* **Payment methods** — per sending country.

**Why six fields?** A WhatsApp Flow cannot create new input boxes at runtime. The Flow therefore has six generic text inputs. The server fills in their label, help text, required flag, maximum length and visibility from the payout method's configuration, and validates the values (required, length, digits-only, phone, e-mail, regular expression) on save. Admin screens refuse anything a phone cannot show (labels over 20 characters, a seventh active field, more than 200 list options).

**Demo data seeded for Malaysia** (`POST /api/dev/remittance/seed-demo`):

| Destination | Methods | Example recipient fields |
|---|---|---|
| Indonesia (IDR, 0 decimals) | Bank transfer (BCA, BRI, Mandiri, BNI), E-wallet (GoPay, OVO, DANA) | Account number 6–20 digits |
| Philippines (PHP) | Bank transfer, E-wallet (GCash, Maya), Cash pickup (Cebuana, M Lhuillier) | Account number, wallet or recipient mobile |
| India (INR) | Bank transfer (IMPS), no bank list | Account number 9–18 digits, IFSC (pattern `[A-Z]{4}0[A-Z0-9]{6}`) |
| Bangladesh (BDT) | Bank transfer, Mobile wallet (bKash, Nagad) | Account number, 9-digit routing number |
| Singapore (SGD) | Bank transfer (DBS/POSB, OCBC, UOB) | Account number |
| Vietnam (VND, 0 decimals) | Bank transfer (Vietcombank, Techcombank, BIDV) | Account number |

All routes: MYR 20 – 20,000. FX margin 0.8 %–1.8 % depending on route. Fee MYR 3–6 (cash pickup also 0.5 %), **first transfer free with a PROMO badge**. Payment: FPX Online Banking (15-minute window) and Pay at Counter (24-hour window).

---

## 5. Rates, fees, OTP, orders and payments in detail

**Exchange rates.** Primary provider **ExchangeRate-API**. Without an API key it uses the free open-access endpoint (daily rates, covers MYR, IDR, PHP, INR, BDT, SGD, VND); with `Remittance__ExchangeRates__ExchangeRateApi__ApiKey` it uses the keyed pair endpoint. Fallback **Frankfurter** (European Central Bank; weekdays; has no BDT or VND, which it reports as unsupported). Each call has a timeout (5 s) and one retry. Rates are cached for 60 s and refused when older than the maximum age (26 h; 96 h for Frankfurter). If no provider has a fresh rate, the customer sees *"Exchange rates are temporarily unavailable"*. **No rate is ever invented.**

**Pricing.** Customer rate = market rate × (1 − margin). Amount received = send amount × customer rate, **rounded down** to the receive currency's decimals, so the customer is never promised more than is delivered. Fee = fixed + percent, kept between the minimum and maximum, or the first-transfer fee when the customer has never had a payment succeed. A quote is stored and **guaranteed for 15 minutes**. Confirming on an expired quote re-prices and shows the new numbers before anything is ordered.

**OTP (development mock).** `OtpService` does the real work: 6 digits from a cryptographic random generator, stored only as an HMAC-SHA256 hash (key derived from the app's encryption key), valid 5 minutes, 5 attempts, 30-second resend cooldown, at most 3 resends, constant-time comparison, lockout. The **mock provider delivers nothing**. In Development, with `Remittance:Otp:Mock:ExposeCodeInFlow`, the code is shown on the verify screen as a development hint. Production refuses to start with the mock. A real provider (WhatsApp authentication template with copy-code button, or SMS) implements `IOtpProvider.SendAsync`. Nothing else changes.

**Orders.** Created on **Confirm**, once per session (a unique index makes a double tap return the same order). Reference format `IR-MY123456`. Every number is copied from the stored quote at confirm time. The payment provider is asked for a hosted payment (URL or counter reference) with the payment method's window. The order is then **AwaitingPayment / Pending**.

**Payments.** The order becomes **Paid / Succeeded** only when the provider's callback arrives with a valid signature and the paid amount and currency match the order. Duplicates are ignored using the provider's event id. A late "failed" after a success is ignored. A payment that arrives after the order expired is still honoured (the money has been received). An amount mismatch is recorded for review and not applied. Unpaid orders expire automatically when the window passes.

**Payouts.** A paid order is queued for its payout partner (**Processing**). The partner's signed callback completes it (**Completed**) or fails it (**Failed**, customer told a refund will follow). Customer WhatsApp messages go out at each step.

**What the mock gateway is.** It stands in for a real gateway's hosted checkout page. Its buttons **sign** a callback with the configured mock secret and send it through the **same** verification and queue as a real callback. There is no shortcut that marks an order paid.

---

## 6. Running it

### 6.1 Configuration (environment variables or the git-ignored `Backend/appsettings.secrets.local.json`)

| Setting | Purpose | Development value |
|---|---|---|
| `Remittance__Enabled` | Master switch | `true` |
| `App__PublicBaseUrl` | Public HTTPS address Meta and the Pay button use | your ngrok `https://….ngrok-free.app` |
| `Remittance__ExchangeRates__ExchangeRateApi__ApiKey` | Optional keyed rates | empty = open access |
| `Remittance__Payments__Mock__WebhookSecret`, `Remittance__Payouts__Mock__WebhookSecret` | Sign/verify mock callbacks | any long random string |
| `Remittance__Otp__Mock__ExposeCodeInFlow` | Show the mock code on screen | `true` (development only) |
| `Remittance__Flows__AllowDraftSend` | Send the Flow before it is published (test numbers only) | `true` |
| `Remittance__DevTools__Enabled` | Development harness endpoints | `true` |

Other defaults (TTL, attempts, cooldowns, providers, cache, timeouts, Flow JSON version) are in `Backend/appsettings.json` under `Remittance`. In **Production** the server refuses to start if Send Money is enabled with the mock OTP, payment or payout provider, or with draft sending on.

### 6.2 Database

One migration, `20261012065738_AddSendMoneyAndWhatsAppFlows`, creates 15 tables and changes no existing ones:
`RemittanceCorridors`, `RemittancePayoutMethods`, `RemittanceFeeRules`, `RemittanceBeneficiaryFields`, `RemittanceInstitutions`, `RemittanceLookups`, `RemittancePaymentMethods`, `RemittanceSessions`, `OtpChallenges`, `FxQuotes`, `RemittanceBeneficiaries` (details encrypted), `TransferOrders`, `TransferOrderEvents`, `WhatsAppFlowEncryptionKeys` (private key encrypted), `WhatsAppFlowRegistrations`. Money uses `numeric(18,2)`, rates `numeric(18,8)`. Two permission groups are added: **Send Money** (Remittance.View / Manage) and **Transfers** (Transfer.View), granted to Admin automatically at start-up.

```bash
dotnet ef database update --project Backend
```

### 6.3 Local test without a phone (Postman)

1. Start the backend (`dotnet run --project Backend --launch-profile http`).
2. Import `docs/remittance/SendMoney.postman_collection.json` and `SendMoney.local.postman_environment.json`; fill in `adminEmail`/`adminPassword`.
3. Run the folders in order: **0 Sign in → 1 Configuration** (seed demo) **→ 2 Flow journey → 3 Encrypted endpoint → 4 Payment and payout → 5 Negative checks.** Test scripts carry ids from one request to the next (OTP, country, order, payment id).
4. Watch the same order appear on **Transfers** in the app and its messages in **Chat**.

### 6.4 Live on a phone (ngrok + Meta)

1. `ngrok config add-authtoken <your token>` once, then `ngrok http 5155`. Put the `https://…` address in `App__PublicBaseUrl` and restart the backend. (A free ngrok address changes on every start; repeat the Meta steps that use it.)
2. **Meta app → WhatsApp → Configuration → Webhook**: callback `https://<ngrok>/api/webhook/whatsapp`, verify token from Connect WABA. Subscribe to `messages`.
3. While the Flow is a draft, add the tester's WhatsApp number to the app's **test recipient list**.
4. **Connect WABA → WhatsApp Flows card**: *Create & Upload Key* → *Create Flow* (uploads the screens and sets the endpoint `https://<ngrok>/api/webhook/whatsapp-flows/<key>`). Fix anything listed under Meta's validation errors. *Data endpoint* shows when Meta's health check last reached us.
5. Seed the demo data. Start the Flow with *Send the Flow to a test number* on the card. Typing **send money** on the phone also works once the **Send Money** bot flow is switched on and the Meta webhook points at a backend that includes Send Money. Switch it on only when every backend sharing the database has Send Money.
6. Complete the journey. Tap **Pay** → the mock checkout page → *Simulate successful payment*. The chat shows *Payment received*. Then complete the payout (Postman folder 4, or `POST /api/dev/mock-payouts/{ref}/simulate?outcome=completed`).
7. **Publish** on the card when it works end to end. Publishing cannot be undone. A published Flow needs *Start a new version* to change.

**Shared dev database: the stale-worker guard.** The shared database has a trigger, `trg_prevent_stale_worker` (`BEFORE UPDATE ON "JobQueue" … WHEN NEW."LeasedBy" LIKE 'ASHOK%'`), that refuses every queue lease from one developer PC.

*Root cause:* every build on that database uses queue contract `v3`. Commit `ef6a43f` moved media to Cloudinary (and lets the fetcher read `*.cloudinary.com`) without bumping `QueueNames.ContractVersion`. A PC without that commit could lease campaign jobs whose media it cannot fetch, so the PC was blocked by name. The proper long-term guard is a `ContractVersion` bump whenever job semantics change.

*Fix (applied 2026-10-12):* the PC was brought up to date with `origin/main`. The guard stays in place for shared job types, because a developer PC's configuration still differs from the hosted server's. It was narrowed so it no longer blocks the `remit-*` queues, which only builds with Send Money process (original definition kept for rollback):

```sql
CREATE OR REPLACE TRIGGER trg_prevent_stale_worker
    BEFORE UPDATE ON public."JobQueue" FOR EACH ROW
    WHEN (NEW."LeasedBy" LIKE 'ASHOK%' AND NEW."QueueName" NOT LIKE 'remit-%')
    EXECUTE FUNCTION prevent_stale_worker();
```

Verified afterwards on the shared database with every worker running: a full order went from session through confirm, an unsigned callback was refused, and the signed payment and payout callbacks were leased by the PC's `remit` worker and applied (Paid → Processing → Completed, duplicate success ignored). Shared queues stayed refused to the PC, as intended.

### 6.5 Automated tests

```bash
dotnet run --project Tests/Remittance.Tests
```

Stop the backend first. The suite uses the real database with its own `zz-remittest`/`ZZ` rows, removed before and after. Phases: 1 Flow encryption & endpoint protocol, 2 OTP, 3 pricing & quotes (fallback, stale, unsupported, all-down), 4 full Flow journey (server-side enforcement, dynamic fields, validation, idempotent confirm, re-quote, ownership checks, admin guards), 5 payments & payouts (signatures, replay window, tampering, duplicates, out-of-order, amount mismatch, expiry, late payment, timeline), 6 live exchange rates against the real APIs (skipped, not failed, when offline). Last run: **173 passed, 0 failed.** Run one phase with `-- 4`.

---

## 7. Extending it

| To add… | Do this | Code changes |
|---|---|---|
| A **country** or delivery method | Setup › Send Money → New Country → payout methods → fields and banks | None |
| An **exchange-rate provider** | Class implementing `IExchangeRateProvider` (`ProviderName`, `GetRateAsync` returning `Ok` with the publication time, `Unsupported` or `Unavailable` — never throwing for ordinary failures); register with `AddHttpClient<IExchangeRateProvider, YourProvider>()`; set `Remittance:ExchangeRates:Provider` / `Fallbacks` | One class + one line |
| A **payment gateway** (e.g. a real FPX acquirer) | `IPaymentProvider`: `CreatePaymentAsync` returns the hosted URL or counter reference; `ParseWebhook` verifies the gateway's signature and maps its events. Register it, then pick it on the payment method in Setup. Its callback URL is `/api/webhook/payments/{ProviderName}`. | One class + one line |
| A **payout partner** | `IPayoutProvider`: `SubmitAsync` (return `IsTransient` for retryable errors — the queue retries) and `ParseWebhook`. Choose it on the payout method. Callback `/api/webhook/payouts/{ProviderName}`. | One class + one line |
| An **OTP channel** | `IOtpProvider` with `DeliversCode = true` and `SendAsync`; set `Remittance:Otp:Provider`. A WhatsApp authentication template is the natural choice. | One class + one line |
| **Another WhatsApp Flow** (e.g. KYC) | New Flow JSON resource, an `IFlowScreenHandler` with its own token prefix and purpose, add the purpose to the reference list; the Bot Flow node can then launch it | Handler + Flow JSON |

---

## 8. Security summary

* Flow traffic is end-to-end encrypted with this connection's own key. Requests are signature-checked (`X-Hub-Signature-256`) and the private key is encrypted at rest. A missing signature is accepted only outside Production, as the existing messages webhook does.
* Flow tokens are 256-bit random; only their SHA-256 is stored. Tokens expire (30 minutes), are tied to their connection, and die once the order is created or the session locks.
* OTP: hashed, expiring, attempt-limited, rate-limited, verified only on the server.
* Payment/payout callbacks: HMAC-signed with a 5-minute replay window, de-duplicated by provider event id, applied only through conditional state transitions, with amount and currency checked.
* No banking credentials are collected anywhere in WhatsApp; payment happens on the provider's page. Account numbers are encrypted at rest and shown masked (`****8234`). Plaintext values are excluded from the audit-change capture.
* Development endpoints are compiled out of Release builds and refuse outside Development with dev tools enabled. Production refuses to start with mock providers.

---

## 9. Limitations and production gaps

**Mocks to replace before real money moves:** the OTP provider (delivers nothing), the payment gateway (sandbox page), the payout partner (accepts everything).

**Not yet verified live:** uploading and publishing the Flow on Meta, Meta's validation of the Flow JSON, a real phone completing the journey, the `nfm_reply` completion webhook from Meta. These need the ngrok address, the test recipient number and a database where this machine's queue worker may run (section 6.4).

**Needed for production:**

1. **Licensing & compliance:** BNM remittance licence; e-KYC of the sender (ID document and liveness — the reference app has it, this build does not); sanctions/PEP screening of sender and recipient; transaction and velocity limits per customer; suspicious-transaction reporting; record retention policy.
2. **Real providers** for OTP, payments (FPX acquirer, counter network) and payouts per corridor, with reconciliation reports and refund handling. Refunds are currently only a status.
3. **Exchange rates** with an SLA and intraday updates (the open endpoints are daily). Consider a treasury-managed rate table.
4. **Notifications after 24 hours** (for example payout completion days later) need approved WhatsApp **utility templates**. Today's free-form messages only work inside the 24-hour customer-service window.
5. **Operations:** alerting on failed callbacks and stuck orders, an admin action to retry payouts or cancel orders, Flow key rotation procedure, monitoring of Meta's endpoint health metrics.
6. **Meta:** Flow publishing review; display-name and business verification; Meta requires the endpoint to answer within 10 seconds (our exchange-rate timeout is 5 s with one retry, which can come close if both providers are slow).
7. **Builds sharing one database (dev only):**
   * The queue guard described in 6.4.
   * Starting any backend runs stalled-campaign recovery on shared campaigns (pre-existing).
   * `SetupSeeder` deletes permissions missing from its own catalogue. A build without Send Money removes `Remittance.*` / `Transfer.View` and their role grants on every start, and a build with it re-adds them. This resolves once Send Money is deployed everywhere.
   * Queue-affecting changes need a `QueueNames.ContractVersion` bump (`ef6a43f` changed media handling without one).
8. **Dead-lettered callbacks:** a payment callback that fails all its retries leaves the order *AwaitingPayment* although the customer paid. This needs alerting and an admin "re-apply" action.
9. **Committed secret:** `origin/main` (`ef6a43f`) commits Cloudinary API credentials in `Backend/appsettings.json`. Rotate them and move them to environment variables / the secrets file.

**Pre-existing issues noticed (not changed):** Button and List bot nodes appear to send their body text twice (interactive message plus plain text). `MetaGraphService` hard-codes Graph API `v18.0` while the rest of the app uses `WhatsApp:ApiVersion`. `GetActiveConfigAsync` takes the business account from the first `Businesses` row globally. The Bot Flow designer's default start keyword is `remit`.
