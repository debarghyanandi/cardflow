using Cardflow.Api.Data.Entities;
using Microsoft.EntityFrameworkCore;

namespace Cardflow.Api.Data;

public sealed class CardflowDbContext(DbContextOptions<CardflowDbContext> options) : DbContext(options)
{
    public DbSet<Board> Boards => Set<Board>();
    public DbSet<BoardMember> BoardMembers => Set<BoardMember>();
    public DbSet<BoardColumn> Columns => Set<BoardColumn>();
    public DbSet<Card> Cards => Set<Card>();
    public DbSet<BoardEvent> BoardEvents => Set<BoardEvent>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Board>(entity =>
        {
            entity.ToTable("boards");
            entity.HasKey(board => board.Id);
            entity.Property(board => board.Id).HasColumnName("id");
            entity.Property(board => board.Title).HasColumnName("title").HasMaxLength(120).IsRequired();
            entity.Property(board => board.JoinToken).HasColumnName("join_token").HasMaxLength(64).IsRequired();
            entity.Property(board => board.CreatedAt).HasColumnName("created_at");
            entity.Property(board => board.EventSeq).HasColumnName("event_seq");
            entity.HasIndex(board => board.JoinToken).IsUnique();
        });

        modelBuilder.Entity<BoardMember>(entity =>
        {
            entity.ToTable("board_members");
            entity.HasKey(member => member.Id);
            entity.Property(member => member.Id).HasColumnName("id");
            entity.Property(member => member.BoardId).HasColumnName("board_id");
            entity.Property(member => member.SessionHash).HasColumnName("session_hash").HasMaxLength(64).IsRequired();
            entity.Property(member => member.Nickname).HasColumnName("nickname").HasMaxLength(40).IsRequired();
            entity.Property(member => member.Colour).HasColumnName("colour").HasMaxLength(7).IsRequired();
            entity.HasOne<Board>().WithMany().HasForeignKey(member => member.BoardId).OnDelete(DeleteBehavior.Cascade);
            entity.HasIndex(member => new { member.BoardId, member.SessionHash }).IsUnique();
        });

        modelBuilder.Entity<BoardColumn>(entity =>
        {
            entity.ToTable("columns");
            entity.HasKey(column => column.Id);
            entity.Property(column => column.Id).HasColumnName("id");
            entity.Property(column => column.BoardId).HasColumnName("board_id");
            entity.Property(column => column.Title).HasColumnName("title").HasMaxLength(120).IsRequired();
            entity.Property(column => column.Rank).HasColumnName("rank").UseCollation("C").IsRequired();
            entity.Property(column => column.IsArchived).HasColumnName("is_archived");
            entity.HasOne<Board>().WithMany().HasForeignKey(column => column.BoardId).OnDelete(DeleteBehavior.Cascade);
            entity.HasIndex(column => new { column.BoardId, column.Rank, column.Id });
        });

        modelBuilder.Entity<Card>(entity =>
        {
            entity.ToTable("cards");
            entity.HasKey(card => card.Id);
            entity.Property(card => card.Id).HasColumnName("id");
            entity.Property(card => card.ColumnId).HasColumnName("column_id");
            entity.Property(card => card.Title).HasColumnName("title").HasMaxLength(160).IsRequired();
            entity.Property(card => card.Description).HasColumnName("description").HasMaxLength(4000).IsRequired();
            entity.Property(card => card.Rank).HasColumnName("rank").UseCollation("C").IsRequired();
            entity.Property(card => card.Version).HasColumnName("version").IsConcurrencyToken();
            entity.Property(card => card.IsArchived).HasColumnName("is_archived");
            entity.HasOne<BoardColumn>().WithMany().HasForeignKey(card => card.ColumnId).OnDelete(DeleteBehavior.Restrict);
            entity.HasIndex(card => new { card.ColumnId, card.Rank, card.Id });
        });

        modelBuilder.Entity<BoardEvent>(entity =>
        {
            entity.ToTable("board_events");
            entity.HasKey(boardEvent => new { boardEvent.BoardId, boardEvent.Seq });
            entity.Property(boardEvent => boardEvent.BoardId).HasColumnName("board_id");
            entity.Property(boardEvent => boardEvent.Seq).HasColumnName("seq");
            entity.Property(boardEvent => boardEvent.Type).HasColumnName("type").HasMaxLength(60).IsRequired();
            entity.Property(boardEvent => boardEvent.Payload).HasColumnName("payload").HasColumnType("jsonb").IsRequired();
            entity.Property(boardEvent => boardEvent.ActorMemberId).HasColumnName("actor_member_id");
            entity.Property(boardEvent => boardEvent.CreatedAt).HasColumnName("created_at");
            entity.HasOne<Board>().WithMany().HasForeignKey(boardEvent => boardEvent.BoardId).OnDelete(DeleteBehavior.Cascade);
            entity.HasOne<BoardMember>().WithMany().HasForeignKey(boardEvent => boardEvent.ActorMemberId).OnDelete(DeleteBehavior.SetNull);
        });
    }
}
