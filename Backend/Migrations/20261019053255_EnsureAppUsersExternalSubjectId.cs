using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace WhatsAppCampaignApi.Migrations
{
    /// <inheritdoc />
    public partial class EnsureAppUsersExternalSubjectId : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(@"
                DO $$
                BEGIN
                    IF EXISTS (
                        SELECT 1 FROM information_schema.columns 
                        WHERE table_name = 'AppUsers' AND column_name = 'ExternalUserId'
                    ) AND NOT EXISTS (
                        SELECT 1 FROM information_schema.columns 
                        WHERE table_name = 'AppUsers' AND column_name = 'ExternalSubjectId'
                    ) THEN
                        ALTER TABLE ""AppUsers"" RENAME COLUMN ""ExternalUserId"" TO ""ExternalSubjectId"";
                    ELSIF NOT EXISTS (
                        SELECT 1 FROM information_schema.columns 
                        WHERE table_name = 'AppUsers' AND column_name = 'ExternalSubjectId'
                    ) THEN
                        ALTER TABLE ""AppUsers"" ADD COLUMN ""ExternalSubjectId"" character varying(150);
                    END IF;
                END $$;
            ");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(@"
                DO $$
                BEGIN
                    IF EXISTS (
                        SELECT 1 FROM information_schema.columns 
                        WHERE table_name = 'AppUsers' AND column_name = 'ExternalSubjectId'
                    ) AND NOT EXISTS (
                        SELECT 1 FROM information_schema.columns 
                        WHERE table_name = 'AppUsers' AND column_name = 'ExternalUserId'
                    ) THEN
                        ALTER TABLE ""AppUsers"" RENAME COLUMN ""ExternalSubjectId"" TO ""ExternalUserId"";
                    END IF;
                END $$;
            ");
        }
    }
}
