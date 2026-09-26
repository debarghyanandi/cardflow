using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace Cardflow.Api.Data.Migrations;

[DbContext(typeof(CardflowDbContext))]
[Migration("20260926_Phase1RankText")]
public sealed class Phase1RankText : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("ALTER TABLE columns ALTER COLUMN rank TYPE text; ALTER TABLE cards ALTER COLUMN rank TYPE text;");
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("ALTER TABLE cards ALTER COLUMN rank TYPE varchar(256); ALTER TABLE columns ALTER COLUMN rank TYPE varchar(256);");
    }
}
