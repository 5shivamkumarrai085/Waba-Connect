-- =====================================================================================
-- PERMANENTLY delete every contact and campaign, and everything that hangs off them.
--
-- The application only ever soft-deletes (IsDeleted = true), which is reversible. On
-- 2026-09-29 all contacts and campaigns were soft-deleted at the owner's request, after a
-- JSON backup was written to Backend/backups/2026-09-29-contacts-campaigns-reset/.
-- This script is for when you are sure you want the rows gone for good. It cannot be undone.
--
-- Run it yourself, against the right database, with the API stopped:
--   psql "<connection string>" -v ONLY_SOFT_DELETED=1 -f Backend/scripts/purge-contacts-campaigns.sql
--
-- By default it removes only rows that are already soft-deleted. Set ONLY_SOFT_DELETED=0 to
-- remove every contact and campaign, deleted or not.
--
-- Order follows the foreign keys (see AppDbContext): Restrict relationships first, then the
-- parents, whose Cascade relationships take the rest. Tables that reference a contact or
-- campaign only by value (no foreign key) are cleaned explicitly.
-- =====================================================================================

\set ON_ERROR_STOP on
\if :{?ONLY_SOFT_DELETED}
\else
  \set ONLY_SOFT_DELETED 1
\endif

BEGIN;

CREATE TEMP TABLE purge_campaigns ON COMMIT DROP AS
  SELECT "Id" FROM "Campaigns" WHERE (:ONLY_SOFT_DELETED = 0 OR "IsDeleted");

CREATE TEMP TABLE purge_contacts ON COMMIT DROP AS
  SELECT "Id" FROM "Contacts" WHERE (:ONLY_SOFT_DELETED = 0 OR "IsDeleted");

-- Queued work for those campaigns (the payload holds the id; PartitionKey is the campaign id).
DELETE FROM "JobQueue" WHERE "PartitionKey" IN (SELECT "Id"::text FROM purge_campaigns);

-- Follow-up campaigns point at their parent by value.
UPDATE "Campaigns" SET "ParentCampaignId" = NULL WHERE "ParentCampaignId" IN (SELECT "Id" FROM purge_campaigns);

-- Campaigns: cascades to recipients, variables, variants, segments, approvals, retry runs,
-- email details and follow-up rules. Chat messages and email events keep their history (SetNull).
DELETE FROM "CampaignContacts" WHERE "CampaignId" IN (SELECT "Id" FROM purge_campaigns);
DELETE FROM "Campaigns" WHERE "Id" IN (SELECT "Id" FROM purge_campaigns);

-- Recipients of other campaigns that point at a contact being removed (Restrict).
DELETE FROM "CampaignContacts" WHERE "ContactId" IN (SELECT "Id" FROM purge_contacts);

-- Conversations are Restrict on the contact; deleting them cascades to their messages and
-- email message details.
DELETE FROM "ChatConversations" WHERE "ContactId" IN (SELECT "Id" FROM purge_contacts);

-- Referenced by value only.
DELETE FROM "ConsentEvents" WHERE "ContactId" IN (SELECT "Id" FROM purge_contacts);
UPDATE "MessageActivityLogs" SET "ContactId" = NULL WHERE "ContactId" IN (SELECT "Id" FROM purge_contacts);

-- Contacts: cascades to consents, group memberships, notes and remaining chat messages.
DELETE FROM "Contacts" WHERE "Id" IN (SELECT "Id" FROM purge_contacts);

SELECT (SELECT count(*) FROM purge_campaigns) AS campaigns_removed,
       (SELECT count(*) FROM purge_contacts)  AS contacts_removed;

-- Review the counts above, then:
COMMIT;
-- (Replace COMMIT with ROLLBACK to try it without changing anything.)
