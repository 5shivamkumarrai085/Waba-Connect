# Waba-Connect — summary of all changes

**How each feature works** is explained in [FEATURE_GUIDE.md](FEATURE_GUIDE.md). This document is the change log.

This document lists everything changed in this engagement: what was fixed, what was added, where the code lives, how it was verified, what you still need to do yourself, and suggestions for what to build next.

**Nothing has been committed to git.** All changes are in the working tree: well over 350 files changed or added.

---

## Contents

1. [Verification status](#1-verification-status)
2. [Round 1 — bug fixes and hardening](#2-round-1--bug-fixes-and-hardening)
3. [Round 2 — startup fix and the 13 enterprise features](#3-round-2--startup-fix-and-the-13-enterprise-features)
   - [Round 3 — startup in any terminal, key guard, server-driven options, UI rework](#round-3--startup-in-any-terminal-key-guard-server-driven-options-ui-rework)
   - [Round 4 — campaign send failures, SES → SMTP, required contact fields, UI rework](#round-4--campaign-send-failures-ses--smtp-required-contact-fields-ui-rework)
   - [Round 5 — tracking that counts, campaign page, chat, audit log](#round-5--tracking-that-counts-campaign-page-chat-audit-log)
4. [Configuration switches](#4-configuration-switches)
5. [Database migrations](#5-database-migrations)
6. [New API endpoints](#6-new-api-endpoints)
7. [New permissions](#7-new-permissions)
8. [Tests](#8-tests)
9. [Things only you can do](#9-things-only-you-can-do)
10. [Issues found in your data](#10-issues-found-in-your-data)
11. [Future suggestions](#11-future-suggestions)
12. [How to run and verify](#12-how-to-run-and-verify)

---

## 1. Verification status

| Check | Result |
|---|---|
| Backend build, Debug and Release (`dotnet build`) | 0 errors, 0 warnings |
| Pending EF model changes (`dotnet ef migrations has-pending-model-changes`) | none |
| Frontend type check (`pnpm exec tsc -b`) | passes |
| Frontend lint (`pnpm lint`) | 0 errors (only pre-existing warnings) |
| Frontend production build (`pnpm build`) | passes |
| Dependency audit (`pnpm audit --prod`) | no known vulnerabilities |
| Integration suite (`Tests/EmailChannel.Tests`) | Round 4 full run: 515 of 516 passed (the failure was test data, fixed); phases 21 and 22 re-run after the final changes: 58/58 passed |
| Backend startup (`cd Backend; dotnet run`) | starts; health endpoint answers |
| Browser and API checks on the running app | see below |

Checks run against the live app (backend on port 5155, frontend on Vite):

- **Webhooks:** internal addresses are refused, with a clear message for each case: `127.0.0.1`, the cloud metadata address `169.254.169.254`, plain `http://`, and unknown event types.
- **Scheduled reports:**
  - Bad recipient, bad time zone, day of month above 28, and more than 20 recipients are each refused.
  - A valid schedule computes the correct next run: Monday 09:00 IST = 03:30 UTC.
- **Templates:**
  - Buttons are saved and returned.
  - An `http://` URL button is refused.
  - "Submit to Meta" without a connected WhatsApp account returns a clear 409.
- **Chat reply buttons:** more than 3 buttons, labels over 20 characters, and duplicate labels are each refused.
- **Connection scoping, switched on, with a temporary non-admin user:**

  | View | Admin sees | Scoped user sees |
  |---|---|---|
  | Campaigns and chats | connections 21, 25, 26, 27, 92 | connection 21 only |
  | Reports | 219 rows | 8 rows |

  The scoped user got a 404 on a conversation from another connection. After the test user was deleted, their token was rejected at once (401).
- **Banner headers:** all 8 form and wizard pages show the blue banner with readable titles and badges.

---

## 2. Round 1 — bug fixes and hardening

### Task 1 — Email replies reach Chat, and Chat UI parity

- **Root cause:** the mailbox `crm1@rma.my` had SMTP configured but no IMAP host, so replies were never read.
- **Fixes:**
  - The IMAP host is now derived from the SMTP host.
  - Replies already opened in webmail are no longer skipped.
  - Real replies are no longer mistaken for auto-replies.
  - Verified live: 3 real replies arrived in Chat, linked to their campaigns.
- **Chat page:**
  - Uses the dashboard header and styling.
  - Conversations and messages load page by page.
  - New messages arrive in real time (SignalR) instead of polling every second.
  - Searching for `(` no longer crashes the page.
  - An error boundary stops one broken page taking down the whole app.
- **Code:** [ImapEndpointResolver.cs](Backend/Services/Email/ImapEndpointResolver.cs), [Chat.tsx](Frontend/src/pages/Chat/Chat.tsx), [ChatPaging.cs](Backend/Models/DTOs/Chat/ChatPaging.cs), [ErrorBoundary](Frontend/src/components/ErrorBoundary/).

### Task 2 — Live campaign KPI cards

- **Root causes:**
  - a migration was missing a column, so events failed to save;
  - the real-time connection was blocked by CORS and the login check;
  - a race overwrote tracking ids;
  - replies and unsubscribes were never counted.
- **Now:**
  - Each person is counted once, however many times they open or click.
  - A sweep every minute recomputes counters from the recipient rows, so they cannot drift.
  - Bounces have their own counter instead of inflating Failed.
- **WhatsApp campaigns** now run on the Postgres job queue, so a restart resumes without sending twice.
- **Code:** [CampaignFinalizer.cs](Backend/Services/Email/CampaignFinalizer.cs), [Services/WhatsApp/](Backend/Services/WhatsApp/), [QueueHealthCheck.cs](Backend/Services/Queue/QueueHealthCheck.cs), [Services/Realtime/](Backend/Services/Realtime/).

### Task 3 — Production hardening, performance and dead code

- **Security:**
  - Account lockout, and short sessions with automatic refresh ([RefreshToken.cs](Backend/Models/Entities/RefreshToken.cs), [SecurityStampCache.cs](Backend/Services/SecurityStampCache.cs)).
  - Meta webhooks are rejected unless correctly signed.
  - WhatsApp tokens and passwords are encrypted at rest ([CredentialEncryptionMigrator.cs](Backend/Services/CredentialEncryptionMigrator.cs)).
  - Uploads are validated and stored privately ([Services/Storage/](Backend/Services/Storage/)).
  - Security headers ([SecurityHeadersMiddleware.cs](Backend/Middleware/SecurityHeadersMiddleware.cs)).
  - Forced password change ([MustChangePasswordFilter.cs](Backend/Middleware/MustChangePasswordFilter.cs)).
  - Phone numbers and emails are masked in logs ([PiiMaskingEnricher.cs](Backend/Helpers/PiiMaskingEnricher.cs)).
- **Performance:** paging on every large list ([pagination.ts](Frontend/src/services/pagination.ts)), new indexes, health endpoints, a minified bundle, a smaller icon set ([iconRegistry.ts](Frontend/src/utils/iconRegistry.ts)), and self-hosted fonts.
- **Dependencies:** HtmlSanitizer upgraded (fixes a known XSS flaw) and react-router upgraded.
- **Dead code:** unused files, store sections, exports and CSS removed.
- **Also fixed:**
  - KPI counters could show 0 in a background tab.
  - "No verified sender yet" always showed for Email.
  - Repeat unsubscribes were double-counted.
  - The unread-count check ran on the login page.

---

## 3. Round 2 — startup fix and the 13 enterprise features

You chose: all features, built in phases; no AI suggestions (no customer data leaves the system); and maker-checker approval for all campaigns while the switch is on.

### Phase 0 — Startup crash: `Encryption:Key is not configured`

**What the error was:**
- The app encrypts stored credentials (WhatsApp tokens, SMTP/IMAP passwords) with a key from `Encryption:Key`.
- That key lives in .NET user-secrets (`%APPDATA%\Microsoft\UserSecrets\<id>\secrets.json`).
- .NET loads user-secrets only when the environment is `Development`, so a VS Code terminal that didn't run as Development never saw the key. The app refused to start rather than handle credentials unencrypted.

**Fix:**
- User-secrets are now loaded in every non-Production environment, from both the SDK path and `%APPDATA%`. Environment variables and the command line still override them.
- A missing secret now fails with one clear message that lists:
  - which keys are missing;
  - the environment;
  - the paths checked;
  - the exact `dotnet user-secrets set` commands;
  - a warning to **reuse the same key**, because a new key makes stored passwords unreadable.
- Log masking no longer turns "Now listening on: http://…" into "h***".

**Code:** [StartupConfiguration.cs](Backend/Helpers/StartupConfiguration.cs), [Program.cs](Backend/Program.cs), [PiiMaskingEnricher.cs](Backend/Helpers/PiiMaskingEnricher.cs).

### Phase 1 — Real per-user connection scoping

**Before:** connection access used fake sample users (`usr-1`, "Aman Kumar"), and the switch was never read.

**Now:**
- Assignments point to real users and roles. The migration backfilled them and deleted the fake rows.
- `IAccessScope` decides which connections a user may see: all for administrators or when the switch is off; otherwise what is assigned to the user or their role. It is cached and invalidated on change.
- **Enforced in:**
  - Chat: list, open, send and email reply.
  - Campaigns: list, every by-id action, create/update and CSV.
  - Reports: a SQL-side filter.
  - Dashboard: the cache is keyed per scope.
  - Connections and email connections.
  - Live updates: SignalR groups per connection.
- A forbidden action returns 403; an out-of-scope record returns 404.
- The Connection Access pages pick real users and roles.
- **The switch is now ON** (see [Configuration switches](#4-configuration-switches)).

**Code:** [AccessScope.cs](Backend/Services/Security/AccessScope.cs), [PermissionManagementService.cs](Backend/Services/PermissionManagementService.cs), [PermissionService.cs](Backend/Services/PermissionService.cs), [CampaignHub.cs](Backend/Hubs/CampaignHub.cs), [DashboardCacheService.cs](Backend/Services/DashboardCacheService.cs), [ReportQueryService.cs](Backend/Services/ReportQueryService.cs), [AssignPermissionModal.tsx](Frontend/src/pages/Permissions/AssignPermissionModal.tsx).

### Phase 2 — Maker-checker approval (feature 12)

- With `Campaigns:Approval:Required = true`, every campaign (email and WhatsApp) waits in **Awaiting Approval**.
- A **different** user must approve it; the maker cannot approve their own, even as an admin. Reject requires a reason and cancels the campaign.
- Who asked, who decided, when and why are stored and audited.
- Editing an approved campaign sends it back for approval.
- **UI:** an approval panel on the campaign page, a "Pending approval" filter with a count, and a Cancel button.
- **Code:** [InternalApprovalGate.cs](Backend/Services/Campaigns/InternalApprovalGate.cs), [CampaignService.cs](Backend/Services/CampaignService.cs) (`ApproveAsync`, `RejectAsync`), [CampaignDetails.tsx](Frontend/src/pages/Campaigns/CampaignDetails.tsx), [CampaignsList.tsx](Frontend/src/pages/Campaigns/CampaignsList.tsx).

### Phase 3 — One-click retry of failed recipients (feature 1)

- **"Retry failed (N)"** re-sends only recipients that failed. Permanent bounces, complaints and suppressions are never retried.
- Each retry run is recorded. The maximum number of runs comes from `Campaigns:Retry:MaxRuns` (default 3).
- Counters are reconciled afterwards.
- **Code:** [CampaignRetryRun.cs](Backend/Models/Entities/CampaignRetryRun.cs), `CampaignService.RetryFailedAsync`, [CampaignFinalizer.cs](Backend/Services/Email/CampaignFinalizer.cs).

### Phase 4 — Compliance: consent, quiet hours, local-time sending, frequency cap (features 4, 5, 13)

- **Consent per contact, channel and topic,** with an append-only history (the audit trail).
  - Recorded from unsubscribe links, the new **preference centre** page, WhatsApp STOP/START keywords (configurable replies), and agents.
- **Quiet hours:** sends in the window are postponed, not dropped, and no attempt is used up.
- **Frequency cap:** at most N marketing messages per contact per N days, across channels.
- **WhatsApp opt-in** can be required for marketing.
- **Transactional campaigns** are exempt from caps and quiet hours.
- **Recipient-local time:** "09:30 in each recipient's time zone". Contacts gained a time zone field.
- Excluded recipients are marked **Skipped** with a reason, and shown on the campaign page.
- **Settings:** a "Compliance" section in OmniConnect Settings.
- **Code:** [ComplianceGuard.cs](Backend/Services/Compliance/ComplianceGuard.cs), [ConsentService.cs](Backend/Services/Compliance/ConsentService.cs), [ContactConsent.cs](Backend/Models/Entities/ContactConsent.cs), [PublicEmailController.cs](Backend/Controllers/PublicEmailController.cs) (preference centre), [ContactConsentsController.cs](Backend/Controllers/ContactConsentsController.cs), [ContactConsentPanel.tsx](Frontend/src/pages/Contacts/ContactConsentPanel.tsx), [ReferenceController.cs](Backend/Controllers/ReferenceController.cs).

### Phase 5 — Link-click report (feature 3)

- Clicks per link, unique clickers, and first and last click, with a CSV export. The export is protected against spreadsheet formula injection.
- **Code:** `CampaignService.GetLinkReportAsync`, [CampaignLinkReport.tsx](Frontend/src/pages/Campaigns/CampaignLinkReport.tsx).

### Phase 6 — Dynamic segments (feature 7)

- A rule builder with AND/OR groups over contact fields, tags, groups, consent, engagement (opened, clicked or replied in the last N days) and ad source.
- A live count and a sample of matching contacts.
- Segments are **re-resolved when the campaign sends**, so the audience is always current.
- The campaign wizard can target segments and groups.
- **Code:** [SegmentQueryBuilder.cs](Backend/Services/Segments/SegmentQueryBuilder.cs) (whitelisted fields only, no raw SQL), [SegmentService.cs](Backend/Services/Segments/SegmentService.cs), [Segment.cs](Backend/Models/Entities/Segment.cs), [SegmentsController.cs](Backend/Controllers/SegmentsController.cs), [pages/Segments/](Frontend/src/pages/Segments/).

### Phase 7 — Deliverability pre-check and proof sends (feature 8)

- **DNS checks:** SPF, DKIM, DMARC and MX, cached for 10 minutes.
- **Content lint:** spam phrases, image-to-text ratio, link shorteners, a missing unsubscribe link, Gmail clipping size, and unresolved merge fields.
- **Pre-flight step** in the wizard. A failure blocks sending unless an administrator overrides it; the override is audited.
- **Proof sends** to up to 5 addresses.
- **Code:** [DeliverabilityService.cs](Backend/Services/Email/DeliverabilityService.cs), [PreflightPanel.tsx](Frontend/src/pages/Campaigns/PreflightPanel.tsx).

### Phase 8 — A/B testing with automatic winner (feature 2)

- Variants use a different template or subject. A test share is split deterministically.
- The winner is picked by open, click, reply or read rate at the decision time, or manually with "Pick winner now". The remaining recipients then get the winner.
- **Code:** [AbTestService.cs](Backend/Services/Campaigns/AbTestService.cs) (with `AbTestWinnerWorker`), [CampaignVariant.cs](Backend/Models/Entities/CampaignVariant.cs), [AbTestEditor.tsx](Frontend/src/pages/Campaigns/AbTestEditor.tsx), [CampaignAbResults.tsx](Frontend/src/pages/Campaigns/CampaignAbResults.tsx).

### Phase 9 — Follow-ups and cross-channel journeys (feature 6)

- **Rules:** "not opened / not clicked / clicked / replied / failed / not read after N hours" leads to a follow-up campaign on either channel, or a tag.
- The follow-up goes through the whole pipeline: approval, consent, caps and quiet hours.
- **Code:** [FollowUpService.cs](Backend/Services/Campaigns/FollowUpService.cs) (with `FollowUpWorker`), [FollowUpRule.cs](Backend/Models/Entities/FollowUpRule.cs), [FollowUpEditor.tsx](Frontend/src/pages/Campaigns/FollowUpEditor.tsx).

### Phase 10 — Chat operations: assignment, routing, SLA, canned replies (features 9 without AI, and 10)

- **Status:** a conversation is Open, Pending, Resolved or Closed. A new message reopens it.
- **Routing:** new conversations are auto-assigned by round-robin or least-busy, only to agents allowed on that connection.
- **SLA:** a first-response timer, a "Reply by" chip that turns red when breached, and auto-close after N hours.
- **Canned replies:** a `/shortcut` in the composer, with `{{first_name}}`-style variables.
- **Settings:** a "Chat Routing & SLA" section in OmniConnect Settings.
- **Code:** [ConversationOperations.cs](Backend/Services/Chat/ConversationOperations.cs) (with `ChatSlaWorker`), [ConversationOwnerControls.tsx](Frontend/src/pages/Chat/ConversationOwnerControls.tsx), [Chat.tsx](Frontend/src/pages/Chat/Chat.tsx).

### Phase 11 — WhatsApp interactive templates and ad attribution (feature 11)

- **Template sync** now keeps buttons (quick reply, link, phone, copy code) and detects carousels. These were dropped before, so templates using them failed at send.
- **Sending** fills button values and carousel cards from campaign variables: `button_0` (link ending), `button_1_payload`, `button_2_code`, and `card_0_image` / `card_0_1`.
- **"New Template" dialog** on the Templates page: name, category, language, header, body with sample values, footer, up to 10 buttons, and a live preview. It is saved here and **submitted to Meta for review**. Meta's rules are checked first.
- **Reply buttons in Chat:** up to 3 tappable answers under a WhatsApp message, inside the 24-hour window.
- **Click-to-WhatsApp ads:**
  - The first ad that brought a contact in (ad id, headline, link, click id) is recorded.
  - It shows in the contact drawer, can be used in segments, and appears as the "Came From Ad" report column.
- **Code:** [TemplateComponents.cs](Backend/Services/WhatsApp/TemplateComponents.cs), [WhatsAppCloudApiService.cs](Backend/Services/WhatsAppCloudApiService.cs) (`BuildTemplateComponents`, `SubmitTemplateAsync`, referral handling), [TemplateService.cs](Backend/Services/TemplateService.cs) (`SubmitToMetaAsync`), [ChatService.cs](Backend/Services/ChatService.cs) (reply buttons), [CreateTemplateModal.tsx](Frontend/src/pages/Templates/CreateTemplateModal.tsx), [ContactDetailsDrawer.tsx](Frontend/src/pages/Contacts/ContactDetailsDrawer.tsx), [ReportCatalog.cs](Backend/Services/ReportCatalog.cs).

### Phase 12 — Signed outbound webhooks and scheduled reports (feature 14)

**Webhooks (Setup › Webhooks)**
- **Event types (17):**
  - messages: sent, delivered, read, failed;
  - email: opened, clicked, replied, bounced, unsubscribed, complained;
  - campaign status changes and approval decisions;
  - consent changes;
  - conversations: message received, assigned, status changed;
  - a test event.
- **Signing:** `X-Waba-Signature: t=<unix>,v1=<HMAC-SHA256(secret, t + "." + body)>`, plus `X-Waba-Event-Id` so receivers can ignore duplicates. The secret is encrypted at rest and shown only once, on create or rotate.
- **Reliable delivery:**
  - Deliveries are queued (Postgres) and retried with backoff for up to 8 attempts.
  - A webhook is switched off automatically after 20 failures in a row.
  - Events are written in batches, so a large campaign doesn't slow the live counters.
- **SSRF protection:**
  - https only; no credentials in the URL; no redirects.
  - Private, loopback, link-local, metadata, carrier-NAT and multicast addresses are refused, both when saved and again at connect time (defeats DNS rebinding).
- **Filters:** per connection, and personal data (recipient address) only if you opt in.
- **Management:** delivery log with the response code and duration, one-click replay (same event id), test button, and secret rotation.
- **Retention:** deliveries older than 30 days are removed automatically.

**Scheduled reports (Reporting › Scheduled Reports)**
- Any saved report can be emailed daily, weekly or monthly, at a local time in any time zone, as CSV, Excel or PDF, to up to 20 people, from a chosen email sender.
- Each run covers the last N days.
- **Runs with the scheduler's own access:** only their connections. It stops if they are deactivated or lose export permission.
- Daylight-saving gaps are handled. "Send now" is available. Only one server instance runs each schedule.

**Code:** [WebhookCore.cs](Backend/Services/Integrations/WebhookCore.cs) (event catalogue, signer, URL guard), [WebhookDelivery.cs](Backend/Services/Integrations/WebhookDelivery.cs) (emitter, sender, delivery worker), [WebhookSubscriptionService.cs](Backend/Services/Integrations/WebhookSubscriptionService.cs), [ReportScheduleService.cs](Backend/Services/Integrations/ReportScheduleService.cs) (service, runner, worker), [WebhookSubscription.cs](Backend/Models/Entities/WebhookSubscription.cs), [WebhooksController.cs](Backend/Controllers/WebhooksController.cs), [ReportSchedulesController.cs](Backend/Controllers/ReportSchedulesController.cs), [WebhooksList.tsx](Frontend/src/pages/Setup/Webhooks/WebhooksList.tsx), [ScheduledReports.tsx](Frontend/src/components/ReportBuilder/ScheduledReports.tsx). Events are raised from [EventPublisherConsumer.cs](Backend/Services/Email/EventPublisherConsumer.cs), [ConsentService.cs](Backend/Services/Compliance/ConsentService.cs), [ConversationOperations.cs](Backend/Services/Chat/ConversationOperations.cs) and [CampaignService.cs](Backend/Services/CampaignService.cs).

### Phase 13 — Banner header on form and wizard pages, and scoping switched on

- **Banner added to 8 pages:** Add/Edit Contact, Import Contacts, Add/Edit User (which had no title before), Message Bot wizard, Template Bot wizard, Campaign wizard (badges kept readable), Connect New WABA, and Connect New Email.
- **Code:** [layout.css](Frontend/src/styles/layout.css) (the "Form and wizard pages" block), plus each page's header.
- `Security:EnforceConnectionScoping` is switched **on** and was verified with a temporary non-admin user (see section 1).

### Fixes made during final verification

- **Webhook shutdown gap:** a delivery could be saved but never queued, or sent but not recorded, if the server stopped at the wrong moment.
  - Fixed: after sending, the outcome is always recorded.
  - An hourly sweep re-queues anything left behind. The idempotency key prevents double sends.
- **Scheduled-report time zone default:** browsers report some zones by old names (e.g. `Asia/Calcutta`), so the form defaulted to UTC. The old names are now mapped to current ones.
- **Campaign wizard:** the blue "Channel" badge was invisible on the blue banner. Fixed.
- **Query warnings:** three new background queries took a batch without a defined order. They now take the oldest first.
- **EF query-filter warnings:** child tables of campaigns and contacts now follow the soft-delete filter, and a deleted campaign's follow-ups are cancelled cleanly.

---

## Round 3 — startup in any terminal, key guard, server-driven options, UI rework

**Skills used** (found with find-skills, checked on skills.sh before installing):
- `diagnosing-bugs` (mattpocock/skills) — the feedback-loop discipline for the SMTP decrypt problem.
- `web-design-guidelines` (vercel-labs/agent-skills) — audit of every reworked screen against the Web Interface Guidelines.
- `vercel-react-best-practices` (vercel-labs/agent-skills) — React patterns while rebuilding the components.
- `emil-design-eng` (already in the project) — interaction polish and motion.

`anthropics/frontend-design` was deliberately not used: it pushes bold, one-off visuals, the opposite of a consistent enterprise UI.

### The "Required secret(s) not configured" startup error

- **What it was:** your secrets file was never missing. It is at `%APPDATA%\Microsoft\UserSecrets\740ac11e-…\secrets.json`, complete and unchanged since 25 Sep. The terminal you ran `dotnet run` from could not read it.
  - .NET's file-exists check says "not found" both for a missing file and for one the process may not read.
  - That fits an editor or agent terminal that only allows access inside the project folder.
- **Fix:**
  - The backend now also reads `Backend/appsettings.secrets.local.json`, which lives inside the project folder where any terminal can read it. It holds the **same** values as your user-secrets, including the unchanged encryption key.
  - The file is git-ignored (`appsettings.*.local.json`) and never copied to build or publish output (`WhatsAppCampaignApi.csproj`). It is not loaded in Production.
  - User-secrets and environment variables still override it.
  - A committed template shows the keys: `Backend/appsettings.secrets.local.example.json`.
- **Better error:** each file checked is reported as "found", "not found" or "exists but this process may not read it (access denied — a sandboxed terminal?)".
- **Verified by starting the real backend three ways:**

  | Scenario | Result |
  |---|---|
  | Normal | starts |
  | Only the project file (user-secrets hidden for that one run) | starts |
  | No secrets anywhere | fails with the new message |

- **Code:** [StartupConfiguration.cs](Backend/Helpers/StartupConfiguration.cs), [WhatsAppCampaignApi.csproj](Backend/WhatsAppCampaignApi.csproj).

### The SMTP password that "could not be decrypted" (connection 92)

**Diagnosis (with `diagnosing-bugs`):**
- **Feedback loop:** a scratch program decrypted every stored credential with the current key and printed only OK or FAIL.
- **Result:** the SMTP password on connection 92 **decrypts fine**, as do all 6 WhatsApp secrets. Nothing was damaged.
- **The key has never changed:** user-secrets and every committed `appsettings.json` share one fingerprint. This corrects my earlier report.
- **Ruled out:**
  - no user- or machine-level `Encryption__Key` variable;
  - every `SecretCipher` initialisation reads `Encryption:Key`;
  - no other copy of the project uses this database.
- **Most likely cause:** the backend running at 06:08 UTC had a stand-in key typed into that terminal session only, while working around the startup error.

**Fix:**
- **Startup key guard:** a backend whose `Encryption:Key` cannot read the stored credentials now refuses to start, with a clear message. Before, it started normally and dead-lettered sends one by one.
- The guard samples up to 5 stored values; one readable value proves the key. An unreachable database does not block startup.
- **Code:** [EncryptionKeyGuard.cs](Backend/Helpers/EncryptionKeyGuard.cs), called from [Program.cs](Backend/Program.cs).
- **Your choice, done:** the dead-lettered job (campaign "1234564") was discarded (status Cancelled, reason recorded). Nothing was sent. Health is back to **Healthy**.

### Server-driven options — nothing hardcoded in the UI

- **One catalogue on the server:** every option list and limit the new screens use now lives in [FeatureCatalogs.cs](Backend/Services/Catalogs/FeatureCatalogs.cs). That covers:
  - A/B metrics, follow-up conditions and actions, and their ranges;
  - report frequencies, formats and limits;
  - template categories, button types and per-type caps;
  - chat states, filters and reply-button limits;
  - webhook delivery states;
  - consent channels, states and sources;
  - segment fields and operators.
- **The server validates against the same catalogue.** The validators in `CampaignService`, `FollowUpService`, `ReportScheduleService`, `TemplateComponents`, `TemplateService`, `ChatService`, `DeliverabilityService` and `WebhookSubscriptionService` were changed to use it.
- **The UI reads it** from new `GET api/reference/*` endpoints. It goes through [referenceService.ts](Frontend/src/services/referenceService.ts), which caches each list once per page load, and the [useReference](Frontend/src/hooks/useReference.ts) hook.
- **Other fixes that came with it:**
  - Authentication templates are refused by "Submit to Meta", because they follow Meta's fixed layout and are made in WhatsApp Manager.
  - The browser's old time-zone names (e.g. `Asia/Calcutta`) are resolved by the server's alias map, so the schedule form defaults to your own zone.

### UI rework of every screen changed in this engagement

**Standards applied:**
- **Design tokens only:** no raw hex, rgb or px colours, verified by a grep gate.
- **Missing tokens added once** in `index.css`:
  - status borders;
  - banner "glass" colours;
  - WhatsApp preview colours.
- **Undefined tokens fixed:** 13 token names used across older stylesheets were never defined, so browsers showed off-palette fallback colours. They now point at real tokens, for example `--danger` and `--primary-color`.
- **Shared components instead of one-offs:**
  - `StatusBadge`, `EmptyState`, `Skeleton`, `Pagination`, `SearchableSelect`, `ChoicePills` and `Modal`;
  - `WhatsAppPreview`, now with header, footer and buttons;
  - `Toggle`.
- **Every state designed:** loading as skeletons, empty with a next step, error with "Try again", and disabled with a reason.
- **Buttons:** spinner plus "Saving…" while busy. A save error is shown inline, announced (`role="alert"`) and focused.
- **Accessibility:**
  - labels on every control, and `aria-label` naming the item on every icon button;
  - `aria-live` for asynchronous results;
  - `:focus-visible` rings;
  - decorative icons hidden from screen readers;
  - dialogs contain their scroll.
- **Locale:** dates via the app's `formatAbsoluteDateTime` and `formatRelativeTime`. Weekday names, ordinals and numbers via `Intl`, with tabular numerals in number columns.
- **Copy:** Title Case buttons, specific labels, `…`, curly quotes, and errors that say how to fix them.
- **Motion:** transform and opacity only, the app's easing tokens, and `prefers-reduced-motion` respected.
- **Responsive:** checked at 375 px — no page scrolls sideways.

**Screens reworked:**

| Screen | Code |
|---|---|
| Setup › Webhooks | [WebhooksList.tsx](Frontend/src/pages/Setup/Webhooks/WebhooksList.tsx) |
| Reporting › Scheduled Reports | [ScheduledReports.tsx](Frontend/src/components/ReportBuilder/ScheduledReports.tsx) |
| New Template dialog and template button chips | [CreateTemplateModal.tsx](Frontend/src/pages/Templates/CreateTemplateModal.tsx), [TemplatesList.tsx](Frontend/src/pages/Templates/TemplatesList.tsx) |
| Chat: owner and status controls, SLA chip (now updates itself), reply buttons, filters | [ConversationOwnerControls.tsx](Frontend/src/pages/Chat/ConversationOwnerControls.tsx), [ReplyButtonsPopover.tsx](Frontend/src/pages/Chat/ReplyButtonsPopover.tsx) |
| Segments list (search and page in the URL) and editor (managed status/type/source lists, unsaved-changes guard) | [SegmentsList.tsx](Frontend/src/pages/Segments/SegmentsList.tsx), [SegmentEditor.tsx](Frontend/src/pages/Segments/SegmentEditor.tsx) |
| Campaign wizard: A/B test, follow-ups, pre-flight | [AbTestEditor.tsx](Frontend/src/pages/Campaigns/AbTestEditor.tsx), [FollowUpEditor.tsx](Frontend/src/pages/Campaigns/FollowUpEditor.tsx), [PreflightPanel.tsx](Frontend/src/pages/Campaigns/PreflightPanel.tsx) |
| Campaign page: approval panel, A/B results, link report (semantic progress bars), follow-ups | [CampaignDetails.tsx](Frontend/src/pages/Campaigns/CampaignDetails.tsx), [CampaignAbResults.tsx](Frontend/src/pages/Campaigns/CampaignAbResults.tsx), [CampaignLinkReport.tsx](Frontend/src/pages/Campaigns/CampaignLinkReport.tsx) |
| Contact Consent tab and "Came from ad" row | [ContactConsentPanel.tsx](Frontend/src/pages/Contacts/ContactConsentPanel.tsx), [ContactDetailsDrawer.tsx](Frontend/src/pages/Contacts/ContactDetailsDrawer.tsx) |
| Connection Access assign dialog | [AssignPermissionModal.tsx](Frontend/src/pages/Permissions/AssignPermissionModal.tsx) |
| Form and wizard banners (tokens; self-contained back and close buttons) | [layout.css](Frontend/src/styles/layout.css) |

**Shared components improved for the whole app:**
- **`Toggle`:** it was a clickable `div` over a read-only checkbox, which couldn't be reached or used by keyboard. It is now a real switch (`role="switch"`) that works with Tab and Space.
- **`Modal`:** decorative icon hidden from screen readers, and scroll contained.
- **Global utilities:** `.sr-only` and `.text-right`. `.sr-only` was previously defined only in Chat's stylesheet.

**Audit:** the `web-design-guidelines` audit found only small issues, all fixed:
- trigger text without `…`;
- one locale-default date;
- straight quotes;
- modal icons not hidden from screen readers;
- modal scroll not contained;
- the segment list's search and page not in the URL.

---

## Round 4 — campaign send failures, SES → SMTP, required contact fields, UI rework

**Skills used:**
- `impeccable` (pbakaus; found with find-skills; 300K installs, passes Socket/Snyk): refinement and polish of every reworked screen within the existing tokens.
- Also: `web-design-guidelines`, `vercel-react-best-practices`, `emil-design-eng`, `engineering:system-design`, `engineering:code-review`, `engineering:documentation`.

### Campaigns failed to send (task 2)

**"Save Changes" returned 500.**
- `CampaignService.UpdateAsync` loaded the campaign's variables **and every recipient row** in one query. EF refuses that in Development (`MultipleCollectionIncludeWarning`).
- **Fix:**
  - Recipients are no longer loaded. They are replaced with a set-based delete and insert inside a transaction, under the database retry strategy.
  - Pre-flight, A/B and follow-up validation now run before anything is written.

**"Stored SMTP password could not be decrypted", with every recipient counted as Bounced.**
- **Root cause:** another, **older build of the app** was connected to the same database and job queue.
  - It doesn't understand this build's credential format (`enc:v2:`).
  - It leased this build's send jobs, failed to decrypt, and marked each recipient failed.
  - Found in `pg_stat_activity`: foreign clients polling `JobQueue`.
  - The stored password itself decrypts fine with the real key.
- **Fixes:**
  - **Queue contract version:** every queue name now ends in `.v2`, and the migration renamed the waiting jobs, so older builds can no longer see this build's jobs.
  - The health check reports **Degraded** while a foreign queue client is connected, and the key guard warns about it at startup.
  - **A connection problem is no longer a recipient failure.** An unusable connection (inactive, or undecryptable) puts the campaign **on hold** (Paused, with the reason) and leaves recipients Pending. Fix the connection and press Resume.
  - The key guard now checks every stored credential (SMTP, IMAP, WhatsApp) and names the unreadable ones.
  - Connections whose password can't be read show **Needs attention**.

### Other campaign fixes (tasks 4, 5, 7)

- **Campaign page:**
  - Separate Bounced (orange) and Failed (red) cards.
  - Pending excludes Skipped.
  - An on-hold banner shows the reason.
  - The status pill is readable on the banner.
  - Colours and spacing are tokenised.
- **Relation types:** Lead, Customer and Vendor now all carry through.
  - Types come from Setup › Type (the fixed array is removed) and are checked by the server.
  - The audience step has per-type chips and counts, a server-paged contact picker, and "send to every contact of these types".
- **A/B testing:**
  - Three-variant tests always went to A (the hash multiplier was divisible by 3). Variants are now dealt round-robin over a hashed order, so each gets an exact share.
  - Invalid A/B settings are refused before the campaign is saved.
  - The winner waits for a minimum number of sends per variant.
  - The decision window is counted from the first test send.
  - UI: variant cards, a live split bar matching the server, and metric explanations.
- **Wizard screens:**
  - Consent topic chosen from the configured topics, and checked by the server.
  - Pre-flight summary with problems first.
  - Follow-up panel headers.
  - WhatsApp variable fields read from each template's own placeholders.

### Contacts and data (task 6)

- **Data reset:** all contacts and campaigns were **soft-deleted** (reversible), after a JSON backup of 16 tables (`Backend/backups/`, git-ignored).
  - [purge-contacts-campaigns.sql](Backend/scripts/purge-contacts-campaigns.sql) deletes them permanently, for you to run.
  - [restore-contacts-campaigns-2026-09-29.sql](Backend/scripts/restore-contacts-campaigns-2026-09-29.sql) undoes the reset.
- **Required fields:**
  - Name, phone, type, status and source are always required.
  - Email and date of birth are required by default, and administrators change the list under OmniConnect Settings › Contacts.
  - One rule set drives the form (`api/reference/contact-fields`), the API validators and the **CSV import**. The import dialog's column table and the sample file come from `GET api/Contacts/csv-layout`.
  - Contacts created by incoming messages are exempt.
- **Date of birth and age:** date of birth is stored and age is computed on read. Segments gain an "Age" rule (at least / at most / not set).

### Dashboard (task 1)

- KPI cards in one row. The grid follows the card count and folds only on narrow screens.
- Live updates through a debounced SignalR `dashboardChanged` signal, with polling as a fallback.
- An error state with Try again.
- Wrong trend percentages removed.

### Segment editor (task 3), Chat (task 8), Connections (task 9)

- **Segment editor:** redesigned.
  - All/any control; Where/And/Or connectors.
  - Live preview with count and sample.
  - Sticky footer; mobile layout.
- **Chat:**
  - The All Channels menu renders above the header (portal plus anchored position; keyboard support).
  - Send is always visible: the composer body scrolls and the action bar is pinned.
  - Mobile single-pane.
  - Rich location and contact cards.
  - Everything tokenised.
- **Connections:**
  - The email connection page is rebuilt: SMTP, IMAP, domain health, test email and senders.
  - Port presets come from the server.
  - Unsaved-changes guard.
  - **Your IMAP settings:** SMTP `mail.rma.my:465` authenticated, and IMAP works through the derived settings (`mail.rma.my:993`, SSL; 20 messages read).

### Amazon SES removed, platform SMTP added (task 10)

- **Removed:**
  - 3 AWS packages;
  - the SES sender, SNS webhook and validator, and domain provisioning;
  - SES options and config;
  - SES columns and the `EmailDeliveryEvents` and `EmailSendingDomains` tables (migration `RemoveAmazonSes`; old SES connections become inactive SMTP).
- **Added:** `SmtpOptions` (section `Smtp`), plus `IEmailSender` / `SmtpEmailSender` exactly as specified.
  - Port 465 uses SSL on connect; other ports use STARTTLS; never Auto.
  - Never throws.
  - Registered in DI; `Backend/.env.example` has the `Smtp__*` block.
- **Uses:**
  - Welcome emails use it when "Send welcome email" is ticked.
  - `GET api/system-mail/status` and `POST api/system-mail/test` check it.
- **Kept, as you chose:** campaigns, chat, proofs and scheduled reports send through each connection's own SMTP account.
- **Deviation:** scheduled reports can't fall back to `IEmailSender`, because the specified interface has no attachments.

### Round 4 migrations, endpoints and tests

- **Migrations** (applied, inspected):
  - `CampaignHoldAbWindowAndContactDob`: date of birth and index, wider relation types, A/B window, hold reason, queue rename, required-fields default.
  - `RemoveAmazonSes`.
  - `ContactTypeIndex`.
- **Endpoints:**
  - `GET api/reference/email-options`, `GET api/reference/contact-fields`;
  - `GET api/Contacts/type-counts`, `GET api/Contacts/csv-layout`;
  - `GET api/email/connections/{id}/domain-health`;
  - `GET api/system-mail/status`, `POST api/system-mail/test`.
  - **Removed:** `api/EmailDomains/*`, `POST api/webhook/email/ses`, and the connection's refresh-sender endpoint.
- **Tests:** Phase 22 (44 checks):
  - three-way split;
  - postponed decision;
  - validation before save;
  - types and topics from managed lists;
  - campaign edit (was a 500);
  - required fields and age;
  - CSV import rules;
  - age segment;
  - queue contract;
  - hold on an unreadable password;
  - SMTP sender off, unconfigured, and refusing plaintext.
  - The SES tests were replaced with SMTP equivalents.

---

## Round 5 — tracking that counts, campaign page, chat, audit log

**How it was done:**
- Plan: [docs/superpowers/plans/2026-10-01-round5-tracking-audit-chat.md](docs/superpowers/plans/2026-10-01-round5-tracking-audit-chat.md).
- Executed task by task with superpowers `executing-plans` and `test-driven-development`: a failing test first, then the fix.
- UI work used `impeccable` (polish and detector) and `web-design-guidelines` (audit).
- find-skills was checked; `anthropics/skills@webapp-testing` was considered, and the built-in browser covered the sweep instead.

### 1. Unique opens, clicks, unsubscribes and bounces did not move (task 1)

**Root cause:** every tracking and unsubscribe link pointed at an address recipients cannot reach.
- In Development, `App:PublicBaseUrl` was `http://localhost:5155`.
- `appsettings.Development.json` also hard-coded a dead trycloudflare tunnel for tracking.
- Gmail loads images through Google's servers, so no open or click ever reached the API. The database held only Sent events.
- Because A/B by open rate counts opens, it had nothing to decide on.

**Fixes:**
- **One public address.** The hard-coded tunnel is gone.
- **The address is now checked**, so a wrong one shows up instead of silently losing everything:
  - `PublicEndpointProbe` calls `{address}/api/t/ping/{nonce}` and passes only when this exact server answers.
  - Pre-flight shows the result as a pass or a warning, and `/health` has a `tracking-address` check.
- **Pre-flight also says how many links will be tracked.** Your templates had none, which is why the Links card said "No clicks yet".
- **Self-healing counters.** The reconcile sweep repairs recipients from the event log. A crash between recording an open and stamping the recipient can no longer lose it, and campaigns that finished long ago are swept when a late event arrives.
- **Bounces:**
  - A 5xx refusal at `RCPT TO` is now a bounce, not a failure.
  - Plain-text Exim/cPanel/qmail bounce reports are parsed; they used to be discarded as auto-replies.
  - A report without a Message-ID is matched by address within 7 days, on the same connection.
- **A/B "no signal" rule.** 0 % vs 0 % no longer silently picks A. The test waits up to 24 h more, then keeps A and says why (`AbDecisionReason`).

**Verified live** through a Cloudflare quick tunnel (installed with your OK, now stopped). You opened the test email in Gmail and clicked both links:
- Unique Opens 1 and Unique Clicks 1 appeared live.
- The A/B table showed variant A at 100 %, and the Links card listed both URLs.
- Your unsubscribe counted live. Re-subscribing in the preference centre lifted the suppression, and all of it was audited.

**Bounce, live:** mail.rma.my accepts mail for non-existent local mailboxes and sends no report. Two probes produced nothing in the inbox, so a live bounce can't be produced on that server. The parser and the SMTP path are covered by tests.

### 2. Queue / Executed tabs (task 1)

- **Cause:** an effect switched the tab back to Executed every time Queue was clicked on a finished campaign.
- **Now:**
  - The default applies once.
  - The tab is kept in the URL (`?tab=`) and has proper tab semantics and arrow keys.
  - Slow responses for the previous tab are ignored.
  - The styles are page-scoped; they used to depend on which page loaded first.
- **Live refresh:** the A/B table and Links card now refresh with live events too, batched every 2 s and filtered by event kind.

### 3. Test templates (task 2)

- All 12 `zz_emailtest_r4_*` templates were backed up to `Backend/backups/2026-10-01-round5/`, then deleted through the app. Each delete is audited.
- The test suite now removes the templates it creates.
- **Deleting a template still in use** (including by a deleted campaign, an A/B variant, a template bot or a follow-up) is now refused with a clear reason. It used to cascade-delete the deleted campaign or silently blank the variant.
- The Campaign → Template foreign key is now Restrict.

### 4. Chat (task 3)

**Tested live** from the UI to manishmishra8970@gmail.com: New Email, Reply, Reply All (Cc correctly empty) and Forward (To left empty, "Fwd:"). All four were sent and audited.

**Fixed:**
- New Email inherited the previous message's threading headers, so Gmail filed it under the old thread.
- A subject or email search showed nothing (the page re-filtered to name and phone).
- Switching New Email → Reply lost the draft.
- At 720 px height, Send sat below the fold (the panel height was a guess).
- The page jumped when opening a conversation.
- The owner name was truncated by the workload count.
- Delete Chat, the email composer and notes ignored permissions.
- Delete Note had no confirmation.

**UI:**
- Reply / Reply All / Forward appear once per message, and New Email is in the header; a duplicate button row was removed.
- The three stacked filters fold into one row with a summary and Clear.
- The phone header wraps instead of pushing controls off screen.
- `impeccable` detector: 4 findings fixed (side-stripe border, overshooting animation ×2, height transition).

### 5. Dashboard and every page (task 4)

- **Top Campaigns "View All"** now opens `/campaigns/campaign` (it was `/campaigns`, a 404).
- **Permissions:** both View All buttons and the quick-create menu respect permissions. `/setup` opens the first page you may see, and bot "view" links accept View rights.
- **Sweep of 47 routes:** 45 clean. The 2 expected 404s now redirect. Every API call returned 200/204, and the 14 main pages have 0 px sideways scroll at 375 px.

### 6. Audit log vs activity log (task 5)

- **Why "creating a contact" wasn't audited:** your contacts were re-added after the reset with the same phone numbers. That restores the soft-deleted record, a path that never wrote an audit row. It now does.
- **Newly audited:**
  - Contact notes.
  - Agent WhatsApp replies and template sends.
  - Template status changes and syncs.
  - Contacts created automatically (by System).
  - Contacts created by CSV campaigns (as a summary).
  - Campaign held, completed and failed (by System, once each).
  - Password changes, now through the audit service.
- **Actor:** "System" only for background work. An anonymous request (a failed sign-in) is left blank.
- **Recent Activity** is now the audit log (newest 8 business events, with who did them). It used to be stitched from table timestamps, which made test data look like approvals.
- **Setup › Activity Log removed completely:**
  - The table (97 rows backed up first; restore script in `Backend/scripts/`), entity, writers, API and page.
  - `MessageSendContext`, the payload redactor and the job-failure writer.
  - The `ActivityLog.Delete`/`Clear` permissions.
  - The sidebar page is now "Audit Log" (`/audit-log`; old links redirect).

### 7. Found while testing

- **An older build was running.** Your own backend (started 10:28, before these changes) was taking this build's queue jobs and reading the same mailbox.
  - The queue contract is now v3 (migration `QueueContractV3`).
  - Database sessions are named `WabaConnect/v3`, so `/health` flags a mismatched build. It did flag yours.
  - You stopped it during testing. **Start it again to run the new code.**
- **rma.my has no DMARC record.** Gmail and Yahoo require one for bulk mail; add it at your DNS provider.
- **axios 1.20.0:** 12 new advisories (7 high) had appeared since round 4. Upgraded; `pnpm audit --prod` is clean.
- **Dead code removed:** the unused `setUnauthorizedHandler` shim, `fade` and `slideFromRight` motion variants, and `matcherFor`. No unused files or dependencies; no unreferenced backend types.

### 8. Chat inbox redesign (your reference image)

Rebuilt as a three-pane inbox — **list | conversation | details** — from the features that exist, without Tailwind or any new library (plain React and CSS tokens). Full description: FEATURE_GUIDE §11.

- **Header:** Channel menu and **New Email** side by side.
- **List:** connection, From with refresh, search with a filter button, **tabs with counts (All / Unread / Mine)**, richer rows (status, type, SLA, owner, unread), and a footer with "1–8 of N", paging and **Newest / Oldest** sort.
- **Conversation:** header with copy-email, **Assigned to**, search, details toggle and a ⋮ menu (resolve/reopen, new email, send template, delete); a **subject bar** (subject, status, channel, SLA, WhatsApp reply window, date); and an **email composer that opens only when you press Reply / Reply All / Forward or New Email**, with a ✕ to close it (it asks before throwing away a draft). A permanently docked version with its own Reply / Reply All / Forward row was tried first; it repeated the message buttons and hid the thread, so it was removed at your request.
- **Details panel (new):** Customer (contact record, tags, groups, notes), Conversation (status, owner, SLA, connection) and **Activity** (this customer's and this conversation's audit history), plus quick actions.
- **Responsive:** panes size to the inbox's own width (CSS container queries), so the app sidebar is accounted for; details overlay below 1000 px; one pane at a time on phones.

Nothing on the page is a fixed list: tabs, sort orders, statuses, filters and their defaults come from `api/reference/chat-options`; counts and history come from the server.

**Server changes:**
- `GET api/Chat/conversations/counts`: one grouped query, same visibility and filters as the list.
- `sort=newest|oldest` on the conversation list, with keyset paging in both directions.
- `entity=Type:Id` (repeatable) on `api/Activity/audit-logs`, served by the `(EntityType, EntityId)` index. A malformed reference matches nothing, never the whole log.
- Chat sends, email replies and contact notes are now audited on the conversation or the contact, so they appear in that history.

**Removed as dead code:** the old info drawer, header delete menu, WhatsApp window dot and floating time banner, the filter summary row, `setConversationsFilter`, a client-side unread re-filter, and 91 unused CSS rules. Some of those rules had generic names (`.text-blue`, `.note-item`) that leaked onto other pages after a visit to Chat.

**Verified in the browser** on separate verify servers (ports 5199/5175):
- 1440 px with the app sidebar open and closed.
- 375 px phone.
- Each tab against its count.
- Oldest/Newest order.
- Filters and the ⋮ menu.
- New Email → Reply All → Discard.
- Activity history loading.
- One real reply sent from the docked composer to manishmishra8970@gmail.com, audited on the conversation.

Results: no horizontal scroll at either width, and Send is in view on the phone.

### Round 5 migrations, endpoints and tests

- **Migrations (applied, inspected):**
  - `AbDecisionReason`.
  - `QueueContractV3` (renames waiting jobs).
  - `RemoveMessageActivityLog`: drops the table and the two permissions; Campaign→Template Restrict; indexes `AuditLogs (Module, CreatedAt)`, `(EntityType, EntityId)`, `Status` and `EmailEvents (CreatedAt)`.
- **Endpoints:**
  - Added: `GET api/t/ping/{nonce}`, `GET api/Chat/conversations/counts`.
  - Extended: `sort` on `GET api/Chat/conversations`; repeatable `entity` on `GET api/Activity/audit-logs`.
  - Removed: `api/setup/activity-log/*`.
  - `campaign-options` gained `abNoSignalGraceHours` and `abDecisionReasons`; `chat-options` gained `readFilters`, `quickViews` and `sortOrders`.
- **Tests:**
  - New Phase 23 (59 checks): public address, pre-flight, opens/clicks, Links report, crash repair, plain-text and SMTP bounces, audit coverage, Recent Activity, template delete safety, New Email threading, actor attribution, inbox tab counts, Newest/Oldest keyset paging, and a record's audit history.
  - Final full run: 585 of 590. The 5 failures are all Phase 19 webhook deliveries that **your older backend on port 5155 picked up** from the shared database and refused, because it does not allow `localhost`, which the test harness does. With that backend stopped, Phase 19 passes, as it did earlier this round.
  - Phase 22 gained the A/B no-signal and hold-audit checks; Phase 5 checks the pixel and links on the wire.
  - The harness records audit events in memory.

### Things for you

1. **Restart your backend** (port 5155) from this code. The one running now (started 4:51 PM) is an older build: it lacks the new inbox endpoints (the tabs would show no counts), and it competes for the shared queue.
2. **Set `App:PublicBaseUrl`** to the API's public https address in every environment that sends real email. Locally, use a tunnel (see FEATURE_GUIDE §18).
3. **Add a DMARC record for rma.my.**
4. **Optional:** make mail.rma.my reject unknown local mailboxes (cPanel › Default Address › "Discard with error"), so bounces are reported.
5. Test data left for you to look at:
   - The "Link tracking check" email template (kept, as agreed).
   - Campaigns "Round 5 tracking check" (441) and "Round 5 bounce check" (442, 444).
   - The bounce-check contacts are soft-deleted.

### Missing / future (not built this round)

- **Machine-open detection** (Apple Mail Privacy Protection preloads images, which inflates opens). Flag opens within seconds of delivery from known proxy ranges.
- **Unsubscribe confirmation step.** One click registered twice, most likely Gmail's link scanner fetching the GET link. The counter is idempotent, but a GET that only shows a confirm button (with the unsubscribe on POST) is safer against scanners.
- **Signed bounce addresses (VERP)**, so a forged "delivery failed" email cannot suppress an address.
- **Audit retention and partitioning** by month, and a `pg_trgm` index for audit text search.
- **Redis backplane for SignalR** before running more than one backend instance.
- **A dead-letter queue page** in Setup.
- **A WhatsApp test number in the dev database.** None is connected, so WhatsApp chat sends could only be checked by code and build, not live.

---

## 4. Configuration switches

All live in `Backend/appsettings.json` (or environment variables). None needs a code change to flip.

| Setting | Default now | Meaning |
|---|---|---|
| `Security:EnforceConnectionScoping` | **true** | Non-admins see only the connections assigned to them or their role (Setup › Connection Access). |
| `Campaigns:Approval:Required` | false | Maker-checker approval for every campaign. |
| `Campaigns:Retry:MaxRuns` | 3 | Maximum retry runs per campaign. |
| `Webhooks:DisableAfterFailures` | 20 | Consecutive failed deliveries before a webhook is switched off. |
| `Webhooks:RetentionDays` | 30 | How long the delivery log is kept. |
| `Webhooks:AllowHttp` / `Webhooks:AllowPrivateNetworks` | false | Non-Production only, for testing against a local receiver. Always off in Production. |
| OmniConnect Settings › **Compliance** | — | Quiet hours, default time zone, frequency cap, WhatsApp opt-in rule, STOP/START keywords and replies. |
| OmniConnect Settings › **Chat Routing & SLA** | — | Routing strategy, agents, first-response and resolution targets, auto-close. |
| OmniConnect Settings › **Contacts** | email, date of birth | Which contact fields are required (name, phone, type, status and source always are). |
| `Dashboard:LiveUpdateIntervalSeconds` | 3 | Minimum gap between live dashboard refresh signals. |
| `Smtp:*` (or `Smtp__*` env vars) | not set | Platform system mail (`IEmailSender`). Off until Host, From address and Password are set. |

---

## 5. Database migrations

All migrations are applied to the dev database, and each was inspected for unrelated changes.

**Round 1:** `SyncEmailEventsSnapshot`, `InboundRepliesAndCampaignCounters`, `ScalabilityIndexesAndWhatsAppQueue`, `AuthSessionsAndLockout`.

**Round 2:**

| Migration | Adds |
|---|---|
| `ConnectionScoping` | Real user and role links for connection access. Backfill, and removal of sample rows. |
| `CampaignApprovalActors` | Who requested and who decided an approval. |
| `CampaignRetryRuns` | Retry run history. |
| `ComplianceControls` | Consent tables, contact time zone, campaign topic, transactional flag, local send time, skipped count, cap index. |
| `DynamicSegments` | Segments and campaign-segment links. |
| `AbTesting` | Variants and the A/B fields on campaigns and recipients. |
| `FollowUps` | Follow-up rules and parent campaign link. |
| `ChatOperations` | Conversation owner, status, SLA timestamps and indexes. |
| `InteractiveTemplates` | Template components and buttons; contact ad-attribution fields. |
| `WebhooksAndReportSchedules` | Webhook subscriptions, delivery log, report schedules. |

---

## 6. New API endpoints

| Area | Endpoints |
|---|---|
| Connection access | `GET api/permissions/candidates` |
| Approval | `GET api/campaigns/pending-approval/count`, `POST api/campaigns/{id}/approve`, `POST api/campaigns/{id}/reject` |
| Retry | `POST api/campaigns/{id}/retry` |
| Links | `GET api/campaigns/{id}/links` |
| Deliverability | `POST api/campaigns/precheck`, `POST api/campaigns/proof` |
| A/B | `POST api/campaigns/{id}/ab-test/decide` |
| Consent | `GET/POST api/contacts/{id}/consents`; public `GET/POST api/public/email/preferences?t=` |
| Reference | `GET api/reference/time-zones`, `GET api/reference/consent-topics`; Round 3: `time-zone-aliases`, `campaign-options?channel=`, `report-schedule-options`, `template-options`, `chat-options`, `webhook-options`, `consent-options`, `segment-fields` |
| Segments | `GET/POST api/segments`, `GET/PUT/DELETE api/segments/{id}`, `POST api/segments/preview`, `GET api/segments/fields` |
| Chat | `GET api/Chat/assignable-agents`, `POST api/Chat/conversations/{id}/assign`, `POST api/Chat/conversations/{id}/status`; `replyButtons` on the send-message body |
| Templates | `POST api/Templates/{id}/submit`; `buttons` on create and update |
| Webhooks | `GET/POST api/webhooks`, `PUT/DELETE api/webhooks/{id}`, `GET api/webhooks/event-types`, `POST api/webhooks/{id}/rotate-secret`, `POST api/webhooks/{id}/test`, `GET api/webhooks/{id}/deliveries`, `POST api/webhooks/deliveries/{id}/replay` |
| Scheduled reports | `GET/POST api/report-schedules`, `PUT/DELETE api/report-schedules/{id}`, `GET api/report-schedules/senders`, `POST api/report-schedules/{id}/run` |

All existing routes and response shapes are unchanged; new fields are additive.

---

## 7. New permissions

These are seeded automatically and granted to the built-in roles at startup:

- `Campaign.Approve`, `Campaign.Retry`
- `Segment.View`, `Segment.Manage`
- `Consent.View`, `Consent.Manage`
- `Chat.Assign`
- `Webhook.View`, `Webhook.Manage`
- `Reporting.Schedule`

---

## 8. Tests

**Suite:** `Tests/EmailChannel.Tests`, a console integration suite against the dev database.

**Run it** with the backend stopped (it shares the job queue):

```
cd Tests/EmailChannel.Tests
dotnet run            # all phases
dotnet run -- 9 19    # chosen phases
```

| Phase | Covers | Checks |
|---|---|---|
| 0–7 | Channel schema, queue, providers, domains, templates, send pipeline, events, bulk CSV | 318 |
| 8 | Reliability and secrets | 33 |
| 9 | Connection scoping | 10 |
| 10 | Maker-checker approval | 14 |
| 11 | Retry | 14 |
| 12 | Consent, quiet hours, local time, frequency cap | 23 |
| 13 | Segments and link report | 18 |
| 14 | Deliverability | 15 |
| 15 | A/B testing | 10 |
| 16 | Follow-ups | 8 |
| 17 | Chat routing, status and SLA | 14 |
| 18 | Interactive templates and ad attribution | 20 |
| 19 | Webhooks (signature, SSRF, delivery, batching, replay, failure) and schedule timetable | 34 |
| 20 | Encryption key guard (right key, wrong key, one corrupt row, the real database) | 5 |
| 21 | Option catalogues match the server's rules (every segment field and operator, conditions, metrics, button caps, ranges) | 14 |
| 22 | Round 4: campaign edit, A/B split and decision, managed types and topics, contact fields and CSV import, age segments, queue contract, connection hold, platform SMTP | 44 |

---

## 9. Things only you can do

1. **Change every secret that was ever committed to git:** the Neon database password, JWT key, Groq API key, tracking secret and admin password.
   - Old commits still contain them; the files were only removed from tracking.
   - **Encryption key: be careful.** Changing `Encryption:Key` makes every stored SMTP/IMAP password and WhatsApp token unreadable, so re-enter them all afterwards. The backend now refuses to start with a key that cannot read the stored credentials.
   - Rotate the values in **both** places: user-secrets and `Backend/appsettings.secrets.local.json`.
2. **Production settings:** set `App:PublicBaseUrl` (public address, or tracking links won't work) and `Cors:AllowedOrigins`. The server refuses to start in Production if a secret is missing.
3. **Data residency:** the database is in `us-east-1`. For a bank, host the database and file storage in the required jurisdiction.
4. **Meta:** templates submitted from the app are reviewed by Meta. Real ad attribution needs a live Click-to-WhatsApp ad.
5. **DNS:** fix any SPF/DKIM/DMARC problems the pre-check reports, at your DNS provider.
6. **Connection access:** assign connections to users or roles under Setup › Connection Access. With scoping on, a non-admin with no assignment sees no chats, campaigns or reports.
7. **Map files:** don't publish the `.map` files from `Frontend/dist` to the public web server.
8. **Users will need to sign in again** after deploying (new session system).
9. **Restart your backend** so it runs this round's code. An older build fails on the dropped SES columns and uses the old queue names, so it would take no jobs.
10. **Set the platform SMTP password** (`Smtp__Password`) if you want welcome emails. Until then, system mail is off.
11. **Permanently purging** the soft-deleted contacts and campaigns is up to you: [purge-contacts-campaigns.sql](Backend/scripts/purge-contacts-campaigns.sql).
12. **Commit the work:** nothing is committed. Tell me if you want it on a new branch or split into several commits.

---

## 10. Issues found in your data

- **Resolved: connection 92's SMTP password.** It decrypts fine and the key never changed; see Round 3. The one failed job was discarded at your request, and health is Healthy. No re-entry is needed.
- **User `superadmin@omniconnect.com`** (created 18 Sep) is active but is **not** an administrator and has no role. With scoping on, it sees nothing. Check whether it is expected.
- **No WhatsApp number is connected** in the dev database. So "New Template" and Chat composers are disabled there, and the new-template dialog could not be opened in the browser. Its server side was fully tested.
- **Old test users** (named "P11", "ZZ" and "Perm") are inactive but still in the Users table. They can be deleted.
- **Your own backend and Vite (ports 5155 and 5173) were left alone throughout.** Every check ran against a separate instance of the current code on ports 5199 and 5175, now stopped. At the end of Round 4, nothing was listening on 5155 or 5173. Start the backend fresh so it runs this round's code.
- **An older build shared the job queue** (Round 4 root cause). If you run another copy of the app from an older checkout against the same database, stop it. `/health` now reports Degraded while one is connected.

---

## 11. Future suggestions

### Security and compliance (bank grade)

1. **Single sign-on and MFA:** SAML/OIDC with the bank's identity provider, and TOTP or WebAuthn for local accounts.
2. **Key management:** move `Encryption:Key` into Azure Key Vault or AWS KMS, with envelope encryption and key versioning. Keys can then rotate without making stored credentials unreadable.
3. **Tamper-evident audit log:** hash-chain the audit rows and export them to the bank's SIEM through the new webhooks. This adds an `audit.*` event family.
4. **Data retention policies:** configurable purge or anonymisation of messages, contacts and delivery logs per jurisdiction, plus "right to be forgotten" requests.
5. **Field-level PII protection:** mask phone and email for roles that don't need them, with an audited "reveal" action.
6. **IP allow-listing** for admin actions, and session limits per user.

### Messaging

7. **WhatsApp Flows** (forms inside WhatsApp) and list messages in Chat, beyond reply buttons.
8. **Media-header template creation** by uploading the sample to Meta's resumable upload API. Today these are made in WhatsApp Manager and synced.
9. **SMS as a third channel,** reusing the queue, consent, caps, approval and reporting already built.
10. **Journeys:** a visual multi-step builder on top of the new follow-up rules (wait, branch, send, tag).
11. **Send-time optimisation** from each contact's past open and read times. No AI provider is needed; it's simple statistics.

### Operations and scale

12. **Dead-letter queue page:** view, requeue or discard failed jobs from the UI, including the one found above.
13. **Metrics and tracing:** OpenTelemetry to Prometheus/Grafana for queue depth, send rate, webhook latency and SLA breaches.
14. **Read replica for reporting,** so heavy reports never slow sending.
15. **Horizontal scaling test:** run two backend instances against the same database and confirm that queue, schedule and A/B claims hold. They are designed for it.
16. **Contract tests** for the webhook payloads, and a published receiver guide with sample verification code in C#, Node and Python.

### Product

17. **Campaign calendar view** showing scheduled and recurring sends.
18. **Contact deduplication and merge.**
19. **Team inbox metrics:** agent response times and resolution rates over time, built on the new SLA data.
20. **AI reply suggestions:** you opted out for now. If revisited, use a self-hosted model so no customer data leaves the bank.

---

## 12. How to run and verify

```
# Backend (port 5155)
cd Backend
dotnet run --launch-profile http

# Frontend (port 5173)
cd Frontend
pnpm install
pnpm dev

# Checks
cd Backend && dotnet build -c Release
cd Backend && dotnet ef migrations has-pending-model-changes
cd Frontend && pnpm exec tsc -b && pnpm lint && pnpm build && pnpm audit --prod
cd Tests/EmailChannel.Tests && dotnet run      # backend stopped first
```

**If the backend says secrets are not configured:** it now lists every file it checked, and whether each was found, missing or unreadable. From any terminal, the simplest fix is to copy `Backend/appsettings.secrets.local.example.json` to `Backend/appsettings.secrets.local.json` and fill it in. Always reuse the **same** encryption key; the backend refuses to start with a key that cannot read the stored credentials.
