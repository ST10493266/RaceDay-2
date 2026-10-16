using Microsoft.EntityFrameworkCore;
using RaceDay.Api.Models;

namespace RaceDay.Api.Data;

public class RaceDayDbContext : DbContext
{
    public RaceDayDbContext(DbContextOptions<RaceDayDbContext> options) : base(options) { }

    public DbSet<Role> Roles => Set<Role>();
    public DbSet<AppUser> Users => Set<AppUser>();
    public DbSet<Event> Events => Set<Event>();
    public DbSet<Category> Categories => Set<Category>();
    public DbSet<Enrolment> Enrolments => Set<Enrolment>();
    public DbSet<Result> Results => Set<Result>();

    // Table, column, index and constraint names mirror docs/raceday.sql.
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Role>(e =>
        {
            e.ToTable("Roles");
            e.HasKey(r => r.RoleId);
            e.Property(r => r.RoleName).IsRequired().HasMaxLength(50);
            e.HasIndex(r => r.RoleName).IsUnique();

            // The only two roles in the system. Ids are fixed so seed data / tests can rely on them.
            e.HasData(
                new Role { RoleId = 1, RoleName = RoleNames.Organiser },
                new Role { RoleId = 2, RoleName = RoleNames.Participant });
        });

        modelBuilder.Entity<AppUser>(e =>
        {
            e.ToTable("Users");
            e.HasKey(u => u.UserId);
            e.Property(u => u.FirstName).IsRequired().HasMaxLength(100);
            e.Property(u => u.LastName).IsRequired().HasMaxLength(100);
            e.Property(u => u.Email).IsRequired().HasMaxLength(256);
            e.Property(u => u.PasswordHash).IsRequired().HasMaxLength(255);
            e.Property(u => u.PhoneNumber).HasMaxLength(20);
            e.Property(u => u.DateOfBirth).HasColumnType("date");
            e.Property(u => u.CreatedAt).HasDefaultValueSql("SYSUTCDATETIME()");
            e.HasIndex(u => u.Email).IsUnique();

            e.HasOne(u => u.Role)
                .WithMany(r => r.Users)
                .HasForeignKey(u => u.RoleId)
                .OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<Event>(e =>
        {
            e.ToTable("Events", t =>
            {
                t.HasCheckConstraint("CK_Events_EventType", "[EventType] IN (N'Run', N'Walk', N'Cycle')");
                t.HasCheckConstraint("CK_Events_DistanceKm", "[DistanceKm] > 0");
            });
            e.HasKey(ev => ev.EventId);
            e.Property(ev => ev.Name).IsRequired().HasMaxLength(150);
            e.Property(ev => ev.Description).IsRequired().HasMaxLength(1000);
            e.Property(ev => ev.Location).IsRequired().HasMaxLength(200);
            e.Property(ev => ev.DistanceKm).HasPrecision(6, 2);
            e.Property(ev => ev.EventType).IsRequired().HasMaxLength(10);
            e.Property(ev => ev.CreatedAt).HasDefaultValueSql("SYSUTCDATETIME()");

            e.HasOne(ev => ev.Organiser)
                .WithMany(u => u.OrganisedEvents)
                .HasForeignKey(ev => ev.OrganiserId)
                .OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<Category>(e =>
        {
            e.ToTable("Categories", t =>
                t.HasCheckConstraint("CK_Categories_AgeRange",
                    "[MinAge] IS NULL OR [MaxAge] IS NULL OR [MinAge] <= [MaxAge]"));
            e.HasKey(c => c.CategoryId);
            e.Property(c => c.Name).IsRequired().HasMaxLength(100);
            e.Property(c => c.DistanceKm).HasPrecision(6, 2);
            e.HasIndex(c => new { c.EventId, c.Name }).IsUnique();

            e.HasOne(c => c.Event)
                .WithMany(ev => ev.Categories)
                .HasForeignKey(c => c.EventId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<Enrolment>(e =>
        {
            e.ToTable("Enrolments");
            e.HasKey(en => en.EnrolmentId);
            e.Property(en => en.EnrolledAt).HasDefaultValueSql("SYSUTCDATETIME()");
            // A participant can enter a given event only once.
            e.HasIndex(en => new { en.UserId, en.EventId }).IsUnique();

            e.HasOne(en => en.User)
                .WithMany(u => u.Enrolments)
                .HasForeignKey(en => en.UserId)
                .OnDelete(DeleteBehavior.Restrict);

            // Event -> Enrolment is the single cascade path (avoids SQL Server "multiple cascade paths").
            e.HasOne(en => en.Event)
                .WithMany(ev => ev.Enrolments)
                .HasForeignKey(en => en.EventId)
                .OnDelete(DeleteBehavior.Cascade);

            e.HasOne(en => en.Category)
                .WithMany(c => c.Enrolments)
                .HasForeignKey(en => en.CategoryId)
                .OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<Result>(e =>
        {
            e.ToTable("Results", t => t.HasCheckConstraint("CK_Results_Position", "[Position] > 0"));
            e.HasKey(r => r.ResultId);
            e.Property(r => r.FinishTime).HasColumnType("time(0)");
            e.Property(r => r.RecordedAt).HasDefaultValueSql("SYSUTCDATETIME()");

            // One result per enrolment (unique index IX_Results_EnrolmentId).
            e.HasOne(r => r.Enrolment)
                .WithOne(en => en.Result)
                .HasForeignKey<Result>(r => r.EnrolmentId)
                .OnDelete(DeleteBehavior.Cascade);
        });
    }
}
