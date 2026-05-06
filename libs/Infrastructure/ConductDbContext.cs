using Conduct.Domain.Audit;
using Conduct.Domain.Authorization;
using Conduct.Domain.CaseTypes;
using Conduct.Domain.Cases;
using Conduct.Domain.Identity;
using Conduct.Domain.Lobs;
using Conduct.Domain.Parties;
using Conduct.Infrastructure.Outbox;
using Microsoft.EntityFrameworkCore;

namespace Conduct.Infrastructure;

public class ConductDbContext(DbContextOptions<ConductDbContext> options) : DbContext(options)
{
    // Cases
    public DbSet<Case> Cases => Set<Case>();
    public DbSet<CaseParty> CaseParties => Set<CaseParty>();
    public DbSet<CaseNote> CaseNotes => Set<CaseNote>();
    public DbSet<CaseType> CaseTypes => Set<CaseType>();

    // LOBs
    public DbSet<Lob> Lobs => Set<Lob>();

    // Parties + per-kind profiles
    public DbSet<Party> Parties => Set<Party>();
    public DbSet<EmployeeProfile> EmployeeProfiles => Set<EmployeeProfile>();
    public DbSet<CustomerProfile> CustomerProfiles => Set<CustomerProfile>();
    public DbSet<VendorProfile> VendorProfiles => Set<VendorProfile>();
    public DbSet<ExternalProfile> ExternalProfiles => Set<ExternalProfile>();

    // Identity
    public DbSet<User> Users => Set<User>();

    // RBAC
    public DbSet<Role> Roles => Set<Role>();
    public DbSet<Group> Groups => Set<Group>();
    public DbSet<GroupMembership> GroupMemberships => Set<GroupMembership>();
    public DbSet<Assignment> Assignments => Set<Assignment>();

    // Audit + outbox
    public DbSet<AuditEvent> AuditEvents => Set<AuditEvent>();
    public DbSet<OutboxMessage> Outbox => Set<OutboxMessage>();

