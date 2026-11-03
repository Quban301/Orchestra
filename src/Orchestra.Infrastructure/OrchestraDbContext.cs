using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Orchestra.Domain;

namespace Orchestra.Infrastructure;

public class OrchestraDbContext(DbContextOptions<OrchestraDbContext> options) : DbContext(options)
{
    public DbSet<WorkflowDefinition> WorkflowDefinitions => Set<WorkflowDefinition>();
    public DbSet<WorkflowRun> WorkflowRuns => Set<WorkflowRun>();
    public DbSet<TaskRun> TaskRuns => Set<TaskRun>();

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    protected override void OnModelCreating(ModelBuilder b)
    {
        b.Entity<WorkflowDefinition>(e =>
        {
            e.ToTable("workflow_definitions");
            e.HasKey(x => x.Id);
            e.Property(x => x.Name).HasMaxLength(200).IsRequired();
            e.HasIndex(x => new { x.Name, x.Version }).IsUnique();

            var comparer = new ValueComparer<List<TaskDefinition>>(
                (a, c) => JsonSerializer.Serialize(a, Json) == JsonSerializer.Serialize(c, Json),
                v => JsonSerializer.Serialize(v, Json).GetHashCode(),
                v => JsonSerializer.Deserialize<List<TaskDefinition>>(JsonSerializer.Serialize(v, Json), Json)!);

            e.Property(x => x.Tasks)
                .HasColumnType("jsonb")
                .HasConversion(
                    v => JsonSerializer.Serialize(v, Json),
                    v => JsonSerializer.Deserialize<List<TaskDefinition>>(v, Json) ?? new())
                .Metadata.SetValueComparer(comparer);
        });

        b.Entity<WorkflowRun>(e =>
        {
            e.ToTable("workflow_runs");
            e.HasKey(x => x.Id);
            e.Property(x => x.Status).HasConversion<string>().HasMaxLength(20);
            e.HasIndex(x => x.Status);
            e.HasIndex(x => x.CreatedAt);
            e.HasMany(x => x.Tasks).WithOne().HasForeignKey(t => t.WorkflowRunId).OnDelete(DeleteBehavior.Cascade);
        });

        b.Entity<TaskRun>(e =>
        {
            e.ToTable("task_runs");
            e.HasKey(x => x.Id);
            e.Property(x => x.Name).HasMaxLength(200).IsRequired();
            e.Property(x => x.Handler).HasMaxLength(200).IsRequired();
            e.Property(x => x.Status).HasConversion<string>().HasMaxLength(20);
            e.Property(x => x.DependsOn).HasColumnType("text[]");
            e.Property(x => x.LeaseOwner).HasMaxLength(200);

            // Optimistic concurrency via Postgres' built-in xmin system column, so the scheduler and
            // workers can never silently overwrite each other's updates.
            e.Property<uint>("xmin").HasColumnType("xid").ValueGeneratedOnAddOrUpdate().IsConcurrencyToken();

            e.HasIndex(x => new { x.WorkflowRunId, x.Name }).IsUnique();
            e.HasIndex(x => new { x.Status, x.NextAttemptAt });   // worker claim query
            e.HasIndex(x => x.LeaseExpiresAt);                    // lease reaper
        });
    }
}
