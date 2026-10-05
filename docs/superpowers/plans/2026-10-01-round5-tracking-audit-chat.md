# Round 5 — Campaign tracking, page fixes, chat, audit log: Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: superpowers:executing-plans (native, recommended — see Execution) or superpowers:subagent-driven-development. Steps use `- [ ]` tracking. On approval, copy this file to `docs/superpowers/plans/2026-10-01-round5-tracking-audit-chat.md`.

**Goal:** Opens, clicks, bounces and unsubscribes count for real Gmail recipients, and A/B by open rate works. Queue/Executed tabs, Dashboard links, Chat and every page work. The 9 test templates are gone. The audit log records every user and system change, and the redundant Setup › Activity Log is removed.

**Architecture:**
- **Tracking:** stays a signed pixel/redirect on the API. We add (a) one source of truth for the public base URL, (b) a reachability probe surfaced in pre-flight and health, (c) atomic event processing, (d) SMTP-time and plain-text bounce detection, (e) a "no signal" A/B rule.
- **Audit:** stays explicit `IAuditService.LogAsync`, with the missing call sites added. The dashboard's Recent Activity reads the audit log, so "View All" shows the same data.

**Tech Stack:** ASP.NET Core .NET 10, EF Core + Npgsql (Neon), MailKit, SignalR; React 19 + TS + Vite; console integration suite `Tests/EmailChannel.Tests`.

**Spec:** the user's Round 5 message (tasks 1–5) and answers:
- Cloudflare quick tunnel for live tests.
- Test mail only to manishmishra8970@gmail.com, plus one bounce probe to a non-existent `@rma.my` mailbox.
- Create a "Link tracking check" email template and keep it.
- Remove Setup › Activity Log completely, with a backup.
- Back up, then delete the 9 test templates.

## Global Constraints
- Nothing hardcoded in the UI. Options, limits and patterns come from `Backend/Services/Catalogs/FeatureCatalogs.cs` via `api/reference/*`. Colours come only from tokens in `Frontend/src/index.css`.
- FluentValidation stays synchronous. Explicit DB transactions go through `Database.CreateExecutionStrategy()`.
- Inspect every generated EF migration for unrelated drift before applying.
- Data removal: back up to `Backend/backups/<date>-<name>/` (git-ignored), with a restore SQL in `Backend/scripts/`, **before** deleting.
- Test emails go only to `manishmishra8970@gmail.com`, plus exactly one bounce probe to `bounce-check-<random>@rma.my`.
- The tunnel exists only while testing. The URL is passed as command-line config to the verify backend and never committed.
- Frontend checks: `pnpm exec tsc -b`, `pnpm lint`, `pnpm build`. Backend: 0 warnings in Debug and Release. Suite: stop backends first; mirror DI changes in `TestHarness`.
- Leave the user's own servers alone. Stop my servers and remove temporary `launch.json` entries at the end. No commits unless asked.

## Root causes found (read-only research, 2026-10-01)

