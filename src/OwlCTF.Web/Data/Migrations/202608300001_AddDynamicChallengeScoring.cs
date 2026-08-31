using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace OwlCTF.Data.Migrations;

[DbContext(typeof(InstanceDbContext))]
[Migration("202608300001_AddDynamicChallengeScoring")]
public sealed class AddDynamicChallengeScoring : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("""
            ALTER TABLE Challenges ADD COLUMN IF NOT EXISTS Initial INT NOT NULL DEFAULT 100;
            ALTER TABLE Challenges ADD COLUMN IF NOT EXISTS Minimum INT NOT NULL DEFAULT 100;
            ALTER TABLE Challenges ADD COLUMN IF NOT EXISTS Decay INT NOT NULL DEFAULT 0;
            ALTER TABLE Challenges ADD COLUMN IF NOT EXISTS CurrentValue INT NOT NULL DEFAULT 100;
            ALTER TABLE Solves ADD COLUMN IF NOT EXISTS ValueAwarded INT NOT NULL DEFAULT 0;
            ALTER TABLE Teams ADD COLUMN IF NOT EXISTS IsHidden BOOLEAN NOT NULL DEFAULT FALSE;
            UPDATE Challenges SET Initial=Points,Minimum=Points,CurrentValue=Points
            WHERE Decay=0 AND Initial=100 AND Minimum=100 AND CurrentValue=100 AND Points<>100;
            UPDATE Solves SET ValueAwarded=PointsAwarded WHERE ValueAwarded=0 AND PointsAwarded>0;
            """);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("""
            ALTER TABLE Teams DROP COLUMN IF EXISTS IsHidden;
            ALTER TABLE Solves DROP COLUMN IF EXISTS ValueAwarded;
            ALTER TABLE Challenges DROP COLUMN IF EXISTS CurrentValue;
            ALTER TABLE Challenges DROP COLUMN IF EXISTS Decay;
            ALTER TABLE Challenges DROP COLUMN IF EXISTS Minimum;
            ALTER TABLE Challenges DROP COLUMN IF EXISTS Initial;
            """);
    }
}
