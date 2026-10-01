using Microsoft.EntityFrameworkCore;
using TicketTracker.Api.Domain;

namespace TicketTracker.Api.Data;

public class AppDbContext(DbContextOptions<AppDbContext> options) : DbContext(options)
{
    public DbSet<Agent> Agents => Set<Agent>();
    public DbSet<Client> Clients => Set<Client>();
    public DbSet<Ticket> Tickets => Set<Ticket>();
    public DbSet<TicketComment> TicketComments => Set<TicketComment>();
    public DbSet<TicketStatusHistory> TicketStatusHistory => Set<TicketStatusHistory>();

    protected override void OnModelCreating(ModelBuilder model)
    {
        // Every foreign key uses DeleteBehavior.Restrict. There is no delete feature,
        // and the status history is an audit trail: deleting an agent or a ticket
        // must not silently take its history with it. (Restrict also avoids SQL
        // Server's "multiple cascade paths" error: TicketComment -> Ticket and
        // TicketComment -> Agent would otherwise both cascade.)

        model.Entity<Agent>(e =>
        {
            e.Property(a => a.Email).HasMaxLength(256);
            e.Property(a => a.DisplayName).HasMaxLength(100);
            e.Property(a => a.PasswordHash).HasMaxLength(200);
            e.HasIndex(a => a.Email).IsUnique();
        });

        model.Entity<Client>(e =>
        {
            e.Property(c => c.Name).HasMaxLength(200);
            e.Property(c => c.Email).HasMaxLength(256);
            // Enforces "one client per email" in the database, not just in code.
            // Two simultaneous requests for a new client can both pass a
            // "does it exist?" check in code; only a unique index truly prevents a duplicate.
            e.HasIndex(c => c.Email).IsUnique();
            // Supports the search query's  Name LIKE 'prefix%'  with an index seek.
            e.HasIndex(c => c.Name);
        });

        model.Entity<Ticket>(e =>
        {
            e.ToTable(t =>
            {
                // The enums are stored as ints; these stop a bad value (e.g. from a
                // hand-written UPDATE) from ever reaching the table.
                t.HasCheckConstraint("CK_Tickets_Status", "[Status] BETWEEN 0 AND 3");
                t.HasCheckConstraint("CK_Tickets_Priority", "[Priority] BETWEEN 0 AND 3");
                t.HasCheckConstraint("CK_Tickets_Category", "[Category] BETWEEN 0 AND 2");
            });

            e.Property(t => t.Title).HasMaxLength(200);
            e.Property(t => t.Description).HasMaxLength(4000);
            e.Property(t => t.ResolutionNotes).HasMaxLength(4000);
            e.Property(t => t.RowVersion).IsRowVersion();

            e.HasOne(t => t.Client).WithMany(c => c.Tickets)
                .HasForeignKey(t => t.ClientId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne(t => t.CreatedByAgent).WithMany()
                .HasForeignKey(t => t.CreatedByAgentId).OnDelete(DeleteBehavior.Restrict);

            // The queue's main query is:
            //   WHERE Status = @s ORDER BY Priority DESC, CreatedAt ASC
            // (most urgent first, then oldest first within a priority).
            // Key columns in exactly that order and direction let SQL Server seek to
            // the status and read rows already sorted: no Sort operator in the plan.
            // Mixed directions matter: an all-ascending index can be read backwards
            // as (Priority DESC, CreatedAt DESC), which is not the order we want.
            e.HasIndex(t => new { t.Status, t.Priority, t.CreatedAt })
                .IsDescending(false, true, false)
                .HasDatabaseName("IX_Tickets_Queue");

            // Note: the full-text index on (Title, Description) is NOT here; EF Core
            // cannot express full-text indexes. It lives in db/fulltext.sql.
        });

        model.Entity<TicketComment>(e =>
        {
            e.Property(c => c.Body).HasMaxLength(4000);
            e.HasOne(c => c.Ticket).WithMany(t => t.Comments)
                .HasForeignKey(c => c.TicketId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne(c => c.AuthorAgent).WithMany()
                .HasForeignKey(c => c.AuthorAgentId).OnDelete(DeleteBehavior.Restrict);
        });

        model.Entity<TicketStatusHistory>(e =>
        {
            e.Property(h => h.Note).HasMaxLength(1000);
            e.HasOne(h => h.Ticket).WithMany(t => t.StatusHistory)
                .HasForeignKey(h => h.TicketId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne(h => h.ChangedByAgent).WithMany()
                .HasForeignKey(h => h.ChangedByAgentId).OnDelete(DeleteBehavior.Restrict);
        });
    }
}
