-- Undo the 2026-09-29 soft delete of all contacts and campaigns (see purge-contacts-campaigns.sql).
-- Restores exactly the rows that were live before the reset, with their earlier status. Generated
-- from the backup in Backend/backups/2026-09-29-contacts-campaigns-reset/ (git-ignored). Run it
-- yourself with psql; it changes nothing if those rows were purged in the meantime.
BEGIN;
UPDATE "Contacts" SET "IsDeleted" = false, "IsActive" = b."IsActive", "UpdatedAt" = now()
  FROM (VALUES (80, true), (98, true), (120, true), (121, true), (122, true), (123, true), (104, true), (106, true), (83, true), (94, true), (124, true), (97, true), (114, true), (125, true), (126, true), (127, true), (128, true), (129, true), (130, true), (131, true), (216, true), (150, true), (217, true)) AS b("Id", "IsActive")
 WHERE "Contacts"."Id" = b."Id";
UPDATE "Campaigns" SET "IsDeleted" = false, "DeletedAt" = NULL, "DeletedBy" = NULL, "Status" = b."Status"
  FROM (VALUES (251, 'Failed'), (110, 'Sent'), (117, 'Sent'), (121, 'Sent'), (111, 'Sent'), (124, 'Sent'), (120, 'Sent'), (118, 'Sent'), (151, 'Sent'), (115, 'Sent'), (113, 'Sent'), (114, 'Sent'), (125, 'Sent'), (131, 'Sent'), (127, 'Failed'), (122, 'Failed'), (123, 'Sent'), (128, 'Failed'), (129, 'Sent'), (130, 'Sent'), (172, 'Sent'), (179, 'Sent'), (162, 'Sent'), (163, 'Sent'), (181, 'Sent'), (174, 'Sent'), (183, 'Sent'), (164, 'Sent'), (170, 'Sent'), (169, 'Sent'), (168, 'Failed'), (160, 'Cancelled'), (159, 'Cancelled'), (175, 'Sent'), (180, 'Sent'), (167, 'Failed'), (177, 'Sent'), (158, 'Cancelled'), (182, 'Sent'), (171, 'Sent'), (178, 'Sent'), (165, 'Sent'), (173, 'Sent'), (166, 'Sent'), (176, 'Sent'), (216, 'Failed')) AS b("Id", "Status")
 WHERE "Campaigns"."Id" = b."Id" AND "Campaigns"."DeletedBy" = 'Data reset (owner request)';
COMMIT;