| Symptom | Cause (evidence) |
|---|---|
| Unique opens/clicks always 0; A/B open rate 0% | `appsettings.Development.json:14` hard-codes `Email:Tracking:BaseUrl` to a dead trycloudflare quick-tunnel; otherwise it falls back to `http://localhost:5155`. Gmail's image proxy never reaches the API. DB: 0 `Opened`/`Clicked` rows in `EmailEvents`, only `Sent`. Nothing validates the URL (`StartupConfiguration.cs:151-195`). |
| Links card "No clicks yet" | None of the 5 `EmailTemplates` has an absolute `href`, so nothing is rewritten (`EmailDispatchWorker.cs:877-911`). |
| Unsubscribed never moves | The unsubscribe base is `App:PublicBaseUrl` = localhost in Development. |
| Bounced never moves | SMTP 5xx counts as Failed (`SmtpEmailProvider.cs:150-160`). `ImapBounceDetector` only parses RFC 3464 `multipart/report`. Plain-text Exim/cPanel NDRs are skipped as auto-replies (`ImapPollingWorker.cs:401-424`). |
| A/B "decided" with 0 vs 0 | `AbTestService.cs:189` orders by rate, then index → always A. |
| Queue tab "doesn't open" | `CampaignDetails.tsx:144-146` effect flips `queue` back to `executed` when queued=0 on every tab change. Stale rows and no request guard (102-132). `.tabs-container` CSS loads only with the ActivityLogs chunk. |
| Dashboard View All → 404 | `TopCampaignsCard.tsx:100` navigates to `/campaigns` (route is `/campaigns/campaign`). |
| Recent Activity "View All" shows unrelated data | The card is computed from 4 tables (`DashboardCacheService.cs:717-800`); View All opens the audit tab. |
| Contact create not audited | Contacts 80/82 were re-added after the reset, so they hit the restore-soft-deleted branch (`ContactService.cs:205-263`), which never calls `LogAsync`. Also unaudited: contact notes, agent chat sends, template sync/status changes, system-created contacts, system campaign transitions; `Auth.PasswordChanged` bypasses `AuditService` (`AuthService.cs:199`). |
| Test templates linger | `Phase22_Round4Tests.cs:47-50` inserts `zz_emailtest_r4_*` and never removes them. The harness prefix `zz-emailtest` (hyphen) misses `zz_emailtest` names. |

## Skills used during execution
- Debugging: `superpowers:systematic-debugging` and `diagnosing-bugs` (Task 1).
- UI: `impeccable` (polish) and `web-design-guidelines` (audit) for Chat and the campaign page (Tasks 4, 6).
- Wrap-up: `superpowers:verification-before-completion`, `engineering:code-review` on the diff, `engineering:documentation` for the guide.
- find-skills result: `anthropics/skills@webapp-testing` (official, 167.7K installs) was considered. The built-in browser covers the page sweep, so nothing new is installed.

## Review Focus
1. **Gmail proxy caching:** a second open must not double count, and an open after a click must not double count (the idempotency key `pixel:{id}:unique` is shared). Test in Task 2.
2. **Bounce report without a Message-ID** (some NDRs strip headers): must fall back to recipient address + latest send ≤ 7 days, and must never mark a recipient of another campaign. Test in Task 3.
3. **A/B with zero opens everywhere:** must postpone, then decide with a visible "no signal" reason, never silently pick A. Test in Task 3.
4. **Tab switching during a live send:** counts update via SignalR; a slow response from the old tab must not overwrite the new tab. Browser check in Task 4.
5. **Audit for a re-created (restored) contact, and for system actors:** must appear with actor "System". A non-admin's Recent Activity must show only their own actions. Tests in Task 7.

---

### Task 1: Public base URL — one source of truth plus a reachability probe

**Files:**
- Modify: `Backend/appsettings.Development.json`: remove the `Email:Tracking:BaseUrl` tunnel value and keep the explanatory comment. Tracking and unsubscribe then fall back to `App:PublicBaseUrl` (`StartupConfiguration.cs:151-156`).
- Create: `Backend/Services/Email/PublicEndpointProbe.cs` with `IPublicEndpointProbe`:
  - `Task<PublicEndpointStatus> CheckAsync(CancellationToken)`.
  - `record PublicEndpointStatus(bool Reachable, string BaseUrl, string Reason)`.
- Modify: `Controllers/EmailTrackingController.cs`: add `GET api/t/ping/{nonce}` (`[AllowAnonymous]`). It returns the nonce plus this instance's id, so the probe proves the URL reaches *this* server.
- Modify: `Services/Email/DeliverabilityService.cs` `PrecheckAsync`: add two items.
  - `tracking` (warn): "Opens, clicks and unsubscribes won't be counted: {base} isn't reachable from the internet ({reason})."
  - `links` (pass, informational): "This message has no links, so the Links report will stay empty."
- Modify: `Services/Queue/QueueHealthCheck.cs`, or add `PublicEndpointHealthCheck`: report Degraded with the reason.
- Modify: `Program.cs`:
  - register the probe (singleton, 5-minute cache via `IMemoryCache`, `HttpClient` with a 5 s timeout);
  - add an IP-partitioned rate limit for `/api/t/*` using `RateLimiting:TrackingPermitLimit` (default 600/min). Configurable, so Gmail proxy IP pools aren't throttled.
