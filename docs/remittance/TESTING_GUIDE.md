# Send Money — complete hands-on guide

This guide has four parts:

1. **[What was built, and how](#part-1--what-was-built-and-how)** — every piece, in the order it was built, with the decisions behind it.
2. **[One-time setup](#part-2--one-time-setup)** — what must be in place before you test.
3. **[Testing it yourself](#part-3--testing-it-yourself)** — five ways, from "one command" to "on a real phone", each step with what you should see.
4. **[Troubleshooting](#part-4--troubleshooting)** — every problem met while building this, and its fix.

For architecture, the full endpoint reference and production gaps, see [SEND_MONEY_WALKTHROUGH.md](SEND_MONEY_WALKTHROUGH.md).

> **Real vs mock.** Exchange rates are **real** (ExchangeRate-API, Frankfurter fallback). OTP delivery, the payment gateway and the payout partner are **development mocks**: they move no money and deliver no codes. The mock gateway still signs its callbacks, and they go through the same verification a real gateway's would.

---

## Part 1 — What was built, and how

### 1.1 The goal

A customer types *send money* in WhatsApp and gets a native WhatsApp **Flow** (an in-chat form). They:

1. Verify their number (OTP).
2. Pick a country, delivery method and amount, and see a guaranteed rate and fee.
3. Choose or add a recipient.
4. Answer the regulatory questions.
5. Confirm with a payment method.

The order is created, the customer pays on the payment provider's page (never inside WhatsApp), and only the provider's **signed callback** marks the order paid. A payout partner then delivers the money. All business data (countries, methods, fees, recipient fields, banks, lists, payment methods) is configurable in the app, not hard-coded.

### 1.2 Build steps, in order

| # | Step | What was done | Main files |
|---|---|---|---|
| 1 | **Data model** | 15 new tables: configuration (countries, payout methods, fees, recipient fields, banks, lists, payment methods), the journey (sessions, OTP challenges, quotes, beneficiaries), money (orders, append-only order events) and WhatsApp Flows (per-connection encryption key, Flow registration). Money as `numeric(18,2)`, rates `numeric(18,8)`. Beneficiary details and the Flow private key are encrypted at rest. One migration, inspected before applying: only `CREATE TABLE`s. | `Backend/Models/Entities/Remittance.cs`, `WhatsAppFlow.cs`, `Data/AppDbContext.cs`, `Migrations/20261012065738_AddSendMoneyAndWhatsAppFlows.cs` |
| 2 | **Provider interfaces** | `IExchangeRateProvider`, `IOtpProvider`, `IPaymentProvider`, `IPayoutProvider`, chosen by name from configuration (same pattern as the e-mail providers). Real: ExchangeRate-API and Frankfurter. Mock: OTP, payment gateway, payout partner. | `Backend/Services/Remittance/Providers/*` |
| 3 | **Core services** | OTP (hashed, expiring, attempt- and resend-limited), pricing (margin, fee rules, rounding down what the recipient gets), quotes (15-minute lock, fallback provider, stale-rate refusal), beneficiaries (validation against configured fields), orders (state machine with separate order and payment status, idempotent confirm, event log), notifier (WhatsApp messages and chat cards). | `Backend/Services/Remittance/*.cs` |
| 4 | **Queue and workers** | Two new queues, `remit-callback.v3` and `remit-payout.v3`, on the existing Postgres job queue. `RemittanceQueueWorker` applies verified callbacks and submits payouts. `RemittanceMaintenanceWorker` expires unpaid orders, sends any missed "order created" message and closes abandoned sessions. | `RemittanceWorkers.cs`, `Services/Queue/QueueNames.cs` |
| 5 | **WhatsApp Flows** | Meta's encryption (RSA-OAEP + AES-GCM with a 16-byte IV, via BouncyCastle because .NET's AES-GCM only takes 12-byte nonces). The encrypted data-exchange endpoint (ping, INIT, BACK, data_exchange, error codes 421/427/432). A Graph API client to create, upload, publish and upload keys. A Flow JSON with 5 screens holding layout only (all data is bound at runtime, with 6 generic text slots for recipient fields). The launcher and completion handler (`nfm_reply`). | `Backend/Services/WhatsApp/Flows/*`, `Resources/WhatsAppFlows/send_money.flow.json`, `Services/Remittance/SendMoneyFlowHandler.cs` |
| 6 | **Bot integration** | A new "WhatsApp Flow" bot node that sends the Flow invitation. Inbound `nfm_reply` messages are recognised and logged as chat cards. | `Backend/Executors/WhatsAppFlowNodeExecutor.cs`, `Services/WhatsAppCloudApiService.cs` |
| 7 | **APIs** | Admin CRUD with validation against WhatsApp's limits (label ≤ 20, 6 field slots, list ≤ 200…), the Transfers list and detail, per-connection Flow set-up, machine endpoints (Flow, payment and payout callbacks), and development-only harness endpoints. Two permission groups: *Send Money* and *Transfers*. | `Controllers/RemittanceController.cs`, `WhatsAppFlowsEndpointController.cs`, `RemittanceDevController.cs`, `Data/Seed/PermissionCatalog.cs` |
| 8 | **Safety rails** | Production refuses to start with mock providers. Dev endpoints are compiled out of Release builds and refuse outside Development. Secrets come only from environment variables or the git-ignored secrets file. | `RemittanceStartupGuard.cs`, `appsettings.json`, `.env.example` |
| 9 | **Demo data** | An idempotent Malaysian demo: MYR → IDR, PHP, INR, BDT, SGD, VND; FPX and Pay-at-Counter. The "Send Money" bot flow is created **switched off**. | `RemittanceDemoSeeder.cs` |
| 10 | **Frontend** | *Setup › Send Money* (countries → payout methods → recipient fields and banks, lists, payment methods, live-rate check). *Transfers* page (order and payment status, timeline). *WhatsApp Flows* card on Connect WABA. The Bot Flow node. Chat cards. | `Frontend/src/pages/Setup/SendMoney/*`, `pages/Transfers/TransfersList.tsx`, `pages/ConnectWABA/WhatsAppFlowsCard.tsx`, `pages/BotFlow/BotFlowDesigner.tsx`, `pages/Chat/Chat.tsx` |
| 11 | **Tests** | Integration suite, 6 phases, 173 checks, against the real database with its own `zz-remittest` rows that clean themselves up. Postman collection with 35 requests. | `Tests/Remittance.Tests/*`, `docs/remittance/*.postman_*.json` |
| 12 | **Shared-database fix** | Root-caused the `trg_prevent_stale_worker` rejection, synced your PC with `origin/main`, and (with your approval) narrowed the guard to exempt only the Send Money queues. | Database trigger (see the walkthrough, §6.4) |
| 13 | **Live verification** | Ran a full order on the real backend with every worker. That found and fixed two bugs: (a) the recipient step crashed in Development because one query loaded two collections without `.AsSplitQuery()`, and the test harness now enforces the same rule; (b) the dev session endpoint accepted a deleted contact, and now answers 409. | `SendMoneyFlowHandler.cs`, `BeneficiaryService.cs`, `RemittanceDevController.cs`, `Tests/Remittance.Tests/RemittanceHarness.cs` |

### 1.3 Key decisions

* **Server decides everything.** The Flow is only a form. The server checks the session row on every request, so a crafted request cannot skip the OTP, change the price or use another customer's recipient.
* **Six recipient-field slots.** A WhatsApp Flow cannot add input boxes at runtime, so the Flow has six generic inputs. The server fills in their labels and rules from the payout method's configuration.
* **Two statuses.** *Order status* and *payment status* are separate, and payment status changes only on a verified callback or expiry.
* **Nothing marks an order paid but a verified callback**: not a button in WhatsApp, not the mock checkout page.

---

## Part 2 — One-time setup

These are already done on your PC. Check them if you set up another machine.

**1. Code** is up to date with `origin/main`, with the Send Money work on top.

**2. Database migration** is applied. On a new database run:

```bash
dotnet ef database update --project Backend
```

**3. Development settings.** In the git-ignored `Backend/appsettings.secrets.local.json` (or as environment variables, using `__` instead of `:`), under `"Remittance"`:

| Key | Value | Why |
|---|---|---|
| `Enabled` | `true` | Master switch |
| `DevTools:Enabled` | `true` | Enables the `/api/dev/...` test endpoints |
| `Otp:Mock:ExposeCodeInFlow` | `true` | Shows the mock OTP on screen (nothing is really sent) |
| `Flows:AllowDraftSend` | `true` | Lets an unpublished Flow be sent to test numbers |
| `Payments:Mock:WebhookSecret`, `Payouts:Mock:WebhookSecret` | long random strings | Sign and verify the mock callbacks |

Optional: `ExchangeRates:ExchangeRateApi:ApiKey` (without it the free daily endpoint is used).

**4. Admin login** for Postman and the app: `admin@admin.com` with the password set in `Auth:SuperAdmin:Password` in your secrets file (or your own account with *Send Money* and *Transfers* permissions).

**5. Shared-database guard.** Already narrowed, so your PC processes the Send Money queues. Your PC is still refused the team's campaign queues. The backend log shows `Worker ASHOK-… has a stale local build` every few seconds; **that is expected, not a fault.**

**6. Test phone numbers.** Use numbers from **+44 7700 900000–900999**. That UK range is reserved for fiction and belongs to no one, which matters because the app really tries to send WhatsApp notifications to the test contact. Don't use `+44 7700 900123`: it belongs to a deleted contact.

---

## Part 3 — Testing it yourself

Start with test 1, then 2. Tests 3–4 show the screens. Test 5 needs Meta set-up.

### Test 1 — Automated suite (5 minutes, one command)

Stop the backend first if it is running (it locks the build output).

```bash
dotnet run --project Tests/Remittance.Tests
```

**Expected:** six phases, ending with

```
  173 passed, 0 failed
```

Run a single phase by adding its number, e.g. `dotnet run --project Tests/Remittance.Tests -- 4`. Phase 6 calls the real exchange-rate APIs and is skipped (not failed) when offline.

### Test 2 — Full journey in Postman (15 minutes)

**2.1 Start the backend**

```bash
dotnet run --project Backend --launch-profile http
```

Wait for `Now listening on: http://localhost:5155`. `http://localhost:5155/health` should answer (status "Degraded" is normal on the shared database; see Part 4).

**2.2 Import into Postman** (File › Import):

* `docs/remittance/SendMoney.postman_collection.json`
* `docs/remittance/SendMoney.local.postman_environment.json`

Select the environment **"WABA Connect — Send Money (local)"** and fill in `adminEmail` and `adminPassword`. Leave the other variables empty; the requests fill them in.

**2.3 Run the requests in order.** Click each one and press Send, or use *Run collection*. Each request has tests that check the result and pass ids on to the next.

| # | Request | Method & URL | What you should see |
|---|---|---|---|
| **0** | Login | `POST /api/auth/login` | `data.token` → saved as `token` |
| **1** | Seed Malaysian demo data | `POST /api/dev/remittance/seed-demo` | "Demo Send Money configuration is in place." (zeros in `summary` if it already exists) |
| 1 | Reference | `GET /api/remittance/reference` | providers `Mock`, field types, lists, `enabled: true` |
| 1 | Countries | `GET /api/remittance/corridors` | 6 routes; Indonesia saved as `corridorId` |
| 1 | Payout methods | `GET /api/remittance/corridors/{{corridorId}}/payout-methods` | Bank Transfer and E-wallet; bank saved as `methodId` |
| 1 | Recipient fields | `GET /api/remittance/payout-methods/{{methodId}}/fields` | `account_number`, label "Account number", 6–20 digits |
| 1 | Banks | `GET …/institutions` | BCA, BRI, Mandiri, BNI |
| 1 | Live rate preview | `GET /api/remittance/corridors/{{corridorId}}/rate` | `ok: true`, `midRate`, `appliedRate` (market rate less the margin), provider, publish time |
| 1 | Payment methods | `GET /api/remittance/payment-methods` | FPX (INSTANT, 15 min) and COUNTER (24H HOLD) |
| **2** | Start session | `POST /api/dev/remittance/sessions` `{"phone":"{{testPhone}}"}` | `flowToken` (starts `sm_`), `endpointKey` |
| 2 | INIT | `POST /api/dev/whatsapp-flows/{{endpointKey}}/exchange` `{"action":"INIT", …}` | `screen: VERIFY_IDENTITY`, `dev_hint` contains the 6-digit code → saved as `otp` |
| 2 | Skip ahead (refused) | same URL, action `quote_or_continue` | still `VERIFY_IDENTITY`, `error_message: "Please verify your number first."` |
| 2 | Verify OTP | `data.op = verify_otp`, `otp` | `screen: TRANSFER_DETAILS`, countries list |
| 2 | Select country | `select_country` | the country's payout methods |
| 2 | Calculate | `quote_or_continue`, amount `500` | `quote_ready: true`, e.g. "Guaranteed rate: 1 MYR = 4,308.53 IDR", "Transfer fee: MYR 0.00 (PROMO)", "Recipient gets: IDR 2,154,269", "Total to pay: MYR 500.00" |
| 2 | Continue | same again | `screen: BENEFICIARY`, slot 1 "Account number", banks list |
| 2 | Invalid recipient (refused) | `save_beneficiary`, `f1: "12ab"` | same screen, "Account number must be at least 6 characters." |
| 2 | Save recipient | `f1: "8204918234"` | `screen: COMPLIANCE` |
| 2 | Regulatory details | `save_compliance` | `screen: REVIEW_CONFIRM`, "Bank & account: Bank Central Asia (BCA) · ****8234", payment methods |
| 2 | Confirm with FPX | `confirm`, `payment_method: FPX` | `screen: SUCCESS`, `order_reference` like `IR-MY710075` |
| 2 | Confirm again | same | **the same** reference (no second order) |
| **3** | Encrypt a ping | `POST /api/dev/whatsapp-flows/{{endpointKey}}/encrypt-request` | an encrypted body exactly as Meta would send it |
| 3 | Real encrypted endpoint | `POST /api/webhook/whatsapp-flows/{{endpointKey}}` | `200`, Base64 text |
| 3 | Decrypt answer | `POST /api/dev/whatsapp-flows/decrypt-response` | `{"data":{"status":"active"}}` |
| 3 | Tampered body | same endpoint, junk body | `421` (or `432` if a signature is required) |
| **4** | Find the order | `GET /api/remittance/orders?search={{orderReference}}` | `AwaitingPayment` / `Pending` |
| 4 | Order detail | `GET /api/remittance/orders/{{orderId}}` | `providerPaymentId` (`mockpay_…`), timeline |
| 4 | Unsigned callback (refused) | `POST /api/webhook/payments/Mock` | **401**: payment cannot change without a valid signature |
| 4 | Customer pays | `POST /api/dev/mock-payments/{{paymentId}}/simulate?outcome=succeeded` | "Signed payment.succeeded callback accepted…" |
| 4 | Paid → Processing | `GET /api/remittance/orders/{{orderId}}` (waits 8 s) | payment `Succeeded`, order `Processing`, `payoutReference` (`mockpo_…`) |
| 4 | Payout delivered | `POST /api/dev/mock-payouts/{{payoutReference}}/simulate?outcome=completed` | "Signed payout callback accepted." |
| 4 | Completed | `GET /api/remittance/orders/{{orderId}}` (waits 8 s) | order `Completed` |
| **5** | Finished session | INIT with the old token | **427** |
| 5 | Unknown token | INIT with `sm_not-a-real-token` | **427** |
| 5 | Sign a callback | `POST /api/dev/remittance/sign-callback` | a valid `X-Mock-Signature` for your own body; change one character and the webhook answers 401 |

**What the timeline should show** (order detail → `events`, or the Transfers page):

```
Order created            (Customer)        Confirmed in WhatsApp: MYR 500.00 via FPX
Order awaiting payment   (System)          Payment mockpay_… started
Payment callback         (ProviderWebhook) Succeeded for MYR 500.00
Payment succeeded
Order paid
Order processing         (System)          Payout mockpo_… accepted
Payout callback          (ProviderWebhook) Completed
Order completed
```

Other outcomes to try: `simulate?outcome=failed` (order and payment **Failed**), `expired`, or `refunded` after a success (order **Cancelled**, payment **Refunded**). For each, start a new session from request 2.

> After your test contact's first paid transfer, later quotes show the normal fee (MYR 4.00) instead of the PROMO first-transfer fee. That is correct. Use another number from the fictional range to see PROMO again.

### Test 3 — The screens in the app (10 minutes)

```bash
npm run dev --prefix Frontend
```

Open `http://localhost:5173` and sign in.

1. **Setup › Send Money** (left rail of Setup):
   * **Countries & payout:**
     * Six countries. Click *Indonesia* to see its payout methods with fees, then *Bank Transfer* to see the 6-slot preview, the recipient fields and the banks.
     * Try *New Field* with a 21-character label: the counter turns red and Save is disabled.
     * *Actions › Check Live Rate* shows the market and customer rate.
   * **Regulatory lists:** Purpose, Source of funds, Relationship.
   * **Payment methods:** FPX and Pay at Counter.
2. **Transfers** (sidebar, under Support):
   * Your Postman order with **two badges**: order status and payment status.
   * Click the reference: amounts, recipient (masked account), payment and payout references, and the **timeline**.
   * Filter by status or search by reference.
3. **Chat:** open the test contact. You'll see cards such as *Payment received* and *Transfer completed*, marked failed if your connection's WhatsApp token is invalid (see Part 4).
4. **Bot Flow:** open *Send Money* (created switched off). It has a Start Trigger and a **WhatsApp Flow** node with a live preview. You can also drag a new *WhatsApp Flow* node in from the palette.
5. **Connect WABA → WhatsApp Flows card:** the checklist (public address, encryption key, Flow, publish). It needs a working connection and a public address for the later steps (Test 5).

### Test 4 — The mock payment page in a browser (3 minutes)

1. Run Postman folder 2 to create a new order, then folder 4 up to *Order detail*.
2. Open in a browser: `http://localhost:5155/api/dev/mock-payments/<paymentId>/checkout`, using the `paymentId` from Postman (`mockpay_…`). The order's own `paymentUrl` uses `App:PublicBaseUrl`, which only works while that tunnel is running.
3. You'll see the **SANDBOX** banner, the order, the amount and three buttons. Click **Simulate successful payment**.
4. Refresh the order in Transfers after a few seconds: **Paid**, then **Processing**.

### Test 5 — On a real phone with Meta (30–60 minutes)

You need:

* **A WhatsApp connection whose access token works.** Connection 21 ("Connection RMA") currently gets "Authentication Error" from Meta: refresh its token on Connect WABA, or use another connection.
* **Your number on the Meta app's test recipient list**, while the Flow is a draft.
* **A public HTTPS address** for your backend.

Steps:

1. **Tunnel.** Run `ngrok config add-authtoken <your token>` once, then:

   ```bash
   ngrok http 5155
   ```

   Copy the `https://…` address into `App:PublicBaseUrl` in your secrets file and restart the backend. A free ngrok address changes every start, so repeat steps 3–4 after each change.
2. **Leave the Meta messages webhook alone** (it points at the team's server). The Flow's data endpoint is separate and is set per Flow in step 4.
3. **Connect WABA → WhatsApp Flows → Create & Upload Key.** "Uploaded … · signature valid" should appear.
4. **Create Flow.** This uploads the screens and sets the endpoint `https://<ngrok>/api/webhook/whatsapp-flows/<key>`. Meta's validation errors, if any, are listed on the card. Within a minute "WhatsApp health check answered" should appear (Meta pings the endpoint).
5. **Seed demo data** (Postman folder 1) if not done yet.
6. **Send the Flow to a test number:** enter your WhatsApp number (it must exist as a contact) and press Send. This sends a real message.
7. **On your phone:** tap *Send money* and work through the five screens. The OTP is shown on the first screen as a development hint. Confirm with FPX.
8. Within about a minute the chat shows **"Order created · IR-MY…"** with a **Pay MYR …** button. Your backend's maintenance worker sends it, because Meta's completion webhook goes to the team's server while the messages webhook stays there. Tap it to open the mock checkout and simulate success. You'll get *Payment received*. Complete the payout with Postman folder 4 (*Payout delivered*) and you'll get *Transfer completed*.
9. **Publish** on the card only when everything works. It can't be undone; later changes need *Start a new version*.

What this test proves beyond Tests 1–4: Meta accepts the Flow JSON, the encryption works with Meta's real key handling, and the screens render correctly on a phone. Only then has the WhatsApp side really been verified.

---

## Part 4 — Troubleshooting

| Symptom | Cause | Fix |
|---|---|---|
| `427` on every Flow request | The session is finished, expired (30 min) or locked (5 wrong codes), or belongs to a deleted contact | Start a new session (Postman 2) with an unused fictional number |
| `409` "That number belongs to a deleted contact" | The number's contact was deleted | Use another number, or restore the contact |
| `404` on `/api/dev/...` | Not running in Development, or `Remittance:DevTools:Enabled` is off | Check the secrets file; run with `--launch-profile http` |
| Order stays **AwaitingPayment** after paying | The Send Money queue worker isn't running, or the queue guard blocks it | Make sure *your* backend is running. The guard must have the `remit-%` exemption (walkthrough §6.4). Check the timeline: a *Payment callback* event means it was applied. |
| Log: `Worker ASHOK-… has a stale local build` every ~5 s | The team's guard refusing your PC the **shared** campaign queues | Expected; nothing to fix for Send Money |
| `/health` says **Degraded** | Pre-existing: dead-lettered jobs, unreachable tracking tunnel, other builds polling the queue | Not caused by Send Money |
| "WhatsApp message failed: Authentication Error" in the timeline or Chat | The connection's Meta access token is invalid or expired (connection 21 today) | Refresh the token on Connect WABA, or test with a working connection |
| Recipient step returned **500** | (Fixed) Two-collection query without `.AsSplitQuery()` | Already fixed; the test suite now catches this class of bug |
| "Exchange rates are temporarily unavailable" | Both rate providers unreachable or stale | Check internet access; rates are never invented |
| Quote shows the normal fee instead of PROMO | The contact already has a paid transfer | Correct behaviour; use a new fictional number |
| `dotnet build` fails with a file-lock error (MSB3027) | The backend is still running | Stop it (Ctrl+C), then build |
| Send Money permissions vanish from roles | A backend without Send Money restarted and pruned them | Restart your backend (it re-adds them); goes away once Send Money is deployed everywhere |
