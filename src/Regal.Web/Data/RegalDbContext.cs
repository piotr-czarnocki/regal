using Microsoft.EntityFrameworkCore;
using Regal.Web.Data.Entities;

namespace Regal.Web.Data;

public sealed class RegalDbContext(DbContextOptions<RegalDbContext> options) : DbContext(options)
{
    public DbSet<User> Users => Set<User>();

    public DbSet<AuditLogEntry> AuditLog => Set<AuditLogEntry>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<User>(user =>
        {
            user.ToTable("Users", table =>
                table.HasCheckConstraint("CK_Users_Email_Lowercase", "\"Email\" = lower(\"Email\")"));
            user.Property(u => u.Email).HasMaxLength(320);
            user.Property(u => u.Role).HasMaxLength(50);
            user.HasIndex(u => u.Email).IsUnique();
        });

        modelBuilder.Entity<AuditLogEntry>(entry =>
        {
            entry.ToTable("AuditLog");
            entry.Property(e => e.Action).HasMaxLength(100);
            entry.Property(e => e.EntityType).HasMaxLength(100);
            entry.Property(e => e.EntityId).HasMaxLength(100);
            entry.HasOne(e => e.User)
                .WithMany()
                .HasForeignKey(e => e.UserId)
                .OnDelete(DeleteBehavior.Restrict);
        });
    }
}