- Test: `Tests/EmailChannel.Tests/Phase23_Round5Tests.cs` (new; register in `Program.cs`).

**Interfaces:**
- Produces:
  - `IPublicEndpointProbe.CheckAsync`;
  - `PublicEndpointStatus.Reason` ∈ catalog `PublicEndpointCatalog.Reasons`: `empty`, `localhost`, `private-network`, `not-https`, `unreachable`, `wrong-instance`, `ok`.

- [ ] **Step 1: Failing tests (Phase 23 "Public address").** Assert that the classifier returns:
  - `localhost` for `http://localhost:5155`;
  - `private-network` for `http://192.168.1.4`;
  - `not-https` for `http://example.com`;
  - `empty` for `""`.

  The probe against an unreachable host returns `unreachable` within 6 s.
- [ ] **Step 2:** Run `dotnet run -c Release -- 23`. Expect FAIL (type missing).
- [ ] **Step 3:** Implement the classifier: parse the URI, then use `IPAddress.IsLoopback` and the RFC 1918, link-local and CGNAT ranges (reuse the webhook SSRF range check in `Services/Integrations/WebhookCore.cs` URL guard instead of duplicating it). Then implement the HTTP probe with nonce echo, the precheck items and the health check.
- [ ] **Step 4:** Run phase 23 and expect PASS. Run `dotnet build` in Debug and Release: 0 warnings.

### Task 2: Atomic event processing, and tracking end to end in tests

**Files:**
- Modify: `Services/Email/CampaignEmailEventProcessor.cs` `ProcessAsync` (78-187). Run `EmailEventStore.RecordAsync`, `ApplyToRecipientAsync` and `IncrementCounterAsync` in one transaction under the execution strategy, and publish SignalR only after commit. A crash then can't leave an event row that makes later hits look like duplicates.
- Modify: `Tests/EmailChannel.Tests/TestHarness.cs`:
  - set `Email:Tracking:Enabled=true` and a BaseUrl;
  - add a `RecordingAuditService` option, replacing `NoOpAuditService`, for phases that assert audit;
  - make cleanup delete `Templates` named `zz_emailtest%` **created by this run** (track ids in a list; no global sweep).
- Modify: `Phase22_Round4Tests.cs:47-50`: register the created template ids for cleanup in `finally`.
- Test: `Phase23` sections "Pixel and links", "Opens count people", "Links report".

- [ ] **Step 1: Failing tests.** Send one campaign through the real dispatch worker to `FakeSmtpServer`, using a template with 2 absolute links. Then:
  - the captured HTML contains `{base}/api/t/o/` once, and both links are rewritten to `/api/t/c/`;
  - decoding the open token and calling the processor sets `OpenedAt` and `OpenedCount=1`;
  - a second open and a click-implied open leave `OpenedCount=1`, while the click raises `ClickedCount=1`;
  - `GetLinkReportAsync` returns the clicked URL with `UniqueClickers=1`;
  - forcing the recipient update to throw leaves **no** `EmailEvents` row, and a retry then counts.
- [ ] **Step 2:** Run and expect FAIL on the atomicity case.
- [ ] **Step 3:** Implement the transaction wrapper:
  ```csharp
  var strategy = _db.Database.CreateExecutionStrategy();
  await strategy.ExecuteAsync(async () => {
      await using var tx = await _db.Database.BeginTransactionAsync(ct);
      var recorded = await _store.RecordAsync(evt, ct);            // false = duplicate
      if (recorded) { await ApplyToRecipientAsync(evt, ct); await IncrementCounterAsync(evt, ct); }
      await tx.CommitAsync(ct);
      outcome = recorded;
  });
  if (outcome) await PublishAsync(evt, ct);   // after commit only
  ```
- [ ] **Step 4:** Run phases 5, 6, 8, 13, 22, 23. Expect PASS.

### Task 3: Bounces (SMTP 5xx and plain-text NDR) and the A/B "no signal" rule

