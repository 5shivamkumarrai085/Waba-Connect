# Waba-Connect — feature guide

This guide explains how every feature works: what it does, how to use it, what happens behind the scenes, where its settings live, and where the code and tests are. It covers all four rounds of work.

- **For the change history** (what was fixed, when and why), see [WORK_SUMMARY.md](WORK_SUMMARY.md).
- **For running the app**, see [How to run and check everything](#18-how-to-run-and-check-everything).

**Two rules hold across the whole app:**
- **Nothing hardcoded.** Every option list, limit and required field comes from the server (`GET api/reference/*`, backed by [FeatureCatalogs.cs](Backend/Services/Catalogs/FeatureCatalogs.cs)). The server validates against the same catalog. Every UI colour is a design token in [index.css](Frontend/src/index.css).
- **Work that can fail runs on a durable queue.** Sending, retries, webhooks and follow-ups are queued in Postgres and resume after a restart without sending twice.

---

## Contents

1. [How the system fits together](#1-how-the-system-fits-together)
2. [Dashboard (live)](#2-dashboard-live)
3. [Contacts and required fields](#3-contacts-and-required-fields)
4. [Segments](#4-segments)
5. [Campaigns: create, send, edit](#5-campaigns-create-send-edit)
6. [A/B testing](#6-ab-testing)
7. [Follow-ups](#7-follow-ups)
8. [Pre-flight check and proof sends](#8-pre-flight-check-and-proof-sends)
9. [Consent, consent topics and compliance](#9-consent-consent-topics-and-compliance)
10. [Campaign details, approval and retry](#10-campaign-details-approval-and-retry)
11. [Chat (unified inbox)](#11-chat-unified-inbox)
12. [Connections: email (SMTP + IMAP) and WhatsApp](#12-connections-email-smtp--imap-and-whatsapp)
13. [Platform email: `IEmailSender` (replaces Amazon SES)](#13-platform-email-iemailsender-replaces-amazon-ses)
14. [Templates, webhooks, reports](#14-templates-webhooks-reports)
15. [Security: credentials, key guard, access scoping](#15-security-credentials-key-guard-access-scoping)
16. [The job queue and its contract version](#16-the-job-queue-and-its-contract-version)
17. [Scaling to millions of users](#17-scaling-to-millions-of-users)
18. [Tracking: opens, clicks, unsubscribes, bounces](#18-tracking-opens-clicks-unsubscribes-bounces)
19. [Audit log](#19-audit-log)
20. [How to run and check everything](#20-how-to-run-and-check-everything)

---

## 1. How the system fits together

```
 Browser (React 19 + Vite)
   │  REST  /api/*            SignalR  /hubs/campaign   (live counters, dashboard, chat)
   ▼
 ASP.NET Core (.NET 10) ── controllers → services → EF Core ──► PostgreSQL (Neon)
   │                                                              ▲
   │  hosted workers (same process, any number of instances)      │
   ├─ CampaignExpansionWorker   campaign → one job per recipient ─┤  JobQueue table
   ├─ EmailDispatchWorker       sends via each connection's SMTP ─┤  FOR UPDATE SKIP LOCKED
   ├─ WhatsApp dispatch         sends via Meta Cloud API ─────────┤
   ├─ AbTestWinnerWorker, FollowUpWorker, ChatSlaWorker            │
   ├─ Webhook delivery, report schedule runner                    │
   └─ IMAP poller               replies → Chat ───────────────────┘
```

- **Controllers** stay thin. The rules live in services (`Backend/Services/**`).
- **Every list** the UI offers comes from a catalog or a lookup table (statuses, sources, types, consent topics) that administrators edit under Setup.
- **Real-time updates** go through one SignalR hub, [CampaignHub.cs](Backend/Hubs/CampaignHub.cs). Clients join groups (per campaign, per connection, `dashboard`), so each message goes only to the people who can see it.

---

## 2. Dashboard (live)

**What you see:**
- One row of KPI cards: messages, delivered, contacts, campaigns, and more.
- Charts, **Top Campaigns** (View All opens the campaign list) and **Recent Activity**.
- **Recent Activity is the audit log:** the latest business events (campaigns, contacts, templates, chat replies, segments, bots, connections) with who did them, a person or "System". Administrators and unscoped users see everyone's; connection-scoped users see their own. **View All** opens the Audit Log.
- A **Live · updated HH:MM** pill in the header.

**How it stays live:**
1. Whenever a counter changes, the server raises a `dashboardChanged` signal. That happens on a message sent or delivered, an email event, an inbound message, or a contact created, edited, deleted or toggled.
2. [DashboardNotifier.cs](Backend/Services/Realtime/DashboardNotifier.cs) **debounces** it: at most one signal per `Dashboard:LiveUpdateIntervalSeconds` (default 3 s). A 100 000-recipient campaign therefore causes a few refreshes, not 100 000.
3. The signal goes only to the SignalR group `dashboard`, which users with `Dashboard.View` join.
4. The browser refetches the summary. The server caches it per access scope ([DashboardCacheService.cs](Backend/Services/DashboardCacheService.cs)), so many viewers cost one query.
5. **Fallbacks:**
   - Polling every 2 minutes if SignalR is down.
   - An immediate refresh when the tab becomes visible again.
   - Polling pauses while the tab is hidden.
   - A failed load shows an error banner with **Try again**, never silent zeros.

**Layout:** the KPI strip is a CSS grid with `grid-auto-flow: column`, so the number of columns follows the number of cards. It folds to 4, 3, 2 or 1 column only on narrower screens.

**Code:** [Dashboard.tsx](Frontend/src/pages/Dashboard.tsx), [dashboardStore.ts](Frontend/src/store/dashboardStore.ts), [StatCard.tsx](Frontend/src/components/StatCard.tsx), [components.css](Frontend/src/styles/components.css) (`.stat-grid`).

---

## 3. Contacts and required fields

**Which fields are required:**
- **Always required:** name, phone (E.164, e.g. `+919499373415`), type, status, source.
- **Required by default, changeable:** email and date of birth.
- An administrator picks which other fields are required under **OmniConnect Settings › Contacts › Required contact fields**. Options include company, website, city, state, country, time zone, zip code, address, description and assigned to.

**How validation works (one set of rules, three places):**

| Where | How it gets the rules |
|---|---|
| Add/Edit Contact form | `GET api/reference/contact-fields` returns every field with its kind, max length, and `required` flag, plus the age range and phone pattern. The form marks required fields and shows the same messages as the server. |
| API (`POST/PUT api/Contacts`) | [ContactValidators.cs](Backend/Validators/ContactValidators.cs) reads the same setting through `ContactLookupValidatorCache` (cached 60 s; saving the setting clears the cache at once). |
| CSV import | [CsvContactColumns.cs](Backend/Helpers/CsvContactColumns.cs) applies the same rules to each row. |

**Checks applied:**
- Email format.
- Website must be a valid address.
- Date of birth may not be in the future, and the age must be 0–120 (`ContactFieldCatalog.AgeYears`).
- Lengths come from the catalog.
- Type, status and source must exist in the lookups administrators manage under Setup.

**Age:** contacts store `DateOfBirth`. The age is computed when read, so it is never stale, and it appears on the contact and in segments.

**CSV import (Contacts › Import):**
- **Download Sample** and the column table in the dialog come from `GET api/Contacts/csv-layout`. A field made required shows up as a required column immediately.
- Optional fields are read from any column named after them: `date_of_birth`, `dateOfBirth` or `Date of birth` all work.
- Dates must be `YYYY-MM-DD`. Day and month order is ambiguous, and guessing would silently swap them.
- **Import is partial by design:** valid rows are saved. Each invalid row comes back with its row number, column and reason. Duplicate phones are skipped and counted.
- A file missing a required column is refused as a whole, and the message names the missing columns.

**Exempt:** contacts created automatically by an incoming WhatsApp message or email. They arrive with only a phone or address.

**Settings:** `contacts.requiredFields` (OmniSettings). Catalog: `ContactFieldCatalog` in [FeatureCatalogs.cs](Backend/Services/Catalogs/FeatureCatalogs.cs).
**Tests:** Phase 22, "Required contact fields and age" and "Contacts CSV import follows the same field rules".

---

## 4. Segments

**What it is:** a saved, dynamic audience, such as "Leads in Pune aged 30+ who clicked in the last 30 days and have marketing consent". It is **re-resolved every time a campaign sends**, so it is always current.

**Using the editor (Contacts › Segments › New):**
- **Details:** name and description.
- **Rules:** choose **All** (AND) or **Any** (OR). Each rule is field → operator → value. The rows are joined by *Where / And / Or* connectors.
- **Fields:** contact fields, tags, groups, consent, engagement (opened, clicked or replied within N days), ad source, and **age** (at least / at most / not set).
- **Live preview:** the matching count and a sample of contacts update as you type (debounced).

**How it works:**
- Rules are JSON, and [SegmentQueryBuilder.cs](Backend/Services/Segments/SegmentQueryBuilder.cs) turns them into an EF Core query.
- Only whitelisted fields and operators are accepted, and there is no raw SQL, so rules cannot inject anything.
- **Age rules become date comparisons** on an indexed column: "age ≥ N" means `DateOfBirth ≤ today − N years`. The database never computes an age per row.
- Fields, operators, the day range and the age range come from `GET api/reference/segment-fields`.

**Code:** [SegmentEditor.tsx](Frontend/src/pages/Segments/SegmentEditor.tsx), [SegmentService.cs](Backend/Services/Segments/SegmentService.cs). **Tests:** Phases 13, 21, 22.

---

## 5. Campaigns: create, send, edit

**The wizard (Campaigns › New):**

| Step | What you set |
|---|---|
| Basic info | Name, channel, connection, sender and reply-to (email), template, relation types (from Setup › Type), A/B test |
| Contact selection | Segments, groups, or individual contacts in a paged picker with per-type counts. **"Send to every contact of these types"** targets all of them without listing them in the browser |
| Scheduling | Now, later, or 09:30 in each recipient's local time. (The consent-topic and transactional controls were removed from the wizard on 2026-10-05; the server still supports both. New campaigns use the default topic and count as marketing; existing campaigns keep their stored values.) |
| Follow-ups | Rules that act after the send ([§7](#7-follow-ups)) |
| Pre-flight | Server checks, and proof sends ([§8](#8-pre-flight-check-and-proof-sends)) |

**Relation types:**
- Every selected type (e.g. Lead, Customer, Vendor) is stored, and the next step shows a chip and count per type.
- Types come from the managed list, not a fixed set.
- The server refuses a type that doesn't exist and stores the canonical spelling.

**What happens when you send:**
1. `CampaignService.CreateAsync` validates everything, including A/B, follow-ups, topic and pre-flight, **before** saving anything. An invalid setting leaves no half-created campaign.
2. `CampaignExpansionWorker` resolves the audience, applies consent, quiet hours and frequency caps (excluded people become **Skipped** with a reason), and queues one job per recipient.
3. The dispatch workers lease jobs (`FOR UPDATE SKIP LOCKED`), respect each connection's send rate, send, and record the outcome.
4. Counters are pushed live over SignalR. A sweep every minute recomputes them from the recipient rows, so they cannot drift.

**Editing a campaign:**
- Recipients are replaced with a set-based delete and insert inside a transaction, under the database's retry strategy.
- Recipient rows are no longer loaded into memory. That load is what caused the old **500 on Save Changes**: EF refused to load two collections in one query.
- Pre-flight runs again on save.

**If the email connection is unusable** (inactive, or its password can't be decrypted):
- The campaign is put **on hold** (Paused) with the reason shown on its page.
- Recipients stay **Pending**. They are **not** marked Failed or Bounced.
- Fix the connection, then press **Resume**.

**Code:** [CampaignWizard.tsx](Frontend/src/pages/Campaigns/CampaignWizard.tsx), [AudienceContactPicker.tsx](Frontend/src/pages/Campaigns/AudienceContactPicker.tsx), [CampaignService.cs](Backend/Services/CampaignService.cs), [EmailDispatchWorker.cs](Backend/Services/Email/EmailDispatchWorker.cs). **Tests:** Phases 5, 7, 22.

---

## 6. A/B testing

**What it is:** send two to five versions (A plus up to 4 more) to a test share of the audience. The best-performing version is picked automatically and sent to everyone else.

**Setting it up (wizard › Basic info › A/B test):**
- **Variant A** is the campaign's own template or subject, shown read-only. Add B, C and so on with a different template (WhatsApp) or subject (email).
- **Test share:** 10–100 %, default 20 %. The split bar shows exactly how many people get each variant and how many wait for the winner. It uses the same arithmetic as the server.
- **Winner metric:**
  - Email: open, click or reply rate.
  - WhatsApp: read or reply rate.
- **Decide after:** 1–168 hours, default 4. **Counted from the first test send**, not from when you pressed Save, so approval or scheduling delays don't shorten the test.

**How the split works ([AbTestService.cs](Backend/Services/Campaigns/AbTestService.cs)):**
1. The recipients are ordered by a hash of their id, which is stable and well mixed.
2. The first `ceil(total × share)` people become the test group, with at least one per variant.
3. They are dealt round-robin: A, B, C, A, B, C, … so every variant gets an equal share, ±1.
   - The old modulo formula always gave three-variant tests to A, because its multiplier was divisible by 3.
4. Everyone else is **held** until the winner is known.

**How the winner is picked:**
- `AbTestWinnerWorker` checks due campaigns. If any variant has sent fewer than `AbMinimumSentPerVariant` messages, the decision is **postponed** by `AbRecheckMinutes` (30), rather than picking a winner from no data.
- **No signal:** if no variant has a single open (or read, click, reply), 0 % against 0 % is not a result. The test keeps waiting, up to `AbNoSignalGraceHours` (24) past the planned decision time, and only then keeps variant A. The campaign page says "No signal: no variant had any engagement in time, so variant A was kept".
- Otherwise the best rate wins, and the held recipients are released with the winning variant.
- **Pick winner now** on the campaign page does this by hand.
- **Why the reason is stored:** each decision records `best-rate`, `no-signal` or `manual` (`Campaign.AbDecisionReason`); the labels come from `campaign-options.abDecisionReasons`.
- **When everyone is in the test** (test share 100 %, or a tiny audience), there is no one left to send a winner to. The page then says the figures are the final results instead of showing a decision time in the past.
- **Open rate only works when tracking works.** It counts recipients whose open pixel reached the server, see [§18](#18-tracking-opens-clicks-unsubscribes-bounces).

**Tests:** Phase 15, and Phase 22 ("Three variants split exactly evenly", "No data, no winner", "No signal: nobody read any variant").

---

## 7. Follow-ups

**What it is:** automatic next steps after a campaign. For example: *"48 hours after sending, anyone who **did not open** gets a reminder email"*, or *"anyone who **replied** gets the tag `interested`"*.

**Setting it up (wizard › Follow-ups):** up to 5 rules. Each rule is:
- **Condition:**
  - Email: did not open, did not click, clicked, replied, did not reply, could not be sent to.
  - WhatsApp: did not read, replied, did not reply, could not be sent to.
- **Delay:** 1–720 hours.
- **Action:** send a follow-up message (on either channel, with its own template and connection) or add a tag.

**How it works ([FollowUpService.cs](Backend/Services/Campaigns/FollowUpService.cs)):**
1. The rules are validated **before** the campaign is saved (condition allowed for the channel, connection usable, template exists).
2. Each rule gets a `DueAt` time.
3. `FollowUpWorker` picks up due rules and selects the recipients matching the condition.
4. For a send action, it creates a **child campaign** that goes through the normal pipeline: approval, consent, caps and quiet hours all still apply. A tag action applies the tag directly.
5. Deleting a campaign cancels its pending follow-ups.

**Tests:** Phase 16.

---

## 8. Pre-flight check and proof sends

**What it is:** a check before a campaign is sent, so problems are caught before thousands of people receive a broken or spam-flagged message.

**What it checks ([DeliverabilityService.cs](Backend/Services/Email/DeliverabilityService.cs)):**
- **Domain authentication (DNS):**
  - **SPF:** does the domain allow your SMTP server to send for it?
  - **DKIM:** are messages signed?
  - **DMARC:** is a policy published?
  - **MX:** can the domain receive mail?
  - Results are cached for 10 minutes.
- **Content:** spam phrases, image-to-text ratio, link shorteners, missing unsubscribe link, Gmail clipping size, and unfilled merge fields such as `{{first_name}}`.

**How it's enforced:**
- The wizard's Pre-flight step shows a summary count, problems first, and passed checks folded away.
- **The server runs the check again on create and on edit.** A failure blocks the send unless an **administrator** ticks the override, and the override is audited. Skipping the UI does not skip the check.

**Proof sends:** send the real message to up to 5 addresses before launching.

**Tests:** Phase 14.

---

## 9. Consent, consent topics and compliance

**Consent:**
- Each contact has a consent state **per channel and per topic**, with an append-only history.
- **Recorded from:**
  - unsubscribe links;
  - the public preference centre;
  - WhatsApp keywords `STOP`/`START` (configurable, with configurable replies);
  - agents, on the contact's Consent tab.

**Consent topic:** what the campaign is *about*, e.g. `marketing`, `newsletter`, `offers`.
- The topics are configured under **OmniConnect Settings › Compliance › Topics** (`compliance.topics`). The wizard lists only those, and the server refuses any other.
- When the campaign sends, anyone who opted out of **that topic** on that channel (or of everything) is **Skipped** with the reason "no consent". Someone who opted out of `offers` can still get `newsletter`.

**Other rules applied at send time ([ComplianceGuard.cs](Backend/Services/Compliance/ComplianceGuard.cs)):**
- **Quiet hours:** sends inside the window are postponed, not dropped.
- **Frequency cap:** at most N marketing messages per contact per N days, across channels.
- **WhatsApp opt-in:** can be required for marketing.
- **Transactional** campaigns (receipts, alerts) are exempt from caps and quiet hours, but still respect a full opt-out.

**Tests:** Phases 12 and 22.

---

## 10. Campaign details, approval and retry

**Campaign page:**
- **KPIs:** Sent, Delivered, Opened, Clicked, Replied, **Bounced** (orange: the receiving server rejected it) and **Failed** (red: it could never be handed over), each with its reasons.
- **Also on the page:** Skipped with reasons, the queue, executed messages, the link report, A/B results and follow-ups.
- **On-hold banner:** when a connection problem paused the campaign, the reason is shown here.
- **Pending** excludes Skipped, and connection problems are no longer counted as bounces.
- **Queue / Executed:** two tabs over the recipients (paged and searched on the server). A finished campaign opens on Executed; after that, the tab you click stays selected (it used to jump back to Executed), and it is kept in the address (`?tab=queue`) so a link or Back returns to it. Arrow keys move between the tabs.
- **Live:** KPI counters move with every event. The recipient lists, the A/B table and the Links card refresh too, at most every 2 seconds and only for events that change them, so a large send does not make every open viewer re-read everything.

**Maker-checker approval:**
- With `Campaigns:Approval:Required = true`, a campaign waits in **Awaiting Approval** until a *different* user approves it.
- Rejecting requires a reason.
- Editing an approved campaign sends it back for approval.

**Retry failed (N):**
- Re-sends only recipients that failed.
- Permanent bounces, complaints and suppressions are never retried.
- The maximum number of runs is `Campaigns:Retry:MaxRuns` (3).

**Code:** [CampaignDetails.tsx](Frontend/src/pages/Campaigns/CampaignDetails.tsx), [InternalApprovalGate.cs](Backend/Services/Campaigns/InternalApprovalGate.cs), [CampaignFinalizer.cs](Backend/Services/Email/CampaignFinalizer.cs). **Tests:** Phases 10, 11.

---

## 11. Chat (unified inbox)

**What it is:** WhatsApp and email conversations in one three-pane inbox — **list | conversation | details** — the layout help desks such as Zendesk, Front and Intercom use. New messages arrive in real time; conversations and messages load page by page.

**Page header:** the **Channel** menu (All Channels, WhatsApp, Email; planned channels listed as "Soon") and **New Email**. New Email is enabled when an email conversation is open, and writes a brand-new thread to that contact.

**List (left):**
- **Active Connection** and **From** (the sender address or WhatsApp line), with a **refresh** button. In All Channels mode there is no connection picker; refresh sits beside the search.
- **Search** is done by the server across name, phone, email, subject and message text.
- **Filters** button (with a count when any apply): Status, Owner and Rows per page; **Clear filters** resets them.
- **Tabs with counts — All / Unread / Mine.** Each tab is a server-defined combination of the read and owner filters (`ChatCatalog.QuickViews`, served in `api/reference/chat-options`). Counts come from `GET api/Chat/conversations/counts` in one grouped query, under the same visibility, channel, connection, status and search as the list — so a count always matches what the tab shows.
- **Rows:** avatar with a channel marker, name, time; last message and unread count; then status (Needs reply / Waiting on customer / Resolved), contact type, SLA chip and owner.
- **Footer:** "1–8 of 12" (the total comes from the active tab's count), previous/next, and **Newest / Oldest** sort (`ChatCatalog.SortOrders`). Both orders use keyset paging on (last activity, id), so deep pages stay one index scan.

**Conversation (middle):**
- **Header:** contact name, type badge, email or phone with a **copy** button; **Assigned to** (agents with their open-conversation counts; read-only without `Chat.Assign`); search messages; show/hide details; **⋮ menu** — Mark as resolved / Reopen, New email, Send template (WhatsApp), Delete conversation (`Chat.Delete`).
- **Subject bar:** the subject being discussed (latest email), status, channel, SLA chip, and for WhatsApp the 24-hour reply window ("Reply window: 5h 12m left" / "Reply window closed"); the date of the last activity on the right.
- **Thread:** WhatsApp bubbles or email cards. Each email card has Reply, Reply All and Forward.
- **Email composer opens on demand:** from a message's Reply / Reply All / Forward, or New Email (page header or ⋮). Until then the thread has the whole pane. Its header names the mode and has a **✕**; ✕ or Escape closes it, asking "Discard this draft?" first if anything is written or attached. Replies fold the recipients into one line (**Edit recipients** to change them); Forward and New Email open the fields. It takes at most 60% of the pane and keeps the message being answered in view; Send stays pinned.

**Details (right):** open by default where it fits beside the conversation (otherwise it overlays it); the choice is remembered in the browser. Three tabs:
- **Customer** — email and phone (copy), company, website, location, time zone, source, customer since, description, tags, groups (in their colours) and **notes** (add/delete with `Contact.Edit`, delete asks first). Read from `GET api/Contacts/{id}`; without `Contact.View` the panel says so and shows what the conversation carries.
- **Conversation** — status and its meaning, owner, channel, connection, unread, first-reply due, resolve-by, last activity.
- **Activity** (needs `ActivityLog.View`) — the audit history of this contact and this conversation (`GET api/Activity/audit-logs?entity=Contact:{id}&entity=ChatConversation:{id}`), newest first, with "Show older activity" and a link to the Audit Log. Notes, chat sends, email replies, assignments and status changes are all filed under the contact or the conversation, so they appear here.
- **Quick actions:** Edit contact, New email / Send template, Mark as resolved / Reopen.

**Responsive:** the panes size to the inbox's own width (CSS container queries), so the app sidebar being open or collapsed is accounted for. From 1280px of inbox width: list 360px, details 320px; 1000–1279px: 300px / 288px; below 1000px the details overlay the conversation; below 820px one pane shows at a time, with a back button. A narrow conversation pane moves its actions onto a second row.

**Operations:**
- **Status:** Open, Pending, Resolved or Closed. A new message reopens a conversation.
- **Assignment:** round-robin or least-busy, only to agents allowed on that connection.
- **SLA:** a "Reply by" chip that turns red when the first-response target is missed.
- **Auto-close:** after N hours.
- **Canned replies:** `/shortcut` with `{{first_name}}` variables.
- **WhatsApp reply buttons:** up to 3.

**The four email actions:**

| Action | To / Cc | Subject | Thread | Use it when |
|---|---|---|---|---|
| **Reply** | The sender only | `Re: …` | Stays in the same thread (`In-Reply-To`/`References` headers) | Answering the person who wrote |
| **Reply All** | The sender, plus everyone else on the original To/Cc as Cc, **except your own address** | `Re: …` | Same thread | Everyone on the original email should see the answer |
| **Forward** | Empty; you choose | `Fwd: …` | The original is quoted below; starts a new thread with the new recipient | Passing the email to someone who wasn't on it |
| **New Email** | Empty; you choose | Yours | A brand-new thread | Starting a fresh conversation with the contact |

- All four are sent by `EmailReplyService` through the conversation's connection (its SMTP account), saved into the conversation, and audited as `EmailReply.Sent` on the conversation.
- **New Email really starts a new thread:** it carries no `In-Reply-To`/`References` headers.
- **Read-only users** (no `Chat.Send`) see the conversation but no composer. The server enforces every permission the page checks.

**Code:** [Chat.tsx](Frontend/src/pages/Chat/Chat.tsx), [ChatContextPanel.tsx](Frontend/src/pages/Chat/ChatContextPanel.tsx), [ConversationOwnerControls.tsx](Frontend/src/pages/Chat/ConversationOwnerControls.tsx), [EmailComposer.tsx](Frontend/src/components/EmailComposer/EmailComposer.tsx), [ChatService.cs](Backend/Services/ChatService.cs) (`CountConversationsAsync`, `OrderConversations`), [AuditQueries.cs](Backend/Data/AuditQueries.cs), [ConversationOperations.cs](Backend/Services/Chat/ConversationOperations.cs). **Tests:** Phases 17 and 23.

---

## 12. Connections: email (SMTP + IMAP) and WhatsApp

**The email connection page (Connections › Email):**
- **Sending (SMTP):** host, port, security, username, password, from address and name, send rate.
  - Choosing a well-known port fills in its security: 465 → SSL/TLS, 587 → STARTTLS, 2525 → STARTTLS. The ports, security modes and rate limits come from `GET api/reference/email-options`.
  - **Test connection** logs in for real.
- **Receiving (IMAP), for replies in Chat:**
  - **Leave it blank** and the server derives it from the SMTP host (`mail.example.com` → IMAP on 993, SSL).
  - Or enter the host, port and credentials explicitly.
  - **Test mailbox** logs in and counts the messages.
- **Domain authentication:** live SPF, DKIM, DMARC and MX results for the sender's domain (`GET api/email/connections/{id}/domain-health`).
- **Test email:** sends a real message to an address you choose.
- **Senders:** the addresses campaigns may send from.
- **Unreadable password:** if a stored password can't be decrypted, the connection shows **Needs attention**, with a prompt to re-enter it.

**Your settings (checked 1 Oct 2026):**
- SMTP `mail.rma.my:465` authenticates; a test email reached manishmishra8970@gmail.com.
- IMAP `mail.rma.my:993` (SSL, saved explicitly now) reads the inbox (22 messages).
- Domain checks for rma.my: SPF, DKIM and MX pass; **DMARC is missing**. Gmail and Yahoo require a DMARC record for bulk senders, so add one at your DNS provider (start with `v=DMARC1; p=none; rua=mailto:<your address>`).
- **mail.rma.my sends no bounce report for unknown local mailboxes:** a message to a non-existent `@rma.my` address was accepted and silently discarded. Bounces from other domains are reported normally; see §18.

**Code:** [ConnectEmail.tsx](Frontend/src/pages/ConnectEmail/ConnectEmail.tsx), [ConnectionsList.tsx](Frontend/src/pages/Connections/ConnectionsList.tsx), [EmailConnectionService.cs](Backend/Services/Email/EmailConnectionService.cs), [ImapEndpointResolver.cs](Backend/Services/Email/ImapEndpointResolver.cs). **Tests:** Phase 2.

---

## 13. Platform email: `IEmailSender` (replaces Amazon SES)

**Amazon SES is gone entirely:**
- the AWS packages, the SES sender, SNS webhooks, domain provisioning, and the SES config and columns (migration `RemoveAmazonSes`).
- Every connection is now plain SMTP through MailKit.

**Two kinds of email, on purpose:**

| Kind | Sent through | Examples |
|---|---|---|
| **Customer messaging** | Each connection's own SMTP account (per-connection credentials, send rate, sender identities) | Campaigns, A/B tests, follow-ups, chat replies, proofs, scheduled reports |
| **System mail** | One platform account, `IEmailSender` | Welcome email for new users (when "Send welcome email" is ticked); more system notices can use it |

**`IEmailSender`** ([SmtpEmailSender.cs](Backend/Services/Email/SmtpEmailSender.cs)), mirroring OmniConnect's AuthService:

```csharp
public interface IEmailSender
{
    bool IsEnabled { get; }
    Task<bool> SendAsync(string toAddress, string toName, string subject,
                         string htmlBody, string textBody, CancellationToken ct = default);
}
```

- **Settings:** section `Smtp` ([SmtpOptions.cs](Backend/Models/Options/SmtpOptions.cs)): Host, Port (587 default), Username, Password, FromAddress, FromName, AppBaseUrl.
  - `IsConfigured` is true when Host is set and FromAddress (or Username) is set.
- **TLS:** port 465 uses implicit SSL (`SslOnConnect`); every other port uses `StartTls`. It never uses `Auto`, which could fall back to plaintext. A server that can't upgrade is refused.
- It authenticates only when a username is set.
- **It never throws:**
  - when unconfigured, it logs and returns `false`;
  - on any error, it logs the exception and returns `false`.
  - So an email problem never breaks a user action or a database transaction.

**Configure it** via environment variables (see [Backend/.env.example](Backend/.env.example)), user-secrets, or `appsettings.secrets.local.json`:

```env
Smtp__Host=mail.rma.my
Smtp__Port=465
Smtp__Username=crm1@rma.my
Smtp__Password=
Smtp__FromAddress=crm1@rma.my
Smtp__FromName=OmniConnect
```

**Check it:**
- `GET api/system-mail/status` shows whether it's configured, without secrets.
- `POST api/system-mail/test` sends a test message.
- **Until `Smtp__Password` is set, system mail is off and skipped cleanly.**

**Tests:** Phase 22, "Platform SMTP", including a fake server that can't do TLS.

---

## 14. Templates, webhooks, reports

- **WhatsApp templates:**
  - Synced from Meta, with buttons (quick reply, link, phone, copy code) and carousels.
  - **New Template** creates one here and submits it to Meta for review, checking Meta's rules first.
  - Campaign variables fill button values and carousel cards.
  - The wizard's variable fields come from each template's own placeholders.
- **Click-to-WhatsApp ads:** the ad that brought a contact in is recorded. It shows on the contact, can be used in segments, and appears in reports.
- **Outbound webhooks (Setup › Webhooks):**
  - 17 event types, signed with HMAC-SHA256 (`X-Waba-Signature`), with an event id for de-duplication.
  - Delivery is queued, retried with backoff up to 8 attempts, and a webhook is switched off after 20 failures in a row.
  - SSRF-safe: https only, and private or metadata addresses are refused both on save and at connect time.
  - Delivery log with replay.
- **Reports:**
  - Saved reports and a link-click report with CSV export.
  - **Scheduled reports:** daily, weekly or monthly, at a local time, as CSV, Excel or PDF, to up to 20 people, sent from a chosen connection. They run with the scheduler's own access.
- **Code and tests:** listed in [WORK_SUMMARY.md §3](WORK_SUMMARY.md#3-round-2--startup-fix-and-the-13-enterprise-features) (Phases 11, 12, 18, 19).

---

## 15. Security: credentials, key guard, access scoping

- **Credentials at rest:**
  - SMTP and IMAP passwords, WhatsApp tokens and webhook secrets are encrypted with AES-GCM (`enc:v2:`) using `Encryption:Key` ([EncryptionService.cs](Backend/Services/EncryptionService.cs)).
  - **Never change that key** without re-entering every stored credential afterwards.
- **Startup key guard ([EncryptionKeyGuard.cs](Backend/Helpers/EncryptionKeyGuard.cs)):**
  - Reads every stored credential. If **none** decrypts, the key is wrong and the backend refuses to start.
  - Individual unreadable values are logged by table, row and column.
  - It also warns when **another build** is connected to the same database's job queue (see §16). That is what really caused the "password could not be decrypted" failures.
- **Secrets loading:**
  - Sources: user-secrets, `Backend/appsettings.secrets.local.json` (git-ignored; readable from sandboxed terminals), or environment variables.
  - A missing secret fails startup with a message listing every file checked.
- **Access scoping** (`Security:EnforceConnectionScoping = true`): non-admins see only the connections assigned to them or their role, in chats, campaigns, reports, the dashboard and live updates.
- **Sessions and requests:**
  - Account lockout and short sessions with refresh tokens.
  - Signed Meta webhooks, security headers, and PII masked in logs.
  - Rate limiting: global per user or IP, plus a stricter `auth` policy on login and public endpoints.

---

## 16. The job queue and its contract version

**How the queue works:**
- All background work is rows in the `JobQueue` table.
- Workers **lease** jobs with `SELECT … FOR UPDATE SKIP LOCKED`, so any number of backend instances can share the work without sending anything twice.
- Leases expire, so a crashed worker's jobs are picked up again. Idempotency keys stop duplicates.

**Contract version:**
- Every queue name ends in a version, now `v3`, e.g. `email-send.v3` ([QueueNames.cs](Backend/Services/Queue/QueueNames.cs)). v3 came with round 5: sends now build links on the verified public address and record refused mailboxes as bounces, so a v2 build must not take them. Migration `QueueContractV3` moved waiting jobs to the new names.
- Each build's database sessions are named `WabaConnect/<version>`, so a build on another version is recognisable in `pg_stat_activity`.
- When a change alters what a job means, the version is bumped, and an **older build still connected to the same database can no longer take the new jobs**.
- **Why it exists:** an older copy of the app, running against the shared database with a different credential format, was picking up this build's send jobs. It failed them with "password could not be decrypted", and they were then counted as Bounced.
- `/health` reports **Degraded** while a foreign queue client is connected. This happened again on 1 Oct: a backend started before the round-5 changes kept taking jobs (and reading the mailbox) until it was stopped.

---

## 17. Scaling to millions of users

**Already in place:**

| Concern | What is done |
|---|---|
| Sending volume | Durable Postgres queue; `SKIP LOCKED` lets you add backend instances for more throughput; per-connection send-rate tokens; batched event writes |
| Large audiences | Recipients expanded server-side in batches; the audience picker and "every contact of these types" never load the full list into the browser; set-based recipient replacement on edit |
| Reads | Server-side paging on every list; indexes on the hot filters (contact type/active, phone, date of birth, campaign-recipient, queue lease, events); split queries where one query would multiply rows |
| Dashboard | Cached per access scope; debounced SignalR signals instead of per-client polling; polling pauses in hidden tabs |
| Reference data | `Cache-Control` on static catalogs (10 min client cache); lookups cached server-side for 60 s |
| Abuse | Global and auth rate limits; SSRF guard on webhooks; validated uploads |
| Failure isolation | Email and connection failures hold the campaign instead of failing recipients; the database retry strategy is used for all transactions |
| Tracking at volume | `/api/t/*` is idempotent and deliberately not rate-limited per IP (Gmail and Outlook fetch through shared proxies); each person's open counts once; the reconcile sweep repairs any recipient an event never reached |
| Audit at volume | One row per action, never per recipient (imports and campaign completions are summaries); indexes on Module + CreatedAt, EntityType + EntityId and Status; Recent Activity reads only the newest 8 rows |
| Live pages | Campaign page refreshes are batched (2 s) and targeted by event kind |

**What to add as traffic grows:**
1. **Redis backplane for SignalR** (`AddStackExchangeRedis`) once you run more than one backend instance, so a live update raised on one instance reaches browsers connected to another.
2. **A read replica for reports and the dashboard,** so heavy reporting never slows sending.
3. **Partition the message and event tables by month** once they pass roughly 100 M rows, and archive old partitions.
4. **A dedicated queue** (e.g. RabbitMQ or SQS) only if Postgres queue contention shows up in metrics. The `JobQueue` abstraction makes that a contained change.
5. **Metrics:** OpenTelemetry for queue depth, send rate, SLA breaches and webhook latency, with alerts.
6. **A CDN** for the built frontend, and object storage for uploads.

---

## 18. Tracking: opens, clicks, unsubscribes, bounces

**What each figure means:**

| Figure | Counted when | How |
|---|---|---|
| Unique opens | The recipient's mail app loads the 1×1 image at `{public address}/api/t/o/{signed token}` | Once per person, however often they open (Gmail's proxy re-fetches) |
| Unique clicks | The recipient follows a link; every absolute `http(s)` link is rewritten to `{public address}/api/t/c/{signed token}`, which records and redirects | Once per person; the Links card counts every click per URL |
| Unsubscribed | The recipient uses the unsubscribe link or Gmail's one-click button (`List-Unsubscribe`) | Consent becomes "opted out" for that channel, the address is suppressed everywhere, and the counter moves once |
| Bounced | The receiving server refuses the mailbox: either straight away (a 5xx to `RCPT TO`), or later in a delivery report read from the connection's inbox | The recipient is marked Bounced and the address suppressed |

**The one thing that must be right: the public address.**
- Every tracking and unsubscribe link is built on `App:PublicBaseUrl` (or `Email:Tracking:BaseUrl` when set). It must be an https address the internet can reach and that answers as this server.
- **Why nothing was counted before:** in Development it pointed at `http://localhost:5155`, and an old tunnel address was hard-coded for tracking. Gmail fetches images through Google's servers, which can never reach your PC, so no open or click ever arrived. Both are fixed: the hard-coded address is gone, and the problem is now visible:
  - **Pre-flight** shows "Tracking address": a pass, or a warning naming the address and the reason (`empty`, `localhost`, `private-network`, `not-https`, `unreachable`, `wrong-instance`).
  - **`/health`** reports `tracking-address` Degraded with the same reason.
  - The check is real: [PublicEndpointProbe.cs](Backend/Services/Email/PublicEndpointProbe.cs) calls `{address}/api/t/ping/{nonce}` from outside and only passes when **this** server answers.
- **Pre-flight also says** how many links will be click-tracked, or that the message has none, in which case the Links card stays empty.
- **To test on your PC:** run a tunnel (`cloudflared tunnel --url http://localhost:5155`) and start the backend with `--App:PublicBaseUrl=<the https address it prints>`. In production, set `App:PublicBaseUrl` to the API's public https origin.

**Verified live on 1 Oct 2026 (Gmail, through a Cloudflare tunnel):** opening the email counted 1 unique open within seconds; clicking both links counted 1 unique click and listed both URLs; the A/B table showed variant A at 100 % open rate; unsubscribing counted 1 live, and re-subscribing through the preference centre lifted the suppression. Every step is in the audit log.

**Bounces in detail:**
- **At send time:** MailKit reports a 5xx refusal of the recipient; [SmtpEmailProvider.cs](Backend/Services/Email/SmtpEmailProvider.cs) marks it a permanent bounce and [EmailSendRecorder.cs](Backend/Services/Email/EmailSendRecorder.cs) records Bounced (not Failed).
- **Later, by IMAP:** [ImapBounceDetector.cs](Backend/Services/Email/ImapBounceDetector.cs) reads RFC 3464 reports; [PlainTextBounceParser.cs](Backend/Services/Email/PlainTextBounceParser.cs) reads the plain-text reports Exim/cPanel, qmail and older Postfix send (they used to be thrown away as auto-replies). It only acts on evidence: a 5.x.x/5xx status or wording such as "permanent error"; 4.x.x means a delay, never a bounce. Patterns live in `BounceCatalog`.
- **Matching:** by the original Message-ID; if a report doesn't include it, by the address and the latest send to it from the same connection within `BounceCatalog.MatchWindowDays` (7).

**Self-healing counters:** the event is recorded first, then the recipient is stamped. If a process dies in between, the every-minute sweep repairs the recipient from the event log and recomputes the counters. It also picks up finished campaigns that received an event recently (an open days later). See `CampaignFinalizer.ReconcileEmailCountersAsync`.

**Tests:** Phase 5 (pixel and links on the wire), Phase 23 (public address, opens/clicks counting, Links report, crash repair, plain-text and SMTP bounces).

---

## 19. Audit log

**Two different logs, explained:**
- **Audit Log** (sidebar, `/audit-log`): who did what and when. It's append-only: nobody can edit or delete it. Tabs: Login errors, Login successes, Audit events (filter by module, action, user, status, date).
- **Setup › Activity Log** (removed in round 5): a per-message send log with raw WhatsApp/email API payloads. It overlapped the campaign's Executed list and Reporting, held personal data, and could be deleted by users. Its 97 rows were backed up first; see [restore-round5-2026-10-01.sql](Backend/scripts/restore-round5-2026-10-01.sql). The old addresses redirect to the Audit Log.

**What is recorded (all via `IAuditService`, actor = the signed-in person, "System" for background work, blank for an anonymous request):**

| Area | Events |
|---|---|
| Contacts | Created (including re-adding a deleted one, and contacts the system creates from an incoming WhatsApp message or email), Updated, Deleted, StatusChanged, Imported (one summary per file or bulk campaign), ContactNote Created/Deleted |
| Campaigns | Created, Updated, Deleted, Cancelled, Paused, Resumed, **Held** (connection problem, by System), AwaitingApproval, Approved, Rejected, RetryFailed, PrecheckOverridden, ProofSent, AbTestDecided, FollowUpRun, **Completed / PartiallyFailed / SendFailed** (by System, once each) |
| Chat | MessageSent/MessageFailed (agent WhatsApp replies), TemplateSent/TemplateFailed, EmailReply.Sent (all four email modes), Assigned, StatusChanged, MessagesDeleted, ConversationDeleted |
| Templates | Created, Updated, Deleted, Submitted, **StatusChanged** (Meta's review decision), **Synced** (summary of a sync) |
| Consent and suppression | Consent.OptedIn/OptedOut, EmailSuppression.Unsubscribed/Removed |
| Everything else | Segments, bots, bot flows, connections, email connections and senders, users, roles, permissions, settings, webhooks, report schedules, sign-ins, password changes, access denials, server errors |

**Why contact changes seemed unaudited:** contacts re-created after the reset reused a soft-deleted record with the same phone number, and that path never wrote an audit row. Fixed and tested.

**Recent Activity** on the dashboard is the newest 8 audit rows from the business modules in `RecentActivityCatalog`.

**Code:** [AuditService.cs](Backend/Services/AuditService.cs), [ActivityController.cs](Backend/Controllers/ActivityController.cs), [ActivityLogs.tsx](Frontend/src/pages/ActivityLogs.tsx). **Tests:** Phase 22 (hold), Phase 23 ("Every change is audited", "Recent Activity is the audit log", "Who did it").

---

## 20. How to run and check everything

```bash
# Backend (port 5155)
cd Backend && dotnet run --launch-profile http

# Frontend (port 5173)
cd Frontend && pnpm install && pnpm dev
```

```bash
# Checks
cd Backend && dotnet build -c Release && dotnet ef migrations has-pending-model-changes
cd Frontend && pnpm exec tsc -b && pnpm lint && pnpm build && pnpm audit --prod
cd Tests/EmailChannel.Tests && dotnet run          # stop the backend first; or: dotnet run -- 21 22
```

**Data scripts** (you run these yourself):
- [Backend/scripts/purge-contacts-campaigns.sql](Backend/scripts/purge-contacts-campaigns.sql) permanently removes contacts and campaigns. Set `ONLY_SOFT_DELETED` to limit it to rows already deleted.
- [Backend/scripts/restore-contacts-campaigns-2026-09-29.sql](Backend/scripts/restore-contacts-campaigns-2026-09-29.sql) undoes the 29 Sep reset (the soft delete).
- The JSON backup is in `Backend/backups/` (git-ignored).
