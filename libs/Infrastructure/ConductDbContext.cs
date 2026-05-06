using Conduct.Domain;
using Microsoft.EntityFrameworkCore;

namespace Conduct.Infrastructure;

public class ConductDbContext(DbContextOptions<ConductDbContext> options) : DbContext(options)
{
    public DbSet<Case> Cases => Set<Case>();

    protected override void OnModelCreating(ModelBuilder b)
    {
        b.HasPostgresExtension("vector"); // pgvector
        b.Entity<Case>(e =>
        {
            e.HasKey(x => x.Id);
            e.Property(x => x.Title).HasMaxLength(512);
            e.HasIndex(x => x.TenantId);
        });
    }
}