**Files:**
- Modify: `Services/Email/SmtpEmailProvider.cs:150-160`. An `SmtpCommandException` with `ErrorCode == RecipientNotAccepted` and a 5xx status becomes a permanent **bounce** (`EmailSendRecorder` records `EmailEventKind.Bounced`, `BounceType=Permanent`, with `DiagnosticCode`). Other 5xx errors stay Failed.
- Create: `Services/Email/PlainTextBounceParser.cs` (`static bool TryParse(MimeMessage, out ParsedBounce)`). Patterns live in `FeatureCatalogs.cs` as a new `BounceCatalog`: sender local-parts (`mailer-daemon`, `postmaster`), subject patterns, permanent status regex `\b5\.\d{1,3}\.\d{1,3}\b|\b55\d\b`. It extracts:
  - the failed recipient(s);
  - the returned `Message-ID`;
  - the diagnostic line.
- Modify: `Services/Email/ImapPollingWorker.cs:283-288, 342-394, 401-424`. Try `ImapBounceDetector`, then `PlainTextBounceParser`, **before** the auto-reply skip. Matching order:
  1. Message-ID → `EmailMessageDetails.MessageIdHeader`;
  2. otherwise recipient address + latest `SentAt` ≤ `BounceCatalog.MatchWindowDays` (7) on the same connection.
- Modify: `Services/Campaigns/AbTestService.cs:170-190`. If every variant's hits == 0 and `now < AbDecideAt + AbNoSignalGraceHours`, postpone by `AbRecheckMinutes`. After the grace period, decide A and set the new `Campaign.AbDecisionReason = "no-signal"`; otherwise `"best-rate"` or `"manual"`.
- Modify: `FeatureCatalogs.cs`: add `AbNoSignalGraceHours = new(0, 168, 24)`; serve it in `campaign-options`.
- Modify: `Models/Entities/Campaign.cs` (`AbDecisionReason`, max 20) and the detail DTO. In `CampaignAbResults.tsx`, show "No variant had any {metric}s in time; Variant A was kept" for `no-signal`.
- Test fixtures: `Tests/EmailChannel.Tests/Fixtures/bounce-exim.eml`, `bounce-dsn.eml`, `bounce-no-msgid.eml` (synthetic, `example.test` addresses).

- [ ] **Step 1: Failing tests (Phase 23 "Bounces", "A/B no signal").**
  - Each fixture marks the right recipient Bounced and `BouncedCount=1`.
  - The no-Message-ID fixture matches by address within 7 days and ignores an older send.
  - A RCPT 550 from `FakeSmtpServer` records Bounced, not Failed.
  - Zero-opens A/B postpones inside the grace period, then decides A with reason `no-signal`.
  - B with one open wins with `best-rate`.
- [ ] **Step 2:** Run and expect FAIL.
- [ ] **Step 3:** Implement as specified. Configure `FakeSmtpServer` to reject one RCPT with `550 5.1.1`.
- [ ] **Step 4:** Run phases 15, 22, 23. Expect PASS.

### Task 4: Campaign page — Queue/Executed tabs and live counts

**Files:**
- Modify: `Frontend/src/pages/Campaigns/CampaignDetails.tsx` (76, 102-155, 493-505, 544):
  - Initial tab comes from `?tab=` or, once on first load only, from counts (guarded with a `useRef`). Delete the effect at 144-146.
  - `setRecipients([])` plus a loading skeleton on tab change.
  - `AbortController` per `loadRecipients`; ignore aborted responses.
  - The tab is mirrored to the URL (`useSearchParams`, `replace: true`).
  - Buttons get `role="tablist"`, `role="tab"`, `aria-selected`, `aria-controls`, and arrow-key navigation.
  - On a SignalR `campaignEvent` or status change, debounced (1 s), refetch counts and the current page.
- Modify: `Frontend/src/pages/Campaigns/CampaignDetails.css`: page-scoped `.cd-tabs` styles using tokens. Move the `.tabs-container` base rules from `components/Tabs/Tabs.css` into `styles/components.css`, so they don't depend on chunk load order.
- Modify: `Frontend/src/hooks/useCampaignEvents.ts`: expose an `onEvent` callback.

