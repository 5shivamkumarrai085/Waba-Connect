# Send Money over WhatsApp: the complete build guide

This document explains, step by step, how the Send Money feature was built in the WABA-Connect project, so you can understand it fully and build something like it again.

| Phase | What it covers |
|---|---|
| **[Phase 1 — Building the feature](#phase-1--building-the-feature)** | Design, database, services, providers, queue, APIs, tests |
| **[Phase 2 — Connecting Meta (WhatsApp Flows)](#phase-2--connecting-meta-whatsapp-flows)** | What a Flow is, the Flow JSON, encryption, the data endpoint, creating the Flow on Meta, ngrok, webhooks, testing with Meta |
| **[Phase 3 — APIs, payments, currency and the frontend](#phase-3--apis-payments-currency-and-the-frontend)** | Exchange-rate APIs, pricing, OTP, payment and payout handling, WhatsApp messages, every frontend page |
| **[Phase 4 — Everything that was created](#phase-4--everything-that-was-created)** | Inventory of files, tables, endpoints, settings |
| **[Phase 5 — Rebuilding it from scratch](#phase-5--rebuilding-it-from-scratch)** | A checklist to build another WhatsApp Flow feature |
| **[Phase 6 — Problems we hit and how they were solved](#phase-6--problems-we-hit-and-how-they-were-solved)** | Every real issue found during development and live testing |

Related documents: [SEND_MONEY_WALKTHROUGH.md](SEND_MONEY_WALKTHROUGH.md) (manager overview and endpoint reference) and [TESTING_GUIDE.md](TESTING_GUIDE.md) (hands-on testing and Postman).

---

## The big picture

```
                         ┌──────────────────────────── Meta (WhatsApp Cloud) ────────────────────────────┐
  Customer's phone ◄────►│  Messages API          Flows                     Webhooks                       │
  (WhatsApp app)         │  (send text/buttons)   (render screens on phone) (deliver inbound messages)     │
                         └──────┬───────────────────────┬───────────────────────────┬──────────────────────┘
                                │ Graph API calls        │ encrypted HTTPS POST      │ HTTPS POST (signed)
                                │ (we call Meta)         │ (Meta calls us)           │ (Meta calls us)
                                │                        ▼                           ▼
                                │            ngrok public HTTPS address  →  http://localhost:5155
                                │                        │                           │
   ┌────────────────────────────┴────────────────────────┴───────────────────────────┴──────────────────┐
   │ ASP.NET Core backend (WhatsAppCampaignApi)                                                          │
   │  /api/webhook/whatsapp-flows/{key}  ← Flow screens (data exchange)                                  │
   │  /api/webhook/whatsapp              ← inbound messages ("send money", Flow completion)              │
   │  /api/webhook/payments/{provider}   ← payment gateway callbacks                                     │
   │  /api/webhook/payouts/{provider}    ← payout partner callbacks                                      │
   │  /api/remittance/*, /api/connections/{id}/whatsapp-flows/*  ← admin API for the React app          │
   │  Services: sessions, OTP, quotes, beneficiaries, orders, notifier · Workers: queue + maintenance   │
   └───────────────┬──────────────────────────────────┬─────────────────────────────────────────────────┘
                   │ EF Core / Npgsql                  │ HTTPS
                   ▼                                   ▼
        PostgreSQL (Neon) tables + JobQueue     Exchange-rate APIs (open.er-api.com, Frankfurter)

   React frontend (Vite, port 5173) ──► admin API: Setup › Send Money, Transfers, Connect WABA card, Bot Flow node, Chat cards
```

**The customer journey in one paragraph.** The customer types *send money* in WhatsApp. Meta delivers that message to our webhook, and our bot engine starts the **Send Money bot flow**. Its **WhatsApp Flow node** creates a session (with a secret *flow token*) and asks Meta to send the customer a **Flow message**. When the customer taps it, WhatsApp opens a native form, and **for every screen Meta calls our encrypted data endpoint**. Our server checks the session, verifies the OTP, prices the transfer with a live exchange rate, validates the recipient, records the regulatory answers and finally **creates an order**. We then message the customer a **Pay** button to the payment provider's page. The order becomes **Paid** only when the provider's **signed callback** arrives. Then the payout is submitted, and the payout partner's signed callback marks it **Completed**. The customer gets a WhatsApp message at each step.

---

## Phase 1 — Building the feature

### 1.1 Starting point and decisions

The project already had WhatsApp messaging, a bot engine (Bot Flows), a Postgres job queue, encryption of secrets at rest, permissions, and a React admin app. There was **no** WhatsApp Flows code and nothing about money.

Decisions made before writing code:

| Decision | Why |
|---|---|
| **Server decides everything.** The Flow is only a form. | A customer, or someone faking requests, must not be able to skip the OTP, change the price or use another person's recipient. Every request is checked against the server's session record. |
| **No business data in the Flow JSON or the UI code.** | Countries, methods, fees, recipient fields, banks, lists and payment methods must be changeable without a release. All of it lives in the database and is sent to the Flow at runtime. |
| **Providers behind interfaces.** | Exchange rates, OTP, payment gateway and payout partner can be swapped, from mock to real, by adding one class. This follows the existing e-mail provider pattern. |
| **Two separate statuses on an order.** | *Order status* (business progress) and *payment status* (money) are different facts. Payment changes **only** on a verified provider callback. |
| **Nothing marks an order paid except a verified callback.** | No button (in WhatsApp or the sandbox page) can mark it paid directly. |
| **Mock providers for development, refused in Production.** | They let us test the whole journey now; a startup guard stops them reaching production. |
| **Malaysia as the demo market** (MYR → IDR, PHP, INR, BDT, SGD, VND) | It matched the reference screens. |

### 1.2 Database (15 new tables)

One EF Core migration, `20261012065738_AddSendMoneyAndWhatsAppFlows`, creates only new tables. It was inspected before applying to make sure it changed nothing else. Entities live in `Backend/Models/Entities/Remittance.cs` and `WhatsAppFlow.cs`; configuration is in `Backend/Data/AppDbContext.cs` (`ConfigureSendMoney`).

| Group | Table | Holds |
|---|---|---|
| **Configuration** | `RemittanceCorridors` | A route: source country/currency → destination country/currency, min/max amount, FX margin (basis points), receive-currency decimals |
| | `RemittancePayoutMethods` | How money reaches the recipient on a route (Bank Transfer, E-wallet, Cash Pickup), delivery time, payout partner |
| | `RemittanceFeeRules` | Fixed + percent fee, min/max, first-transfer fee, badge (e.g. PROMO) |
| | `RemittanceBeneficiaryFields` | The recipient form for a payout method: key, label (≤ 20 chars), type, required, min/max length, regex, help text, "is account number". Max **6 active**. |
| | `RemittanceInstitutions` | Banks / wallet providers per payout method |
| | `RemittanceLookups` | Pick-lists: `purpose`, `source_of_funds`, `relationship` |
| | `RemittancePaymentMethods` | How the customer pays (FPX, Pay at Counter): provider, badge, payment window |
| **Journey** | `RemittanceSessions` | One pass through the Flow: **hash** of the flow token, contact, phone, connection, status, OTP-verified time, chosen route/method/amount/quote/recipient, compliance answers, expiry (30 min) |
| | `OtpChallenges` | OTP **hash** only, expiry, attempts, resend count, lock |
| | `FxQuotes` | A priced, locked quote: amounts, market rate, customer rate, fee, provider, rate publication time, expiry (15 min) |
| | `RemittanceBeneficiaries` | Saved recipients; details **encrypted**, account shown masked (`****8234`) |
| **Money** | `TransferOrders` | The order: reference `IR-MY123456`, snapshot of everything confirmed, **OrderStatus** and **PaymentStatus**, provider payment id/URL/reference, payout reference, timestamps |
| | `TransferOrderEvents` | Append-only timeline; unique `(Provider, ProviderEventId)` so a duplicate callback is ignored |
| **WhatsApp Flows** | `WhatsAppFlowEncryptionKeys` | Per connection: RSA public key and **encrypted** private key |
| | `WhatsAppFlowRegistrations` | Per connection: Meta Flow id, status, endpoint key, endpoint URL, last sync/publish/ping, validation errors |

Rules applied: money `numeric(18,2)`, rates `numeric(18,8)`, enums stored as text, unique indexes to make operations idempotent (one order per session, one reference, one event per provider event id). Encrypted columns were added to the existing `EncryptionKeyGuard` check, and sensitive tables were excluded from audit-change capture so plaintext never lands in audit logs.

### 1.3 Provider interfaces

File: `Backend/Services/Remittance/Providers/ProviderContracts.cs`

```csharp
interface IExchangeRateProvider { string ProviderName; Task<ExchangeRateResult> GetRateAsync(from, to, ct); }   // Ok | Unsupported | Unavailable
interface IOtpProvider          { string ProviderName; bool DeliversCode; Task<OtpDeliveryResult> SendAsync(request, ct); }
interface IPaymentProvider      { string ProviderName; Task<PaymentSessionResult> CreatePaymentAsync(request, ct);
                                  PaymentWebhookParseResult ParseWebhook(headers, rawBody, now); }      // verifies signature
interface IPayoutProvider       { string ProviderName; Task<PayoutSubmitResult> SubmitAsync(request, ct);
                                  PayoutWebhookParseResult ParseWebhook(headers, rawBody, now); }
```

Implementations:
- **Real:** `ExchangeRateApiProvider`, `FrankfurterProvider` (`ExchangeRateProviders.cs`)
- **Mock:** `MockOtpProvider`, `MockPaymentProvider`, `MockPayoutProvider` (`MockProviders.cs`)

`RemittanceProviderRegistry` picks an implementation by name: from configuration (`Remittance:*:Provider`) or from the configuration row (a payment method's `Provider`, a payout method's `PayoutProvider`). Adapters report ordinary failures in their result instead of throwing.

### 1.4 Core services (`Backend/Services/Remittance/`)

| Service | Responsibility |
|---|---|
| `RemittanceSessionService` | Creates a session with a random 256-bit token `sm_…`, stores only its SHA-256, finds a session by token |
| `OtpService` | Generates, hashes, expires, rate-limits and verifies OTPs (details in 3.3) |
| `RemittancePricing` | Pure arithmetic: margin, fee, rounding, amount parsing, formatting |
| `ExchangeRateService` | Primary provider + fallbacks, cache, staleness check, "never invent a rate" (3.1) |
| `QuoteService` | Validates route/method/limits, gets the rate, applies margin and fee, detects first transfer, saves a 15-minute quote, supersedes older quotes |
| `BeneficiaryService` | Lists saved recipients, validates new ones against the configured fields (required, length, digits/phone/email, regex), encrypts and masks |
| `TransferOrderService` | The state machine: create order (idempotent), start payment, apply payment events, submit payout, apply payout events, expire unpaid orders |
| `RemittanceNotifier` | Sends WhatsApp messages about the order and records them in the Chat inbox and the order timeline |
| `SendMoneyFlowHandler` | The server side of the Flow: one method per screen action (Phase 2.6) |
| `RemittanceAdminService` | CRUD for all configuration with WhatsApp-limit validation; Transfers list/detail |
| `RemittanceDemoSeeder` | Malaysian demo data and the "Send Money" bot flow (created switched off) |
| `RemittanceStartupGuard` | Production refuses to start with mock providers or draft-Flow sending |

### 1.5 Queue and background workers

The project already had a Postgres job queue (`JobQueue` table). Two new queues were added in `Backend/Services/Queue/QueueNames.cs`:

| Queue | Job | Processed by |
|---|---|---|
| `remit-callback.v3` | A verified payment or payout callback | `RemittanceQueueWorker` → `TransferOrderService.ApplyPaymentEventAsync` / `ApplyPayoutEventAsync` |
| `remit-payout.v3` | "Submit this paid order to the payout partner" | `RemittanceQueueWorker` → `SubmitPayoutAsync` (transient failures retried) |

`RemittanceMaintenanceWorker` runs every 30 seconds:
1. **Expires** unpaid orders whose payment window passed.
2. **Sends the "Order created – Pay" message** for any order not yet announced after 45 s. This is the fallback when Meta's completion webhook doesn't reach us.
3. **Closes** abandoned sessions.

Why a queue for callbacks? The webhook endpoint verifies the signature and answers the provider at once (they retry slow endpoints). The real work happens in the worker, with retries and a dead-letter state. Idempotency keys (`payment:Mock:{eventId}`) stop a redelivered callback from being queued twice.

### 1.6 APIs

- **Admin API** (`Backend/Controllers/RemittanceController.cs`, requires login and permissions):
  - `api/remittance/...` for countries, payout methods, fields, banks, lists, payment methods, rate preview, Transfers
  - `api/connections/{id}/whatsapp-flows/...` for the encryption key, Flow sync and publish, and a test send
- **Machine endpoints** (`WhatsAppFlowsEndpointController.cs`, anonymous; each checks its own signature):
  - Meta's Flow data endpoint
  - payment and payout callbacks
- **Development harness** (`RemittanceDevController.cs`): compiled only in Debug builds and active only in Development with `Remittance:DevTools:Enabled`. It seeds demo data, starts sessions without WhatsApp, runs the Flow in plaintext, encrypts and decrypts like Meta, and hosts the mock checkout page and payment/payout simulators.
- **Permissions** (`PermissionCatalog.cs`): *Send Money* (`Remittance.View`, `Remittance.Manage`) and *Transfers* (`Transfer.View`).

The full endpoint table is in [SEND_MONEY_WALKTHROUGH.md §3](SEND_MONEY_WALKTHROUGH.md).

### 1.7 Configuration and secrets

`Backend/appsettings.json` → `Remittance` section holds the non-secret defaults (TTLs, attempts, provider names, cache, timeouts, Flow JSON version). **Secrets never go in it.** Webhook secrets, API keys and the public URL come from environment variables (`Remittance__…`) or the git-ignored `Backend/appsettings.secrets.local.json`. Development switches: `Remittance:Enabled`, `Remittance:DevTools:Enabled`, `Remittance:Otp:Mock:ExposeCodeInFlow`, `Remittance:Flows:AllowDraftSend`.

### 1.8 Tests

`Tests/Remittance.Tests` is a console integration suite against the real database with its own `zz-remittest` rows, which are cleaned up before and after a run. **173 checks in 6 phases:**
1. Flow encryption and endpoint protocol
2. OTP
3. Pricing and quotes
4. Full Flow journey
5. Payments and payouts
6. Live exchange-rate APIs

Run it with `dotnet run --project Tests/Remittance.Tests`, with the backend stopped. A Postman collection (35 requests) is in `docs/remittance/`.

---

## Phase 2 — Connecting Meta (WhatsApp Flows)

### 2.1 What a WhatsApp Flow is

A **WhatsApp Flow** is a multi-screen native form that opens inside WhatsApp. You describe the screens in a JSON document (the **Flow JSON**) and register it with Meta. A Flow can be:
- **static**: screens only, answers sent back at the end
- **endpoint-powered** (ours): **for each screen, WhatsApp calls your server**, and your server decides the next screen and its data

All traffic between Meta and your server is **end-to-end encrypted** with a key pair you create.

Two separate channels connect Meta to us. Don't mix them up:

| Channel | URL on our side | Configured where | Used for |
|---|---|---|---|
| **Messages webhook** | `/api/webhook/whatsapp` | Meta App → WhatsApp → Configuration → Callback URL + verify token | Inbound messages ("send money", button replies) and the **Flow completion** message (`nfm_reply`) |
| **Flow data endpoint** | `/api/webhook/whatsapp-flows/{endpointKey}` | Per Flow (`endpoint_uri`), set by our "Create/Sync Flow" | Every Flow screen request (INIT, button presses), Meta's health-check `ping` |

### 2.2 The Flow JSON (`Backend/Resources/WhatsAppFlows/send_money.flow.json`)

It's embedded in the backend assembly and uploaded to Meta by "Sync Flow". Key parts:

```json
{
  "version": "7.3",                 // Flow JSON version (supports If, dynamic properties)
  "data_api_version": "3.0",        // data-endpoint protocol version
  "routing_model": { "VERIFY_IDENTITY": ["TRANSFER_DETAILS"], "TRANSFER_DETAILS": ["BENEFICIARY"],
                     "BENEFICIARY": ["COMPLIANCE"], "COMPLIANCE": ["REVIEW_CONFIRM"], "REVIEW_CONFIRM": [] },
  "screens": [ … five screens … ]
}
```

| Screen | Shows | Button (payload `op`) |
|---|---|---|
| `VERIFY_IDENTITY` | Customer's number, OTP input, development hint, "Resend code" | `verify_otp` (with `otp`), link `resend_otp` |
| `TRANSFER_DETAILS` | Country dropdown, method dropdown (both trigger a server refresh), amount, and an `If` block that shows the quote | `select_country`, `select_method`, footer `quote_or_continue` |
| `BENEFICIARY` | Saved recipients (radio) or a new recipient: name, bank, **six generic text fields f1–f6**, relationship | `select_beneficiary`, footer `save_beneficiary` |
| `COMPLIANCE` | Source of funds, purpose, remarks | `save_compliance` |
| `REVIEW_CONFIRM` (terminal) | Full summary, payment methods with badges | `confirm` (with `payment_method`) |

**How data gets onto screens (data binding).** Every text, list and visibility flag is a placeholder such as `${data.rate_line}` or `${data.countries}`. Each screen declares its data fields with an `__example__` value; Meta's builder preview draws those examples (that's where the sample `+60123456789` comes from). At runtime our server fills them.

**How answers get to the server.** Buttons have `on-click-action: { "name": "data_exchange", "payload": { … } }`, and payload values like `${form.amount}` take what the customer typed.

**Why six "generic" recipient fields.** A Flow **cannot create input boxes at runtime**. So the screen has six text inputs whose **label, help text, required flag, max length and visibility** are all data-bound (`${data.f1_label}`, `${data.f1_visible}`, …). The server fills them from the payout method's configured fields. Bank Transfer to India might show "Account number" + "IFSC code"; an e-wallet shows only "Wallet mobile no.". This is why the admin screens refuse a seventh active field and labels over 20 characters (WhatsApp's limit).

**Meta rules the JSON respects:**
- no empty strings in bound text (we send `"-"` and hide the component)
- titles ≤ 30 characters
- dropdowns ≤ 200 options
- radio groups ≤ 20 options
- one Footer per screen
- the button field is called **`op`, not `action`** (see Phase 6)

### 2.3 Encryption: how a request is protected

Set-up (once per connection), in `WhatsAppFlowsService.EnsureKeyAsync`:
1. Generate an **RSA-2048** key pair (`FlowCrypto.GenerateKeyPair`).
2. Store the public key and the **encrypted** private key (`WhatsAppFlowEncryptionKeys`).
3. Upload the public key to Meta for each phone number: `POST /{phone-number-id}/whatsapp_business_encryption` with `business_public_key`. Meta **signs** it ("signature valid").

Every Flow request from Meta looks like this:

```json
{ "encrypted_flow_data": "<base64>", "encrypted_aes_key": "<base64>", "initial_vector": "<base64>" }
```

Our server (`FlowCrypto.cs`):
1. **Decrypts the AES key** with our RSA private key, using **RSA-OAEP with SHA-256**. That gives a random 128-bit AES key chosen by Meta for this request.
2. **Decrypts the body** with **AES-128-GCM** using that key and the 16-byte IV (the last 16 bytes are the GCM tag). Result: the JSON request.
3. Processes it and builds the JSON response.
4. **Encrypts the response** with the same AES key but the **IV with every bit flipped** (`~b`), using AES-GCM.
5. Returns it **Base64-encoded as `text/plain`**.

> .NET's built-in `AesGcm` only supports 12-byte nonces, while Meta uses **16-byte** IVs. So we use **BouncyCastle** (`GcmBlockCipher(new AesEngine())`), added as the `BouncyCastle.Cryptography` package.

The request is also signed with the app secret in **`X-Hub-Signature-256`** (HMAC-SHA256 of the raw body). We check it as the messages webhook does: required in Production, and a missing header is tolerated in Development.

**HTTP codes Meta understands:**

| Code | Meaning | When we return it |
|---|---|---|
| **200** | Encrypted screen response | Normal |
| **421** | "I couldn't decrypt" | Meta re-downloads our public key and retries |
| **427** | "Flow token invalid" | Session unknown, expired, finished or locked. Meta tells the user the Flow can't be used. |
| **432** | Signature check failed | Wrong `X-Hub-Signature-256` |
| **404** | Unknown endpoint key | Wrong URL |

Error codes are returned **with an empty body**; a body makes Meta report "Response could not be decrypted".

### 2.4 The data-exchange protocol (what goes back and forth)

Decrypted request examples:

```json
{ "version": "3.0", "action": "ping" }                                       // Meta health check
{ "version": "3.0", "action": "INIT", "flow_token": "sm_…" }                 // Flow opened (first screen)
{ "version": "3.0", "action": "data_exchange", "screen": "VERIFY_IDENTITY",
  "data": { "op": "verify_otp", "otp": "123456" }, "flow_token": "sm_…" }   // a button press
{ "version": "3.0", "action": "BACK", "screen": "BENEFICIARY", "flow_token": "sm_…" }
{ "version": "3.0", "action": "data_exchange", "flow_token": "sm_…",
  "data": { "error": "…", "error_message": "…" } }                           // Meta reporting a broken screen
```

Our responses:

```json
{ "data": { "status": "active" } }                                           // answer to ping
{ "screen": "TRANSFER_DETAILS", "data": { "countries": [ … ], "quote_ready": false, … } }  // next screen + its data
{ "screen": "VERIFY_IDENTITY", "data": { …, "error_message": "Incorrect code. 4 attempts left." } }  // snackbar
{ "screen": "SUCCESS", "data": { "extension_message_response": { "params":
      { "flow_token": "sm_…", "order_reference": "IR-MY503766" } } } }       // close the Flow
{ "data": { "acknowledged": true } }                                         // answer to an error notification
```

`WhatsAppFlowEndpoint.cs` handles ping and error notifications itself, then finds the **handler by the token prefix** (`sm_` → `SendMoneyFlowHandler`). Another Flow, e.g. KYC, would use its own prefix and handler.

### 2.5 The server-side handler (`SendMoneyFlowHandler.cs`)

For every request it first loads the session by token hash and refuses (427) if it's unknown, expired, cancelled, locked, finished or from another connection. Then:

| `op` / action | Server does | Next screen |
|---|---|---|
| `INIT` | Issues an OTP (mock: nothing delivered; code shown as a dev hint) | `VERIFY_IDENTITY` |
| `resend_otp` | New code if the 30 s cooldown passed and fewer than 3 sends | `VERIFY_IDENTITY` |
| `verify_otp` | Checks the hash, expiry and attempts; on success sets `OtpVerifiedAt` | `TRANSFER_DETAILS` |
| *anything else before verification* | **Refused**: "Please verify your number first." | `VERIFY_IDENTITY` |
| `select_country` / `select_method` | Reloads the methods for the country | `TRANSFER_DETAILS` |
| `quote_or_continue` | First tap: validates and prices, saves a 15-min quote, shows it. Second tap on an unchanged valid quote: continue. | `TRANSFER_DETAILS` → `BENEFICIARY` |
| `select_beneficiary` | Shows a saved recipient or the new-recipient form | `BENEFICIARY` |
| `save_beneficiary` | Validates against configured fields; saves encrypted; or checks the chosen saved recipient **belongs to this customer** | `COMPLIANCE` |
| `save_compliance` | Validates against the configured lists | `REVIEW_CONFIRM` |
| `confirm` | Re-checks everything. If the quote expired it re-prices and shows the new numbers. Creates the order (once per session), starts the payment. | `SUCCESS` (Flow closes) |

### 2.6 Creating the Flow on Meta (Graph API)

All done by `WhatsAppFlowsService` + `WhatsAppFlowsApiClient` (Graph version from `WhatsApp:ApiVersion`, `v21.0`), triggered by the buttons on **Connect WABA → WhatsApp Flows**:

| Step | Graph call | Notes |
|---|---|---|
| Upload key | `POST /{phone-number-id}/whatsapp_business_encryption` (`business_public_key`) | Once per number; check with GET |
| Create Flow | `POST /{waba-id}/flows` (`name`, `categories=["OTHER"]`, `flow_json`, `endpoint_uri`) | Returns the Flow id and Meta's validation errors |
| Update screens | `POST /{flow-id}/assets` (multipart file `flow.json`, `asset_type=FLOW_JSON`) | "Sync Flow" |
| Set endpoint / app | `POST /{flow-id}` (`endpoint_uri`, `application_id`) | The endpoint is `{App:PublicBaseUrl}/api/webhook/whatsapp-flows/{endpointKey}` |
| Read status | `GET /{flow-id}?fields=id,name,status,validation_errors,endpoint_uri,json_version` | Shown on the card |
| Publish | `POST /{flow-id}/publish` | Irreversible; changes later need a new version |

`endpointKey` is a random value per connection, so the URL isn't guessable. The sync **refuses** a public URL that is `localhost` or plain `http`, because Meta can't reach those.

### 2.7 Launching the Flow and finishing it

**Launch.** Bot Flow with a Start Trigger ("send money", "/sendmoney", "remit", …) → **WhatsApp Flow node** (`WhatsAppFlowNodeExecutor`) → `WhatsAppFlowLauncher` creates the session and sends:

```json
{ "messaging_product": "whatsapp", "to": "<customer>", "type": "interactive",
  "interactive": { "type": "flow",
    "header": { "type": "text", "text": "International Remittance" },
    "body": { "text": "Send money abroad in a few taps…" },
    "action": { "name": "flow", "parameters": {
        "flow_message_version": "3", "flow_token": "sm_…", "flow_id": "<id>",
        "flow_cta": "Click Here to Send Money", "flow_action": "data_exchange",
        "mode": "draft" } } } }      // "draft" only while unpublished and AllowDraftSend is on
```

If Meta refuses, the node sends a polite apology text through the bot engine instead.

**Finish.** When the Flow closes with `SUCCESS`, WhatsApp posts a message to the **messages webhook** with `interactive.type = "nfm_reply"` and our `params` in `response_json`. `WhatsAppCloudApiService` recognises it, logs a chat card and calls `WhatsAppFlowCompletionHandler`. That handler checks the token and order reference, then sends **"Order created – Pay"** exactly once, guarded by a conditional update on `CustomerNotifiedAt`. If the completion message never arrives (for example the webhook points elsewhere), the maintenance worker sends it after 45 s.

### 2.8 Making the backend reachable (ngrok) and connecting Meta

Meta must reach our server over **public HTTPS**:
1. `ngrok config add-authtoken <token>` (once), then `ngrok http 5155`. ngrok gives `https://xxxx.ngrok-free.dev`.
2. Put it in **`App:PublicBaseUrl`** (git-ignored secrets file) and **restart** the backend; it's read at startup.
3. **Messages webhook:** Meta App → WhatsApp → Configuration → Callback URL `https://xxxx.ngrok-free.dev/api/webhook/whatsapp` + verify token → *Verify and save*, and subscribe to **messages**. The verify token must match one saved for a connection, or the fallback `WhatsApp:VerifyToken`.
4. **Flow endpoint:** click **Create Flow / Sync Flow** on the card, which sets `endpoint_uri` on Meta automatically.
5. A free ngrok address **changes on every restart**: repeat steps 2–4 after each change.

### 2.9 Testing against Meta, in order

| Test | How | What it proves |
|---|---|---|
| Key | Card → *Create & Upload Key* → "signature valid" | Meta accepted our public key |
| Flow JSON | Card → *Create Flow* → 0 validation errors | Meta accepts our screens |
| **Health check** | WhatsApp Manager → Flows → Send Money → **Endpoint → Run health check** | Meta reaches us through ngrok, and encryption works both ways |
| **Preview with real data** | Flow Builder → **Preview Flow** → Flow token from `POST /api/dev/remittance/sessions` + **Request data** | The **whole journey** on Meta's real infrastructure, without delivering a message |
| Send to phone | Bot ("send money") or Flow Builder → ⋯ → **Send** | Delivery to a real phone (needs a trusted account; see 6.7) |

Verified on 12–13 Oct 2026: key ✅, Flow JSON ✅, health check ✅. **Preview: full journey ✅**: order IR-MY503766 created, then "Order created", "Payment received" and "Transfer completed" received on WhatsApp. Sending the Flow to a phone ❌ (account integrity block).

---

## Phase 3 — APIs, payments, currency and the frontend

### 3.1 Currency exchange APIs

| Provider | URL | Key needed | Coverage | Freshness |
|---|---|---|---|---|
| **ExchangeRate-API, open access** (default primary) | `https://open.er-api.com/v6/latest/{FROM}` → `rates[TO]` | No | All 6 demo currencies (IDR, PHP, INR, BDT, SGD, VND) | Updated daily |
| ExchangeRate-API, keyed | `https://v6.exchangerate-api.com/v6/{KEY}/pair/{FROM}/{TO}` → `conversion_rate` | `Remittance__ExchangeRates__ExchangeRateApi__ApiKey` | Same | Plan-dependent |
| **Frankfurter / ECB** (fallback) | `https://api.frankfurter.dev/v1/latest?base={FROM}&symbols={TO}` | No | No BDT or VND (reported as unsupported) | Weekdays |

`ExchangeRateService` tries the primary, then the fallbacks:
- **5 s timeout**, 1 retry on 5xx/429/timeouts
- rates cached **60 s**
- rates older than **26 h** refused (96 h for Frankfurter)

If none has a fresh rate, the customer sees "Exchange rates are temporarily unavailable". **No rate is ever invented.** The provider and its publication time are stored on every quote.

### 3.2 Pricing (`RemittancePricing.cs`)

```
customer rate   = market rate × (1 − margin_bps / 10,000)          (8 decimals, rounded down)
recipient gets  = send amount × customer rate                      (rounded DOWN to the currency's decimals: 0 for IDR/VND)
fee             = fixed + percent × amount, kept between min and max    (2 decimals)
                  OR the first-transfer fee (e.g. 0.00 "PROMO") if the customer has never had a successful payment
total payable   = send amount + fee
```

Example: 500 MYR, market 3,640 IDR, margin 150 bps → customer rate 3,585.4 → recipient gets IDR 1,792,700. The quote is locked for 15 minutes.

### 3.3 OTP (`OtpService.cs`)

- 6 digits from a cryptographic random generator.
- Stored only as **HMAC-SHA256** over `purpose \n session \n code`, keyed by a key **derived (HKDF)** from the app's encryption key.
- Valid **5 min**, **5 attempts** then locked, **30 s** resend cooldown, **3 sends** maximum.
- Constant-time comparison; attempts are counted with a conditional database update, so parallel guesses can't cheat.
- **Mock provider:** delivers nothing. In Development the code is shown on the screen ("Development only — your code is …").
- **Production:** implement `IOtpProvider` with a WhatsApp authentication template (copy-code button) or SMS.

### 3.4 Payment handling

```
confirm in Flow ─► TransferOrderService.CreateFromSessionAsync
                     ├─ order Created (unique per session) ─► IPaymentProvider.CreatePaymentAsync
                     └─ order AwaitingPayment, payment Pending, PaymentUrl / reference, window (15 min FPX, 24 h counter)
customer taps Pay ─► provider's hosted page (mock: /api/dev/mock-payments/{id}/checkout)
provider ─► POST /api/webhook/payments/{provider}  (raw body + signature header)
             └─ adapter.ParseWebhook verifies signature (HMAC-SHA256 "t=<unix>,v1=<hex>", 5-min replay window)
                 ├─ bad / missing signature → 401, nothing changes
                 └─ OK → JobQueue remit-callback.v3 (idempotency key payment:{provider}:{eventId}) → 200
RemittanceQueueWorker ─► ApplyPaymentEventAsync
                     ├─ record event id first (duplicate → ignored)
                     ├─ amount/currency must match the order (else recorded for review, not applied)
                     ├─ Succeeded → payment Succeeded, order Paid → queue payout → WhatsApp "Payment received"
                     ├─ Failed / Expired → order Failed / Expired (never downgrades a success)
                     └─ Refunded → payment Refunded, order Cancelled
```

**The mock gateway** is a sandbox page served by the dev harness. Its buttons build the gateway's callback, **sign it** with `Remittance:Payments:Mock:WebhookSecret`, and push it through the **same** verification and queue as a real gateway. To add a real gateway (an FPX acquirer, Stripe, Billplz, …), implement `IPaymentProvider`, register it, and select it on the payment method in Setup. Its callback URL is `/api/webhook/payments/{ProviderName}`.

### 3.5 Payout handling

Paid order → `remit-payout.v3` job → `IPayoutProvider.SubmitAsync` → order **Processing** with a payout reference. The partner's signed callback at `/api/webhook/payouts/{provider}` → **Completed** (WhatsApp "Transfer completed") or **Failed**. The mock partner accepts everything; completion is simulated with `/api/dev/mock-payouts/{ref}/simulate`.

### 3.6 Order and payment statuses

| Order status | Meaning |
|---|---|
| Created → **AwaitingPayment** | Confirmed, payment started |
| **Paid** | Verified payment callback received |
| **Processing** | Sent to the payout partner |
| **Completed** | Partner confirmed delivery |
| Expired / Failed / Cancelled | Window passed / payment or payout failed / refunded |

Payment status is separate: Pending → Succeeded / Failed / Expired → Refunded.

Every change is a **conditional UPDATE** ("only if currently X") plus an event row, so duplicates and out-of-order callbacks can't corrupt state.

### 3.7 WhatsApp messages to the customer (`RemittanceNotifier.cs`)

| When | Message type |
|---|---|
| Order created | `interactive.type = "cta_url"`: summary + **"Pay MYR 2,000.00"** button to the payment page (or a counter reference) |
| Payment received / failed / expired / refunded, transfer completed / failed | Text |

Each message is also written to the **Chat inbox** as a card (`[Transfer|Ref: …|Status: …]`) and to the order timeline.

These are normal free-form messages, allowed within 24 h of the customer's last message. Updates days later need approved **utility templates** (a production gap).

### 3.8 The frontend (React + Vite)

| Area | Files | What it does |
|---|---|---|
| **Setup › Send Money** | `Frontend/src/pages/Setup/SendMoney/` (`SendMoneySetup`, `CountriesTab`, `PayoutMethodsView`, `MethodDetailsView`, `ListsTab`, `PaymentMethodsTab`, `ConfigTable`, `SendMoney.css`) | Three tabs. **Countries & payout** drills down Countries → Payout methods (with fees) → Recipient fields (a **6-slot preview**, live **20-character** label counter) and Banks; "Check live rate". **Regulatory lists**: purpose, source of funds, relationship. **Payment methods**: FPX, counter, badges, payment windows. |
| **Transfers** | `Frontend/src/pages/Transfers/TransfersList.tsx` | Sidebar (Support). Paged list with **two status badges**, search, filters, and a detail drawer: amounts, masked account, payment/payout references, **timeline**. |
| **WhatsApp Flows card** | `Frontend/src/pages/ConnectWABA/WhatsAppFlowsCard.tsx` | On Connect WABA, per connection: public-address check, **Create & Upload Key**, **Create/Sync Flow**, Meta validation errors, data-endpoint URL and last health check, **Publish**, "Send the Flow to a test number". |
| **Bot Flow node** | `Frontend/src/pages/BotFlow/BotFlowDesigner.tsx` | New palette item **"WhatsApp Flow"**: choose the Flow (from the server's list), header, body, footer, button text (30-character limit), with a live preview. Saved as node type "WhatsApp Flow". |
| **Chat cards** | `Frontend/src/pages/Chat/Chat.tsx` | `[FlowSent|…]`, `[FlowReply|…]` and `[Transfer|…]` messages render as cards |
| **Services** | `services/remittance/remittanceService.ts`, `services/waba/whatsAppFlowsService.ts` | Typed API calls, status-badge mapping, money and rate formatting |
| **Navigation** | `pages/Setup/setupNav.ts`, `App.tsx`, `components/Sidebar.tsx`, `utils/iconRegistry.ts` | Routes `/setup/send-money` and `/transfers`, permission-gated |

No business option is hard-coded in the UI; every list comes from the API. Check the frontend with `npx tsc -b` and `npm run build`.

---

## Phase 4 — Everything that was created

**Backend:**
- **Entities and enums:** `Models/Entities/Remittance.cs`, `Models/Entities/WhatsAppFlow.cs`, additions to `Models/Enums/Enums.cs`
- **Options:** `Models/Options/RemittanceOptions.cs`
- **Services:** `Services/Remittance/` (SessionService, OtpService, RemittancePricing, ExchangeRateService, QuoteService, BeneficiaryService, TransferOrderService, RemittanceNotifier, RemittanceWorkers, SendMoneyFlowHandler, RemittanceAdminService, RemittanceDemoSeeder, RemittanceStartupGuard)
- **Providers:** `Services/Remittance/Providers/` (ProviderContracts, ExchangeRateProviders, MockProviders, ProviderRegistry)
- **WhatsApp Flows:** `Services/WhatsApp/Flows/` (FlowCrypto, FlowEndpointContracts, WhatsAppFlowEndpoint, WhatsAppFlowsApiClient, WhatsAppFlowsService, WhatsAppFlowLauncher)
- **Flow JSON:** `Resources/WhatsAppFlows/send_money.flow.json`
- **Bot node:** `Executors/WhatsAppFlowNodeExecutor.cs`
- **Controllers:** `RemittanceController.cs` (+ `WhatsAppFlowsController`), `WhatsAppFlowsEndpointController.cs`, `RemittanceDevController.cs`
- **Migration:** `Migrations/20261012065738_AddSendMoneyAndWhatsAppFlows.cs`
- **Edited:** `Program.cs`, `AppDbContext.cs`, `AuditChangeCapture.cs`, `EncryptionKeyGuard.cs`, `PermissionCatalog.cs`, `QueueNames.cs`, `WebhookDtos.cs` (`nfm_reply`), `WhatsAppCloudApiService.cs`, `appsettings.json`, `.env.example`, `WhatsAppCampaignApi.csproj` (BouncyCastle, embedded Flow JSON)
- **Bot-engine fix:** `FlowExecutionService.cs`, `NodeExecutionResult.cs`, Button/List/Location/ContactCard executors (no more duplicate "Please choose an option")

**Frontend:** see 3.8. Also `Chat.css`, `ConnectWABA.css`, `BotFlowDesigner.css`, and the `setup.css` narrow-screen fix.

**Tests and docs:**
- `Tests/Remittance.Tests/` (Program, TestRun, RemittanceHarness, Journey, Phase1–6)
- `docs/remittance/`: this guide, `SEND_MONEY_WALKTHROUGH.md`, `TESTING_GUIDE.md`, Postman collection and environment

**Database:**
- 15 tables (1.2)
- 3 permissions
- demo data (6 routes, 10 payout methods, banks, lists, 2 payment methods)
- the "Send Money" bot flow
- on the shared development database only, a narrowed `trg_prevent_stale_worker` trigger (see 6.6)

---

## Phase 5 — Rebuilding it from scratch

Use this checklist to build Send Money again, or another endpoint-powered WhatsApp Flow (KYC, bookings, loan applications):

1. **Design the screens on paper.** List each screen, what it shows, and what each button sends. Keep business data out of the layout.
2. **Model the data.** Configuration tables (what the business edits), journey tables (session + token hash), result tables (orders + append-only events). Add unique indexes for idempotency.
3. **Write the Flow JSON.** Use `version` 7.x and `data_api_version` 3.0, a `routing_model`, `${data.*}` bindings with `__example__` values, and `data_exchange` actions whose payload field is **`op`** (not `action`). Mark the last screen `terminal`. Validate it in WhatsApp Manager's Flow Builder.
4. **Write the crypto.** RSA-OAEP-SHA256 to unwrap the AES key; AES-GCM with the **16-byte IV** (BouncyCastle in .NET); flipped-IV response; Base64 `text/plain`.
5. **Write the endpoint.**
   - Check the `X-Hub-Signature-256` signature, then decrypt (421 on failure).
   - Answer `ping` and error notifications.
   - Route by token prefix to a handler; return **bodiless** error codes (427 for a dead token).
6. **Write the handler.** One method per `op`, always checking the server-side session state first.
7. **Register with Meta.**
   - Generate the key pair, then `POST /{phone-number-id}/whatsapp_business_encryption`.
   - Create the Flow with `POST /{waba-id}/flows`, then upload `flow.json` and set `endpoint_uri` on the Flow.
   - Run the **health check**.
8. **Launch it.** Send an `interactive.type = "flow"` message with your `flow_token`; handle the `nfm_reply` completion on the messages webhook.
9. **Expose it.** Use a public HTTPS URL (ngrok in development), `App:PublicBaseUrl`, and the Meta webhook URL plus verify token.
10. **Test in layers.** Unit and integration tests → plaintext harness → encrypted harness → Meta health check → **Flow Builder preview with a real token** → send to a phone (needs a trusted, ideally verified business account).
11. **Before production.**
    - Use real providers, and complete business verification on a real registered number.
    - Publish the Flow.
    - Use templates for messages sent outside the 24-hour window.
    - Add monitoring for dead-lettered jobs.

---

## Phase 6 — Problems we hit and how they were solved

### 6.1 Meta strips a payload field named `action`
**Symptom:** in Meta's preview, *Verify & continue* answered "Please verify your number first."
**Cause:** the request arrived as `data: { "otp": "…" }`. WhatsApp removed our `"action": "verify_otp"` because it clashes with the request's own `action` (`data_exchange`). Found by logging each request's field names.
**Fix:** renamed the payload field to **`op`** in all 9 buttons. The server reads `op` (and still accepts `action`); the Flow was resynced to Meta.

### 6.2 "Response could not be decrypted" on a 427
**Cause:** ASP.NET's `[ApiController]` attached a JSON "problem details" body to the 427, and Meta tried to decrypt it.
**Fix:** error codes are returned as a bare status with an empty body.

### 6.3 The preview showed `+60123456789`
That's the `__example__` value the builder uses when it doesn't call the server. With **Request data** and a real flow token, the server sends the customer's own number.

### 6.4 500 on the beneficiary step (Development only)
**Cause:** a query loaded two collections (recipient fields + banks), which the app refuses in Development unless `.AsSplitQuery()` is used.
**Fix:** added `.AsSplitQuery()`, and the test harness now uses the same strict setting so tests catch this.

### 6.5 "Please choose an option" sent twice by the bot
**Cause:** Button/List/Location/Contact Card executors sent their interactive message, and the bot engine sent the body text again to record it in Chat.
**Fix:** executors return the send result (`SentByExecutor`) and the engine only records it. Recording the WhatsApp message id also stops the "No stored message has WhatsApp id …" webhook failures. (Applied and built on 13 Oct 2026; awaiting confirmation on a phone by sending "hi" to the bank bot.)

### 6.6 "Worker ASHOK-… has a stale local build" (shared development database)
**Cause:** the team's database has a trigger blocking this PC's queue workers. Every build shares queue contract `v3`; commit `ef6a43f` (Cloudinary media) changed job behaviour without bumping `QueueNames.ContractVersion`, and an out-of-date PC could break shared campaign jobs.
**Fix:** synced the PC with `origin/main`. The trigger was **narrowed** so this PC may process only the `remit-*` (Send Money) and `wa-webhook.*` (inbound messages) queues; campaign and e-mail queues stay blocked. Lesson: **bump `ContractVersion` whenever a job's meaning changes.**

### 6.7 Error 139000 "Blocked by Integrity — Integrity requirements not met"
**Symptom:** sending the Flow to a phone fails (HTTP 400), both from our API and from Meta's own Flow Builder **Send** button.
**Cause (per Meta Support):** the WhatsApp Business Account has **"low trust"** status, and **business verification is NOT_STARTED**. Normal messages are unaffected; only Flow messages are blocked.
**Why the preview works:** the preview runs inside Meta's builder and delivers no message, so the trust check doesn't apply. It still calls our endpoint for every screen.
**Fix (outside the code):** complete business verification, or use an established, verified WhatsApp Business Account with a real number. Nothing in our code changes.

### 6.8 Other gotchas
- **ngrok's free plan** shows a warning page to browsers (tap **Visit Site**). Meta's servers aren't affected.
- The address **changes on every restart**: update `App:PublicBaseUrl`, resync the Flow and update the Meta webhook URL.
- **Meta test numbers** can only message numbers on the app's test recipient list.
- **Don't click Save in Meta's Flow Builder** after we've synced the Flow; it uploads the builder's text and can overwrite the latest version. Reload the page first.
- **Only one flow token per journey.** After the order is created, or after 5 wrong codes, the token is finished (427). Create a new session for each test (`POST /api/dev/remittance/sessions`, 30 minutes).

### 6.9 "send money" sometimes got the wrong menu, or no reply at all (audit, 14 Oct 2026)

On a phone (Connection RMA), "send money" sometimes returned the "hi" menu. After STOP and START,
"send money" got no reply at all. Four separate causes:

1. **Two builds were answering the same customers.**
   - The hosted server (`srv-…`) runs older code with no Send Money, and it took about half of the
     incoming messages from the shared `wa-webhook.v3` queue. When it took "send money", it dropped
     the flow silently.
   - **Fix:** the inbound queue has its own contract version (`QueueNames.InboundContractVersion`,
     now **v4**), so only this build handles messages that reach this build's webhook.
   - **Rule:** bump the inbound version whenever the handling of an incoming message changes meaning.
2. **A keyword typed while a menu was waiting counted as a wrong answer to that menu.**
   - **Fix:** an *exact* trigger keyword of any active flow now interrupts the waiting menu and starts
     that flow. A genuine option of the menu on screen still wins.
3. **The menu's second wrong answer ended the conversation silently.**
   - **Fix:** the message is offered to the other triggers. If nothing matches, the customer gets a
     short closing line (`BotFlow:MenuGiveUpReply`).
4. **STOP/START never closed the open menu**, so after START the old menu swallowed the next message.
   - **Fix:** STOP and START close every open bot and assistant session, and START also lifts a
     Stop Bot suppression.
   - **Policy:** STOP blocks marketing and campaigns only. A customer who writes in afterwards is
     still answered (Meta allows customer-initiated replies).

**Other routing rules:**
- **Matching:** keywords are compared without case, extra spaces or trailing punctuation, and may
  be saved as a list or as comma-separated text.
- **Priority:** exact beats "contains" and "starts with", then a flow on this connection beats a
  global one, then the oldest flow wins.
- **Session length:** sessions end after 30 minutes idle or 2 hours in total (`BotFlow:MaxSessionMinutes`),
  or as soon as their flow is switched off.

**How incoming messages are processed now:**
- The bot reply runs inside the queue job, not as a detached task, so a crash retries it instead
  of losing it.
- Each customer's messages are claimed one at a time, in order, across all servers (the ordered
  claim on the partition `wa:{phone_number_id}:{wa_id}`).
- Delivery statuses for messages this app never sent are retried for 3 minutes, then dropped
  instead of being dead-lettered.

**Saving a flow** in the designer no longer unbinds it from its connection or switches it on. An
active flow whose Start Trigger has no keywords is refused.

### 6.11 Replies took 10–15 seconds (14 Oct 2026)

**Measured cause:** database round trips, not the queue or Meta.
- From this PC, one query to the Neon database takes about **280 ms**.
- One incoming message made about **45 queries, one after another**.
- 45 × 280 ms ≈ 12.6 s, which matches the delay we saw.

**Fix:**
- **Cache read-mostly configuration** for 60 seconds (`HotPathCache`): active bot flows and their
  steps, Message/Template Bots, a connection's send credentials and numbers, the Send Money
  catalogue and Flow registration. A save clears the matching part at once
  (`HotPathCacheInvalidator`, an EF save interceptor); other servers catch up within a minute.
- **Fewer writes:** bot log rows are written with the turn, and the contact, conversation and
  number are resolved once per turn.
- **Reply first:** the customer's reply is sent before the inbox bookkeeping (SLA, routing, live
  update).
- **No poll wait:** the webhook endpoint wakes the inbound worker immediately (`QueueSignal`).

**Watch:** every message logs `Inbound webhook job …: waited X ms in the queue, processed in Y ms`
and `Inbound message …: stored in …, automated reply done at …`.

**The real fix for production:** run the backend in the **same region as the database**. Round
trips then drop from ~280 ms to ~1–2 ms, and a reply takes well under a second.

### 6.10 Money safety and production guard (audit, 14 Oct 2026)
- **Paying twice is impossible:**
  - a payout is claimed (`Paid → Submitting`) before the provider is called;
  - providers must de-duplicate on the order reference (the mock does);
  - a submission that never reports back goes to `NeedsReview` for a person.
- **Callbacks are all or nothing:** a callback's record and the status changes it causes commit
  together, and a paid order whose payout job was lost is queued again by the reconciliation sweep.
- **Early or mis-cased callbacks:** a callback for an order not saved yet is retried for 10 minutes,
  and provider names are normalised.
- **Payment start failures:**
  - the quote stays usable, so Confirm can simply be tapped again;
  - a renewed quote re-prices the existing order instead of charging the old price;
  - orders whose payment never started expire after 30 minutes.
- **One open form per customer:**
  - starting Send Money cancels the customer's older sessions (5 launches per 10 minutes);
  - OTP codes are limited per phone number across sessions (5/hour, 10/day; generous in Development);
  - two simultaneous opening requests send only one code.
- **Exchange rates:**
  - rates customers use are refreshed in the background;
  - a request waits at most 3 seconds for a provider;
  - the last good rate may be served for up to 5 minutes during an outage.
- **Endpoint failures:** an unexpected error on the Flow endpoint answers an empty 500 (WhatsApp shows
  its own retry message) instead of an unreadable body. The signature is checked before any
  database work.
- **References** are `IR-{CC}{yyMMdd}{8 random}` (19 characters), so they don't run out.
- **Mock providers** exist only in Development. Any other environment refuses to start with Send
  Money on while an active payment or payout method uses a mock, while the OTP provider is the mock,
  or while draft sends or dev tools are on.

---

## Appendix — key settings

| Setting | Purpose |
|---|---|
| `App:PublicBaseUrl` | Public HTTPS address (ngrok) used for the Flow endpoint and Pay links |
| `Remittance:Enabled` | Master switch |
| `Remittance:DevTools:Enabled` | Development harness endpoints |
| `Remittance:Otp:Mock:ExposeCodeInFlow` | Show the mock OTP on screen (development only) |
| `Remittance:Flows:AllowDraftSend` / `JsonVersion` | Send the Flow before publishing / Flow JSON version (7.3) |
| `Remittance:ExchangeRates:Provider` / `Fallbacks` / `CacheSeconds` / `TimeoutSeconds` / `MaxRateAgeHours` | Rate sources and freshness |
| `Remittance:ExchangeRates:ExchangeRateApi:ApiKey` | Optional keyed rates (secret) |
| `Remittance:Payments:Mock:WebhookSecret`, `Remittance:Payouts:Mock:WebhookSecret` | Sign/verify mock callbacks (secret) |
| `Remittance:SessionTtlMinutes` (30), `QuoteTtlMinutes` (15), `Otp:*` | Timings and limits |
| `WhatsApp:ApiVersion` | Graph API version (v21.0) |
| `WhatsApp:VerifyToken` | Fallback webhook verify token (secret) |
| `WhatsApp:Webhook:Concurrency` (4) | Inbound processing loops per instance |
| `BotFlow:SessionTimeoutMinutes` (30) / `MaxSessionMinutes` (120) / `MenuGiveUpReply` | Bot session limits and the closing line |
| `Remittance:MaxLaunchesPerTenMinutes` (5), `Otp:MaxPerDestinationPerHour` (5) / `PerDay` (10) | Abuse limits (raised in Development) |
| `Remittance:ExchangeRates:RequestBudgetSeconds` (3) / `ServeStaleSeconds` (300) | Rate wait budget and outage fallback |
| `Workers:{WorkerName}:Enabled` / `Workers:Enabled` | Switch background workers off (API-only instances) |

All secrets live only in environment variables or the git-ignored `Backend/appsettings.secrets.local.json`.
