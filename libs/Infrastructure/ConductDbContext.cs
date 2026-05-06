using Conduct.Domain;
using Conduct.Infrastructure.Outbox;
using Microsoft.EntityFrameworkCore;

namespace Conduct.Infrastructure;

public class ConductDbContext(DbContextOptions<ConductDbContext> options) : DbContext(options)
{
    public DbSet<Case> Cases => Set<Case>();
    public DbSet<OutboxMessage> Outbox => Set<OutboxMessage>();

    protected override void OnModelCreating(ModelBuilder b)
    {
        b.HasPostgresExtension("vector"); // pgvector

        b.Entity<Case>(e =>
        {
            e.HasKey(x => x.Id);
            e.Property(x => x.Title).HasMaxLength(512);
            e.HasIndex(x => x.TenantId);
        });

        b.Entity<OutboxMessage>(e =>
        {
            e.HasKey(x => x.Id);
            e.Property(x => x.Topic).HasMaxLength(256);
            e.Property(x => x.Key).HasMaxLength(256);
            e.Property(x => x.PayloadJson).HasColumnType("jsonb");
            e.HasIndex(x => new { x.PublishedAt, x.CreatedAt }); // relay scans pending rows in age order
            e.HasIndex(x => x.TenantId);
        });
    }
}
