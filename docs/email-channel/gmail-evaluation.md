# Gmail integration — evaluation and recommendation

**Status:** analysis only. Nothing in this document has been implemented.
**Asked:** whether Gmail integration is needed, given we already use Amazon SES for outbound and
SES inbound via webhook, and if it is, what the most secure enterprise approach would be.

---

## Recommendation

**Gmail is not needed, and should not become a campaign sending channel.**

The distinction that matters is *sending to* Gmail versus *sending as* Gmail:

- **Sending to Gmail recipients** already works, and works well. Gmail is a mailbox provider, not
  a channel. SES delivers to `@gmail.com` addresses like any other, and our SPF/DKIM/DMARC setup
  is specifically what makes Gmail accept that mail — Google's 2024 bulk-sender rules require
  authenticated mail with one-click unsubscribe, both of which the email channel implements.

- **Sending as a Gmail or Workspace mailbox** is the only thing a Gmail integration would add. That
  is a real requirement only if a client insists the mail leave *their* mailbox — typically so it
  appears in their Sent folder, or because they have no domain of their own.

For campaign sending, that second case is the wrong tool regardless of how it is built: Gmail
enforces per-user daily send caps (2,000/day on Workspace, 500/day on consumer Gmail) and provides
no bounce or complaint feedback loop. A campaign of any size would exhaust the quota and then have
no way to learn who it failed to reach.

## If a client does require it, in order of preference

### 1. Verify the client's own domain in SES — recommended

Almost every "we need Gmail" request is really "we need mail to come from
`someone@theirdomain.com`", and their domain being *hosted* on Google Workspace does not stop SES
from sending as it.

- No new integration, no new code, no new credential type.
- One sending pipeline, so delivery events, suppression, retries and reporting all keep working.
- DKIM and DMARC stay under our control and aligned.
- Their inbound mail continues to arrive in Gmail as normal — MX records are unaffected by adding
  SES as an authorised sender.

Cost: one SPF include and three DKIM CNAMEs in their DNS, which is the same setup any sending
domain needs. The existing domain-verification screen already walks an operator through it.

### 2. Google Workspace SMTP relay — works today, no new code

`smtp-relay.gmail.com` is an ordinary SMTP endpoint, so it is already supported: it is another
connection configured with the SMTP provider.

- Zero implementation cost. It is a configuration, not a feature.
- Restricted by IP allow-list or by Workspace-authenticated relay, both configured on the client's
  side.

Limitations, which the UI already states for any SMTP connection: no delivery, bounce, complaint,
open or click events. Failures surface only as SMTP-level rejections at send time, so a bounce an
hour later is invisible. `EmailProviderCapabilities` reports this honestly rather than showing
tracking that will never populate.

### 3. Gmail API with OAuth 2.0 — only for per-user mailbox sending

Justified only when mail must genuinely originate from an individual user's mailbox and appear in
their Sent folder. Two variants:

| | Service account with domain-wide delegation | Per-user OAuth |
|---|---|---|
| Who consents | A Workspace admin, once | Each user, individually |
| Scope | Can impersonate any user in the domain | Only the consenting user |
| Works for | Workspace only | Workspace and consumer Gmail |
| Risk | A single compromised key can send as anyone in the domain | Contained to one user |

What it would actually cost:

- A Google Cloud project, an OAuth consent screen, and — because `gmail.send` is a restricted
  scope — a Google security review before it can be used outside the publishing organisation.
  That review takes weeks and has to be repeated when scopes change.
- Refresh-token storage and rotation per user. These are long-lived credentials with a broader
  blast radius than an SES key, and the encrypted-column approach used for provider secrets is
  adequate but not ideal for something impersonating a person.
- Per-user quotas as above, and no feedback loop.
- A genuinely different send shape: the Gmail API takes a base64url-encoded RFC 2822 message
  rather than an SMTP conversation, so it needs its own `IEmailProvider` implementation.

**If it has to be built**, the shape is already there: a third `IEmailProvider` registered
alongside SES and SMTP, selected by `ProviderName`. No change to the queue, the workers, the
campaign service, the templates or the UI. The realistic work is the OAuth token lifecycle and
the Google verification process, not the sending.

## What to avoid

**Gmail app passwords.** They bypass MFA entirely, Google has been progressively restricting them
since 2024, and they put a long-lived password with full mailbox access into our database. They
offer nothing over options 1 or 2. The SMTP provider would technically accept one, but it should
not be recommended to a client.

## Inbound

No change. Inbound replies arrive through the SES receipt rule and the SNS webhook, keyed on the
`Message-ID` we generate when sending. That works whichever provider sent the original message,
including the SMTP relay, because the correlation is on our own header rather than on anything
provider-specific.

The one case that would need work is a client wanting replies to land in their Gmail *and* be
threaded in this app. That is IMAP polling or a Gmail watch subscription — a separate piece of
work from sending, and worth a separate decision.

## Summary

| Requirement | Answer |
|---|---|
| Deliver reliably to Gmail recipients | Already works. SES plus SPF/DKIM/DMARC is what makes it work. |
| Send from a client's own domain hosted on Workspace | Verify their domain in SES. No new code. |
| Send through the client's Workspace infrastructure | Configure an SMTP connection against the Workspace relay. No new code. |
| Send from an individual user's mailbox, in their Sent folder | Gmail API with OAuth. Real cost; needs a business reason. |
| Use Gmail for bulk campaigns | Do not. Per-user quotas and no feedback loop make it unsuitable. |

No change to the existing SES functionality is proposed or required.
