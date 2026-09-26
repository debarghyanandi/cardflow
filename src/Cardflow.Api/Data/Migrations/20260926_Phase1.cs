using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace Cardflow.Api.Data.Migrations;

[DbContext(typeof(CardflowDbContext))]
[Migration("20260926_Phase1")]
public sealed class Phase1 : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("""
            CREATE TABLE boards (
                id uuid PRIMARY KEY,
                title varchar(120) NOT NULL,
                join_token varchar(64) NOT NULL UNIQUE,
                created_at timestamptz NOT NULL
            );
            CREATE TABLE board_members (
                id uuid PRIMARY KEY,
                board_id uuid NOT NULL REFERENCES boards(id) ON DELETE CASCADE,
                session_hash varchar(64) NOT NULL,
                nickname varchar(40) NOT NULL,
                colour varchar(7) NOT NULL,
                UNIQUE (board_id, session_hash)
            );
            CREATE TABLE columns (
                id uuid PRIMARY KEY,
                board_id uuid NOT NULL REFERENCES boards(id) ON DELETE CASCADE,
                title varchar(120) NOT NULL,
                rank varchar(256) COLLATE "C" NOT NULL,
                is_archived boolean NOT NULL
            );
            CREATE INDEX ix_columns_board_id_rank_id ON columns (board_id, rank, id);
            CREATE TABLE cards (
                id uuid PRIMARY KEY,
                column_id uuid NOT NULL REFERENCES columns(id) ON DELETE RESTRICT,
                title varchar(160) NOT NULL,
                description varchar(4000) NOT NULL,
                rank varchar(256) COLLATE "C" NOT NULL,
                version bigint NOT NULL,
                is_archived boolean NOT NULL
            );
            CREATE INDEX ix_cards_column_id_rank_id ON cards (column_id, rank, id);
            """);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("DROP TABLE cards; DROP TABLE columns; DROP TABLE board_members; DROP TABLE boards;");
    }
}
