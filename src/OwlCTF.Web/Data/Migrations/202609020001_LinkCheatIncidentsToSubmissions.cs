using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace OwlCTF.Data.Migrations;

[DbContext(typeof(InstanceDbContext))]
[Migration("202609020001_LinkCheatIncidentsToSubmissions")]
public sealed class LinkCheatIncidentsToSubmissions : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("""
            ALTER TABLE CheatIncidents ADD COLUMN IF NOT EXISTS SubmissionAttemptId BIGINT NULL;
            ALTER TABLE CheatIncidents ADD COLUMN IF NOT EXISTS ManualBanAtUtc DATETIME(6) NULL;
            ALTER TABLE CheatIncidents ADD COLUMN IF NOT EXISTS ManualBanByUserId CHAR(36) NULL;
            ALTER TABLE CheatIncidents ADD UNIQUE INDEX IF NOT EXISTS UX_CheatIncidents_SubmissionAttempt (SubmissionAttemptId);
            """);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("""
            ALTER TABLE CheatIncidents DROP INDEX IF EXISTS UX_CheatIncidents_SubmissionAttempt;
            ALTER TABLE CheatIncidents DROP COLUMN IF EXISTS ManualBanByUserId;
            ALTER TABLE CheatIncidents DROP COLUMN IF EXISTS ManualBanAtUtc;
            ALTER TABLE CheatIncidents DROP COLUMN IF EXISTS SubmissionAttemptId;
            """);
    }
}
