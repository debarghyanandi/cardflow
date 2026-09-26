using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace Cardflow.Api.Data.Migrations;

[DbContext(typeof(CardflowDbContext))]
[Migration("20260926_Phase2Events")]
public sealed class Phase2Events : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("""
            ALTER TABLE boards ADD COLUMN event_seq bigint NOT NULL DEFAULT 0;
            CREATE TABLE board_events (
                board_id uuid NOT NULL REFERENCES boards(id) ON DELETE CASCADE,
                seq bigint NOT NULL,
                type varchar(60) NOT NULL,
                payload jsonb NOT NULL,
                actor_member_id uuid NULL REFERENCES board_members(id) ON DELETE SET NULL,
                created_at timestamptz NOT NULL,
                PRIMARY KEY (board_id, seq)
            );
            CREATE INDEX ix_board_events_actor_member_id ON board_events (actor_member_id);
            """);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("DROP TABLE board_events; ALTER TABLE boards DROP COLUMN event_seq;");
    }
}