- [ ] **Step 1:** Implement.
- [ ] **Step 2:** `pnpm exec tsc -b && pnpm lint`.
- [ ] **Step 3: Browser (verify servers).** On a finished campaign:
  - click Queue: it stays on Queue and shows "Nothing waiting";
  - click Executed: it shows the rows; Back/Forward restores the tab.
  - During the live send in Task 9, counts move without reload.

### Task 5: Dashboard and navigation sweep

**Files:**
- Modify: `components/TopCampaignsCard.tsx:100` → `navigate('/campaigns/campaign')`. Hide "View All" without `Campaign.View`.
- Modify: `components/RecentActivityCard.tsx:44`. Hide "View All" without `ActivityLog.View` (Task 7 changes the data source).
- Modify: `components/Header.tsx:66-76`: filter quick-create items by permission (each item gets its permission key).
- Modify: `App.tsx`:
  - 190/194: `?view=true` routes accept `MessageBot.View`/`TemplateBot.View` (route guard takes `anyOf`). The wizards enforce read-only when lacking Edit.
  - 206-207: `/setup` index redirects to the first `setupNav` entry the user may see. Reuse `setupNav.ts` permission keys.
- Modify: `components/Guarded` (wherever `Guarded` lives): support `anyOf: string[]`.

- [ ] **Step 1:** Implement.
- [ ] **Step 2:** `tsc -b`, `lint`.
- [ ] **Step 3: Browser sweep (Task 9).** For every route in `App.tsx`, navigate and assert:
  - no "Page not found", no "Not authorized" for the admin, no error boundary;
  - no console errors, no 4xx/5xx except intended ones.

  Click every Dashboard card link. Produce a page × result matrix for the report.

### Task 6: Chat — fixes, de-duplication and UI polish (impeccable)

**Files:**
- `Frontend/src/pages/Chat/Chat.tsx`
- `Chat.css`
- `ConversationOwnerControls.tsx`
- `components/EmailThreadMessage/EmailThreadMessage.tsx`
- `components/EmailComposer/EmailComposer.tsx`
- `Backend/Services/ChatService.cs` (search)

**Changes:**
1. **One place for each email action.** Keep the per-message Reply / Reply All / Forward (Gmail pattern) and the composer's mode tabs. Remove the duplicate bottom bar (1744-1788). Move **New Email** to the conversation header as a primary button. Replace the per-card ⋮ (which only opened Reply) with a real menu: Reply, Reply All, Forward, Copy message ID.
2. **Bug:** New Email → Reply tab unmounted the composer and lost the draft (1723, 1782). Keep the composer mounted; switching to a reply mode without a source falls back to the last inbound message.
3. **Permissions (security):**
   - gate the email composer by `canSend` (`Chat.Send`);
   - gate "Delete Chat" by `Chat.Delete`;
   - gate notes add/delete by `Contact.Edit`.
   - The backend endpoints are checked to enforce the same permissions; add `[RequiresPermission]` where missing.
4. **Search:** remove the client re-filter (595-611), so server matches on email, subject and message text are shown.
5. **Filters:**
   - Combine All/Unread, Status and Owner into one "Filters" row: labelled compact selects in a 3-column grid, plus an active-filter count and "Clear".
   - Hide the static "All Connections" badge row in All Channels mode.
   - Labels are visible (from the catalog).
6. **Owner select:** the selected value shows the name only; the options show "name · N open". Width is driven by content (`min-width`, ellipsis with a title tooltip).
7. **Header:**
   - remove the header "Initiate Chat" when the 24-hour banner already offers it;
   - the window dot becomes a non-interactive status with a tooltip.
8. **Info drawer:** hide Phone for email-only contacts; fix the "groups" label case; show the real connection name instead of "Connection 1".
9. **Scroll:** replace `scrollIntoView` (534) with container `scrollTop` so the page doesn't jump.
10. **Polish:** `impeccable` polish pass on spacing, alignment and focus states using tokens only, then a `web-design-guidelines` audit. Fix all findings.

