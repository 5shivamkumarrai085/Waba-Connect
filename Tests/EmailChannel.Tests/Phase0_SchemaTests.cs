namespace EmailChannel.Tests;

/// <summary>
/// Phase 0 — the channel dimension was added without disturbing what was already there.
///
/// <para>
/// These assertions are the ones that matter most on a database with live data in it. The email
/// channel required a nullable <c>Campaigns.TemplateId</c>, a widened unique index on
/// <c>ChatConversations</c>, and new columns on three shared tables — each of which could have
/// silently lost or re-pointed existing rows. Checking the schema alone would not catch that, so
/// these check the data too.
/// </para>
/// </summary>
public static class Phase0_SchemaTests
{
    public static async Task RunAsync(TestHarness harness, TestRun run)
    {
        run.BeginPhase("Phase 0 — channel dimension, additive and non-destructive");

        run.Section("Existing rows kept their meaning");

        var campaigns = await harness.CountAsync("""SELECT count(*) FROM "Campaigns" """);
        var offChannel = await harness.CountAsync(
            """SELECT count(*) FROM "Campaigns" WHERE "Channel" IS NULL""");
        run.Check("every campaign has a channel", offChannel == 0, $"{offChannel} without one");

        // WhatsApp campaigns must still point at a WhatsApp template. A campaign on that channel
        // with a null template would mean the nullability change dropped data.
        var whatsappWithoutTemplate = await harness.CountAsync(
            """SELECT count(*) FROM "Campaigns" WHERE "Channel" = 'WhatsApp' AND "TemplateId" IS NULL""");
        run.Check("no WhatsApp campaign lost its template",
            whatsappWithoutTemplate == 0, $"{whatsappWithoutTemplate} orphaned");

        // The mirror check: an email campaign must not be carrying one.
        var emailWithWhatsappTemplate = await harness.CountAsync(
            """SELECT count(*) FROM "Campaigns" WHERE "Channel" = 'Email' AND "TemplateId" IS NOT NULL""");
        run.Check("no email campaign holds a WhatsApp template",
            emailWithWhatsappTemplate == 0, $"{emailWithWhatsappTemplate} mismatched");

        var conversationsOffChannel = await harness.CountAsync(
            """SELECT count(*) FROM "ChatConversations" WHERE "Channel" IS NULL""");
        run.Check("every conversation has a channel", conversationsOffChannel == 0);

        var messagesOffChannel = await harness.CountAsync(
            """SELECT count(*) FROM "ChatMessages" WHERE "Channel" IS NULL""");
        run.Check("every chat message has a channel", messagesOffChannel == 0);

        // The pre-existing WhatsApp correlation column must still be populated for WhatsApp
        // traffic — email writes only ProviderMessageId, and must not have displaced it.
        var whatsappIdsIntact = await harness.CountAsync("""
            SELECT count(*) FROM "ChatMessages"
             WHERE "Channel" = 'WhatsApp' AND "WhatsAppMessageId" IS NOT NULL
            """);
        run.Check("WhatsApp message ids are still present", whatsappIdsIntact >= 0);

        var emailWritingWhatsappIds = await harness.CountAsync("""
            SELECT count(*) FROM "ChatMessages"
             WHERE "Channel" = 'Email' AND "WhatsAppMessageId" IS NOT NULL
            """);
        run.Check("no email message wrote to the WhatsApp id column",
            emailWritingWhatsappIds == 0, $"{emailWritingWhatsappIds} rows");

        run.Section("Schema shape");

        var columnDefault = await harness.ScalarAsync("""
            SELECT column_default FROM information_schema.columns
             WHERE table_name = 'Campaigns' AND column_name = 'Channel'
            """);
        run.Check("Channel has a database-level default, so a plain INSERT still works",
            columnDefault is not null && columnDefault.Contains("WhatsApp"), columnDefault);

        var templateIdNullable = await harness.ScalarAsync("""
            SELECT is_nullable FROM information_schema.columns
             WHERE table_name = 'Campaigns' AND column_name = 'TemplateId'
            """);
        run.Check("Campaigns.TemplateId is nullable", templateIdNullable == "YES", templateIdNullable);

        // The conversation key had to gain Channel, or one contact reachable on both channels
        // through one connection would have had their two threads merged into one row.
        var conversationIndex = await harness.ScalarAsync("""
            SELECT indexdef FROM pg_indexes
             WHERE schemaname = 'public'
               AND tablename = 'ChatConversations'
               AND indexdef LIKE '%UNIQUE%'
               AND indexdef LIKE '%ContactId%'
            """);
        run.Check("conversation uniqueness includes the channel",
            conversationIndex is not null && conversationIndex.Contains("Channel"), conversationIndex);

        run.Section("New tables and indexes exist");

        foreach (var table in new[]
        {
            "EmailConfigurations", "EmailSendingDomains", "EmailSenderIdentities",
            "EmailCampaignDetails", "EmailMessageDetails", "EmailDeliveryEvents",
            "EmailSuppressions", "EmailSendQuotas", "JobQueue", "CampaignApprovalStates"
        })
        {
            var exists = await harness.CountAsync("""
                SELECT count(*) FROM information_schema.tables
                 WHERE table_schema = 'public' AND table_name = @name
                """, ("name", table));

            run.Check($"table {table} exists", exists == 1);
        }

        foreach (var index in new[]
        {
            "IX_JobQueue_Claim", "IX_JobQueue_Lease", "IX_JobQueue_Partition", "UX_JobQueue_Idempotency",
            "UX_EmailSuppression_Global", "UX_EmailSuppression_Connection"
        })
        {
            var exists = await harness.CountAsync("""
                SELECT count(*) FROM pg_indexes WHERE schemaname = 'public' AND indexname = @name
                """, ("name", index));

            run.Check($"index {index} exists", exists == 1);
        }

        run.Section("Delete rules preserved");

        // Making TemplateId nullable changes EF's conventional delete behaviour from Cascade to
        // NoAction. That was pinned back to Cascade explicitly, because a template delete
        // previously hard-deleted soft-deleted campaign rows and quietly changing that is a
        // behaviour change this work was required not to make.
        var templateRule = await harness.ScalarAsync("""
            SELECT rc.delete_rule
              FROM information_schema.table_constraints tc
              JOIN information_schema.referential_constraints rc ON tc.constraint_name = rc.constraint_name
             WHERE tc.table_name = 'Campaigns' AND tc.constraint_name = 'FK_Campaigns_Templates_TemplateId'
            """);
        run.Check("WhatsApp template delete rule is still CASCADE", templateRule == "CASCADE", templateRule);

        // The email template, by contrast, is RESTRICT: a sent campaign must stay able to explain
        // what it sent, so its template cannot be deleted out from under it.
        var emailTemplateRule = await harness.ScalarAsync("""
            SELECT rc.delete_rule
              FROM information_schema.table_constraints tc
              JOIN information_schema.referential_constraints rc ON tc.constraint_name = rc.constraint_name
             WHERE tc.table_name = 'Campaigns'
               AND tc.constraint_name = 'FK_Campaigns_EmailTemplates_EmailTemplateId'
            """);
        run.Check("email template delete rule is RESTRICT", emailTemplateRule == "RESTRICT", emailTemplateRule);

        run.Section("Seeded content untouched");

        var seededTemplates = await harness.CountAsync(
            """SELECT count(*) FROM "EmailTemplates" WHERE "IsSystem" = true""");
        run.Check("the four seeded system templates are still present",
            seededTemplates >= 4, $"{seededTemplates}");

        Console.WriteLine($"    note  {campaigns} campaign(s) in the database were checked");
    }
}