    protected override void OnModelCreating(ModelBuilder b)
    {
        b.HasPostgresExtension("vector"); // pgvector for future RAG features

        // -------- Cases --------
        b.Entity<Case>(e =>
        {
            e.HasKey(x => x.Id);
            e.Property(x => x.Title).HasMaxLength(512);
            e.Property(x => x.CaseNumber).HasMaxLength(64);
            e.Property(x => x.Status).HasMaxLength(64);
            e.Property(x => x.ClosureReason).HasMaxLength(64);
            e.Property(x => x.DataJson).HasColumnType("jsonb");
            e.Property(x => x.ExternalRefsJson).HasColumnType("jsonb");
            e.HasIndex(x => new { x.TenantId, x.CaseNumber }).IsUnique();
            e.HasIndex(x => new { x.TenantId, x.OwnerLobId });
            e.HasIndex(x => new { x.TenantId, x.CaseTypeId });
            e.HasIndex(x => new { x.TenantId, x.Status });
        });

        b.Entity<CaseParty>(e =>
        {
            e.HasKey(x => x.Id);
            e.Property(x => x.RoleOnCase).HasMaxLength(64);
            e.Property(x => x.DataJson).HasColumnType("jsonb");
            e.HasIndex(x => new { x.TenantId, x.CaseId });
            e.HasIndex(x => new { x.TenantId, x.PartyId });
        });

        b.Entity<CaseNote>(e =>
        {
            e.HasKey(x => x.Id);
            e.Property(x => x.Content).HasColumnType("text");
            e.HasIndex(x => new { x.TenantId, x.CaseId, x.CreatedAt });
        });

        b.Entity<CaseType>(e =>
        {
            e.HasKey(x => x.Id);
            e.Property(x => x.Key).HasMaxLength(64);
            e.Property(x => x.Name).HasMaxLength(256);
            e.Property(x => x.NumberFormat).HasMaxLength(256);
            e.Property(x => x.FieldsSchemaJson).HasColumnType("jsonb");
            e.Property(x => x.PartyDataSchemasJson).HasColumnType("jsonb");
            e.Property(x => x.LifecycleJson).HasColumnType("jsonb");
            e.Property(x => x.TransferRulesJson).HasColumnType("jsonb");
            e.Property(x => x.NotesLifecyclePolicyJson).HasColumnType("jsonb");
            e.HasIndex(x => new { x.TenantId, x.Key }).IsUnique();
        });

        // -------- LOBs (adjacency tree) --------
        b.Entity<Lob>(e =>
        {
            e.HasKey(x => x.Id);
            e.Property(x => x.Name).HasMaxLength(256);
            e.Property(x => x.ShortCode).HasMaxLength(32);
            e.HasOne(x => x.Parent).WithMany(x => x.Children).HasForeignKey(x => x.ParentLobId).OnDelete(DeleteBehavior.Restrict);
            e.HasIndex(x => new { x.TenantId, x.ShortCode }).IsUnique();
            e.HasIndex(x => new { x.TenantId, x.ParentLobId });
        });

        // -------- Parties --------
        b.Entity<Party>(e =>
        {
            e.HasKey(x => x.Id);
            e.Property(x => x.DisplayName).HasMaxLength(512);
            e.HasIndex(x => new { x.TenantId, x.IdentityKind });
        });

        b.Entity<EmployeeProfile>(e =>
        {
            e.HasKey(x => x.Id);
            e.HasIndex(x => x.PartyId).IsUnique();
            e.Property(x => x.EmployeeId).HasMaxLength(128);
            e.HasIndex(x => new { x.TenantId, x.EmployeeId }).IsUnique();
        });

        b.Entity<CustomerProfile>(e =>
        {
            e.HasKey(x => x.Id);
            e.HasIndex(x => x.PartyId).IsUnique();
            e.Property(x => x.CustomerId).HasMaxLength(128);
            e.HasIndex(x => new { x.TenantId, x.CustomerId }).IsUnique();
        });

        b.Entity<VendorProfile>(e =>
        {
            e.HasKey(x => x.Id);
            e.HasIndex(x => x.PartyId).IsUnique();
            e.Property(x => x.VendorId).HasMaxLength(128);
            e.HasIndex(x => new { x.TenantId, x.VendorId }).IsUnique();
        });

        b.Entity<ExternalProfile>(e =>
        {
            e.HasKey(x => x.Id);
            e.HasIndex(x => x.PartyId).IsUnique();
        });

        // -------- Identity --------
        b.Entity<User>(e =>
        {
            e.HasKey(x => x.Id);
            e.Property(x => x.KeycloakSub).HasMaxLength(256);
            e.Property(x => x.Username).HasMaxLength(256);
            e.Property(x => x.Email).HasMaxLength(512);
            e.HasIndex(x => new { x.TenantId, x.KeycloakSub }).IsUnique();
            e.HasIndex(x => new { x.TenantId, x.Email });
        });

        // -------- RBAC --------
        b.Entity<Role>(e =>
        {
            e.HasKey(x => x.Id);
            e.Property(x => x.Name).HasMaxLength(128);
            e.Property(x => x.Permissions).HasColumnType("text[]");
            e.HasIndex(x => new { x.TenantId, x.Name }).IsUnique();
        });

        b.Entity<Group>(e =>
        {
            e.HasKey(x => x.Id);
            e.Property(x => x.Name).HasMaxLength(256);
            e.HasIndex(x => new { x.TenantId, x.Name }).IsUnique();
        });

        b.Entity<GroupMembership>(e =>
        {
            e.HasKey(x => x.Id);
            e.HasIndex(x => new { x.TenantId, x.GroupId, x.UserId }).IsUnique();
            e.HasIndex(x => new { x.TenantId, x.UserId });
        });

        b.Entity<Assignment>(e =>
        {
            e.HasKey(x => x.Id);
            e.HasIndex(x => new { x.TenantId, x.SubjectType, x.SubjectId });
            e.HasIndex(x => new { x.TenantId, x.RoleId });
            e.HasIndex(x => new { x.TenantId, x.ScopeType, x.ScopeId });
            // Natural-key uniqueness — prevents duplicate assignments under racing seeders / admin double-clicks.
            e.HasIndex(x => new { x.TenantId, x.SubjectType, x.SubjectId, x.RoleId, x.ScopeType, x.ScopeId }).IsUnique();
        });

        // -------- Audit --------
        b.Entity<AuditEvent>(e =>
        {
            e.HasKey(x => x.Id);
            e.Property(x => x.Actor).HasMaxLength(256);
            e.Property(x => x.EntityType).HasMaxLength(128);
            e.Property(x => x.Action).HasMaxLength(64);
            e.Property(x => x.ChangeSetJson).HasColumnType("jsonb");
            e.Property(x => x.ContextJson).HasColumnType("jsonb");
            e.HasIndex(x => new { x.TenantId, x.EntityType, x.EntityId, x.OccurredAt });
            e.HasIndex(x => new { x.TenantId, x.OccurredAt });
        });

        // -------- Outbox --------
        b.Entity<OutboxMessage>(e =>
        {
            e.HasKey(x => x.Id);
            e.Property(x => x.Topic).HasMaxLength(256);
            e.Property(x => x.Key).HasMaxLength(256);
            e.Property(x => x.PayloadJson).HasColumnType("jsonb");
            e.HasIndex(x => new { x.PublishedAt, x.CreatedAt });
            e.HasIndex(x => x.TenantId);
        });
    }
}