- [ ] **Step 1:** Implement 1–9, then polish 10.
- [ ] **Step 2:** `tsc -b`, `lint`, `build`.
- [ ] **Step 3: Live (Task 9).**
  - **New Email** to manishmishra8970@gmail.com: subject "Round 5 chat check — new".
  - **Reply** on the Ashok thread.
  - **Reply All:** Cc is empty because only that address is on the thread. Show the derived Cc.
  - **Forward** to the same address.

  Each must appear in Gmail threading (the user confirms) and in the thread, with an `EmailReply.Sent` audit row. At 1280, 1024 and 375 px, Send is visible and nothing overflows.

### Task 7: Audit log — complete coverage; Recent Activity from the audit log

**Files (add `LogAsync` calls; actor "System" when there is no user):**
- `Services/ContactService.cs:205-263`: restore branch → `Contact.Created` with metadata `{ "restoredFromDeleted": true }`.
- `Controllers/ContactsController.cs:310, 330`: `ContactNote.Created` / `ContactNote.Deleted`.
- `Services/ChatService.cs:584, 712`: `Chat.MessageSent` / `Chat.TemplateSent` (agent sends only; inbound is not audited).
- `Services/TemplateService.cs:241-383`: `Template.Synced` (summary: added, updated, removed), `Template.StatusChanged` (system), `Template.RemovedBySync`.
- `Services/WhatsAppCloudApiService.cs:806` and `Services/Email/InboundEmailThreader.cs:302`: `Contact.Created` (system, metadata `source`).
- `Services/CampaignService.cs:2030` (CSV-created contacts): one `Contact.Imported` summary with the count.
- `Services/Email/EmailDispatchWorker.cs` `HoldCampaignAsync`: `Campaign.Held` (system, reason). `CampaignFinalizer`: `Campaign.Completed` / `Campaign.PartiallyFailed` (system, counts).
- `Services/AuthService.cs:199`: route through `IAuditService` (`Auth.PasswordChanged`) so Module, Action and Status are set.
- `Services/AuditService.cs`: system-actor support (`UserName = "System"`, `UserId = null`) when there is no HttpContext user.
- `Services/DashboardCacheService.cs:717-800`: Recent Activity = the latest 8 `AuditLogs` rows.
  - Modules: Campaign, Contact, Template, Segment, Chat, EmailReply, BotFlow, MessageBot, TemplateBot, Connection, EmailConnection.
  - Excludes Auth and `*.AccessDenied`/`*.Failed`.
  - Without `ActivityLog.View`, only rows where `UserId == current user`.
  - The cache key includes the scope; invalidated by the debounced `dashboardChanged`.
- `Frontend/src/components/RecentActivityCard.tsx`: render the audit `Event` text with the actor and relative time. View All → `/audit-log?tab=audits&module=…`.
- Migration indexes: `IX_AuditLogs_Module_CreatedAt`, `IX_AuditLogs_EntityType_EntityId`, `IX_AuditLogs_Status`.

- [ ] **Step 1: Failing tests (Phase 23 "Audit", using `RecordingAuditService`).**
  - Re-creating a soft-deleted contact writes `Contact.Created`.
  - A note create writes `ContactNote.Created`.
  - A chat send writes `Chat.MessageSent`.
  - A campaign hold writes `Campaign.Held` with actor System.
  - Recent Activity for a non-admin contains only their rows.
- [ ] **Step 2:** Run and expect FAIL.
- [ ] **Step 3:** Implement.
- [ ] **Step 4:** Run phases 10, 11, 12, 17, 22, 23. Expect PASS.

### Task 8: Remove Setup › Activity Log (MessageActivityLogs); template delete safety; delete the test templates

**Removal checklist (nothing left behind):**
- **Backend:**
  - Delete `Models/Entities/MessageActivityLog.cs` and `Controllers/MessageActivityController.cs`.
  - In `WhatsAppCloudApiService.cs:272-311`: remove `RecordMessageActivityAsync` and its calls.
  - In `EmailSendRecorder.cs:292, 343`: remove the writes.
  - `AppDbContext.cs:308-315`: remove the DbSet and mapping.
  - `AuditChangeCapture.cs:40`: remove the exclusion.
  - Correct the doc comments in `AuditLog.cs:10` and `LoginAttempt.cs`.
  - Remove the reference in `ActivityController.cs`.
  - Remove `ActivityLog.Delete`/`ActivityLog.Clear` from `Data/Seed/PermissionCatalog.cs:174-177` and `RoleSeed.cs:27`. Keep `ActivityLog.View`, relabelled "Audit log: view".
  - `scripts/purge-contacts-campaigns.sql:54`: remove the line.
