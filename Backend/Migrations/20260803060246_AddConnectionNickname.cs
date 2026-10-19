using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace WhatsAppCampaignApi.Migrations
{
    /// <inheritdoc />
    public partial class AddConnectionNickname : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(@"
                DO $$
                BEGIN
                    IF NOT EXISTS (
                        SELECT 1 FROM information_schema.columns 
                        WHERE table_name = 'Connections' AND column_name = 'Nickname'
                    ) THEN
                        ALTER TABLE ""Connections"" ADD COLUMN ""Nickname"" character varying(4);
                    ELSE
                        ALTER TABLE ""Connections"" ALTER COLUMN ""Nickname"" TYPE character varying(4);
                    END IF;
                END $$;
            ");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "Nickname",
                table: "Connections");
        }
    }
}