- **Frontend:**
  - Delete `pages/Setup/ActivityLog/`.
  - Remove its route in `App.tsx:221`, the entry in `setupNav.ts:53`, its styles in `setup.css`, and the mention in `SystemLogs.tsx`.
  - Rename the sidebar "Activity Logs" → **"Audit Log"**, with the route `/audit-log` and a redirect from `/activity-logs`.
  - Update `Sidebar.tsx`, `RecentActivityCard.tsx`, and the `zustand.ts`/`activityLogService.ts` names.
- **Tests:** remove the `MessageActivityLogs` checks in `Phase5_PipelineTests.cs:320-328` and the harness cleanup line `TestHarness.cs:266`.
- **Migration `Round5AuditTrackingCleanup`:**
  - drop `MessageActivityLogs`;
  - delete the `ActivityLog.Delete`/`Clear` permission rows from RolePermissions, UserPermissions and Permissions;
  - add `Campaigns.AbDecisionReason`;
  - Campaign→Template FK `Cascade` → `Restrict`;
  - the AuditLogs indexes from Task 7.

**Template delete safety:**
- `Services/TemplateService.cs:218-239`:
  - check campaigns (`IgnoreQueryFilters`), `CampaignVariants`, `TemplateBots` and `FollowUpRules`;
  - return 409 "Used by N campaign(s)/bot(s) — remove those first" instead of a 500, and instead of silently cascading soft-deleted campaigns.

**Steps:**
- [ ] **Step 1:** Back up `MessageActivityLogs` (97 rows) and the 9 `zz_emailtest_r4_*` `Templates` rows, plus their `TemplateVariables`, to `Backend/backups/2026-10-01-round5/*.json`. Write `Backend/scripts/restore-round5-2026-10-01.sql`. Verify the row counts match.
- [ ] **Step 2:** Failing test: deleting a template referenced by a variant returns a conflict, not an exception.
- [ ] **Step 3:** Implement the removal and the delete safety.
- [ ] **Step 4:** Generate the migration, inspect for drift, apply.
- [ ] **Step 5:** `dotnet ef migrations has-pending-model-changes` reports none.
- [ ] **Step 6:** Delete the 9 test templates through `DELETE api/Templates/{id}` on the verify backend (each audited as `Template.Deleted`). Confirm 0 `zz_emailtest%` rows remain and the dashboard's "Template … was approved" entries disappear.
- [ ] **Step 7:** Grep gate: `MessageActivit|activity-log|ActivityLog\.(Delete|Clear)` returns no hits outside Migrations and backups.

### Task 9: Live end-to-end verification (tunnel, campaign, chat, bounce, connections, page sweep)

- [ ] **Step 1:** Install cloudflared with `winget install --id Cloudflare.cloudflared -e` (approved). Add `backend-verify` (5199, Release) and `frontend-verify` (5175) to `.claude/launch.json`. Start `cloudflared tunnel --url http://localhost:5199` in the background and read the https URL from its log. Restart backend-verify with `--App:PublicBaseUrl=<tunnel>`.
- [ ] **Step 2:** Pre-flight shows `tracking` pass; `/health` is Healthy. With the tunnel stopped, pre-flight shows the warning (negative check).
- [ ] **Step 3:** Create the email template **"Link tracking check"** (non-system). Keep it. It contains:
  - 2 literal absolute links: `https://rma.my` and `https://rma.my/?utm_source=omniconnect`;
  - an unsubscribe link built from `{{unsubscribe_url}}`.
- [ ] **Step 4:** Create a campaign "Round 5 tracking check" to contact 80 (manishmishra8970@gmail.com) with A/B (A: Link tracking check, B: Welcome Email) and the test share at 100%. Send. Watch Recipients and Accepted update live.
- [ ] **Step 5:** Fetch the open-pixel URL through the public tunnel from this machine (the same path Gmail's proxy uses). Then ask the user to open the email in Gmail and click a link. Expect:
  - Unique Opens = 1 and Unique Clicks = 1, live, no reload;
  - the A/B table shows the rates;
  - the Links card lists the URL;
  - a second open doesn't double count.
- [ ] **Step 6:** Unsubscribe from the email. Use Gmail's one-click button if Gmail shows it (low-volume senders often don't get it); otherwise use the template's unsubscribe link. Expect Unsubscribed = 1, consent opted out, suppression recorded. Then restore the contact's consent through the Consent tab, which is audited.
- [ ] **Step 7:** Bounce probe: a campaign "Round 5 bounce check" to a temporary contact `bounce-check-<random>@rma.my`. Expect Bounced = 1, from either an SMTP 550 or a parsed NDR via IMAP within one poll cycle. Then soft-delete the temporary contact.
- [ ] **Step 8:** Chat checks (Task 6, Step 3). Each mode is verified in the thread and in the audit log.
- [ ] **Step 9:** Connections: on Connections, run Test for SMTP and IMAP on "xyz", Domain health, and Test email (to manishmishra8970@gmail.com). The WhatsApp connection list and Connect WABA page load; with no WABA connected, check the Test/Sync buttons fail gracefully with a clear message.
- [ ] **Step 10:** Page sweep (Task 5, Step 3) at 1440 px and 375 px. Then the audit page: filter by module (Contact, Campaign, Chat) and check that every action from Steps 3–9 appears.
- [ ] **Step 11:** Stop cloudflared and both verify servers. Remove the `launch.json` entries. Reset the viewport.

### Task 10: Dead code, final gates, docs

- [ ] **Step 1: Dead code.**
  - Frontend: `pnpm dlx knip --reporter compact` for unused files, exports and dependencies. Remove what is confirmed unused, and re-check each removal against dynamic imports and `iconRegistry`.
  - Backend: `dotnet build -warnaserror` plus a grep for unreferenced services and controllers (e.g. `LocalTestController` stays only if Development-gated). Remove confirmed-dead code.
- [ ] **Step 2: Gates.**
  - Backend Debug and Release: 0 warnings; no pending model changes.
  - Frontend: `tsc -b`, `lint`, `build`, `pnpm audit --prod`.
  - Full suite: everything passes.
  - Grep gate: no hex or inline styles in touched files.
- [ ] **Step 3:** `engineering:code-review` on the whole diff; fix the findings.
- [ ] **Step 4: Docs.**
  - Update `FEATURE_GUIDE.md` (tracking and public URL, bounces, A/B no-signal, audit log coverage table, Recent Activity, chat actions, removed Activity Log).
  - Add a Round 5 section to `WORK_SUMMARY.md`, including a **"Missing / future"** list for the user's request: Apple MPP machine-open detection, Redis SignalR backplane, audit retention and partitioning, `pg_trgm` audit search, a dead-letter UI, WhatsApp test number in dev.
  - Update memory (tracking base URL lesson).
- [ ] **Step 5: Final report.**
  - Root causes and fixes.
  - The page sweep matrix.
  - The chat feature verdict (keep, merge or remove for each control, with a reason).
  - Activity log vs audit log explanation.
  - The backup locations.
  - Ask about committing.

## Execution
- **Native**, recommended. Tasks 4–9 share the same verify servers, the tunnel and live data in one session.
- The live checks need the user once: opening the test email and clicking a link in Gmail (Task 9, Step 5).

## Verification summary
- **Automated:** Phase 23 covers the public address, the pixel and links path, atomicity, bounces, A/B no-signal and audit. Plus the full suite.
- **Live (through the tunnel):** opens, clicks, unsubscribe and bounce counters move in real time; A/B rates and the Links card fill; all four chat modes deliver to Gmail; every route loads without errors; every action appears in the Audit Log; there are 0 test templates and no Setup › Activity Log.
