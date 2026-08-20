using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using UltraSim.Portal.Models;

namespace UltraSim.Portal.Data;

public sealed class ApplicationDbContext : IdentityDbContext<ApplicationUser>
{
    public DbSet<ForumSection> ForumSections => Set<ForumSection>();
    public DbSet<ForumThread> ForumThreads => Set<ForumThread>();
    public DbSet<ForumPost> ForumPosts => Set<ForumPost>();
    public DbSet<ServerClaim> ServerClaims => Set<ServerClaim>();
    public DbSet<ServerVote> ServerVotes => Set<ServerVote>();
    public DbSet<AdminAuditRecord> AdminAuditRecords => Set<AdminAuditRecord>();

    public ApplicationDbContext(DbContextOptions<ApplicationDbContext> options)
        : base(options)
    {
    }

    protected override void OnModelCreating(ModelBuilder builder)
    {
        base.OnModelCreating(builder);

        builder.Entity<ForumSection>(entity =>
        {
            entity.HasKey(x => x.Id);
            entity.Property(x => x.Slug).HasMaxLength(120).IsRequired();
            entity.Property(x => x.Title).HasMaxLength(160).IsRequired();
            entity.Property(x => x.Description).HasMaxLength(2000).IsRequired();
            entity.Property(x => x.CreatedUtc).IsRequired();
            entity.HasIndex(x => x.Slug).IsUnique();
            entity.HasIndex(x => x.DisplayOrder);
        });

        builder.Entity<ForumThread>(entity =>
        {
            entity.HasKey(x => x.Id);
            entity.Property(x => x.AuthorUserId).HasMaxLength(450).IsRequired();
            entity.Property(x => x.LastPostUserId).HasMaxLength(450);
            entity.Property(x => x.Title).HasMaxLength(200).IsRequired();
            entity.Property(x => x.CreatedUtc).IsRequired();
            entity.Property(x => x.UpdatedUtc).IsRequired();
            entity.Property(x => x.LastPostedUtc).IsRequired();
            entity.HasIndex(x => x.SectionId);
            entity.HasIndex(x => x.AuthorUserId);
            entity.HasIndex(x => x.LastPostUserId);
            entity.HasIndex(x => new { x.SectionId, x.IsHidden, x.IsPinned, x.LastPostedUtc });
            entity.HasOne(x => x.Section)
                .WithMany(x => x.Threads)
                .HasForeignKey(x => x.SectionId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        builder.Entity<ForumPost>(entity =>
        {
            entity.HasKey(x => x.Id);
            entity.Property(x => x.AuthorUserId).HasMaxLength(450).IsRequired();
            entity.Property(x => x.Content).HasMaxLength(20000).IsRequired();
            entity.Property(x => x.IsHidden).IsRequired();
            entity.Property(x => x.CreatedUtc).IsRequired();
            entity.Property(x => x.UpdatedUtc).IsRequired();
            entity.HasIndex(x => x.ThreadId);
            entity.HasIndex(x => x.AuthorUserId);
            entity.HasIndex(x => new { x.ThreadId, x.IsHidden, x.CreatedUtc });
            entity.HasOne(x => x.Thread)
                .WithMany(x => x.Posts)
                .HasForeignKey(x => x.ThreadId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        builder.Entity<ServerClaim>(entity =>
        {
            entity.HasKey(x => x.Id);
            entity.Property(x => x.OwnerUserId).HasMaxLength(450).IsRequired();
            entity.Property(x => x.DisplayName).HasMaxLength(200).IsRequired();
            entity.Property(x => x.TargetHost).HasMaxLength(255).IsRequired();
            entity.Property(x => x.Description).HasMaxLength(2000);
            entity.Property(x => x.WebsiteUrl).HasMaxLength(512);
            entity.Property(x => x.DiscordUrl).HasMaxLength(512);
            entity.Property(x => x.RequestedServerId).HasMaxLength(200);
            entity.Property(x => x.OwnershipToken).HasMaxLength(512);
            entity.Property(x => x.LastError).HasMaxLength(1000);
            entity.HasIndex(x => x.OwnerUserId);
            entity.HasIndex(x => x.RequestedServerId);
        });

        builder.Entity<ServerVote>(entity =>
        {
            entity.HasKey(x => x.Id);
            entity.Property(x => x.UserId).HasMaxLength(450).IsRequired();
            entity.Property(x => x.ServerId).HasMaxLength(200).IsRequired();
            entity.HasIndex(x => x.UserId);
            entity.HasIndex(x => x.ServerId);
            entity.HasIndex(x => new { x.UserId, x.ServerId }).IsUnique();
        });

        builder.Entity<AdminAuditRecord>(entity =>
        {
            entity.HasKey(x => x.Id);
            entity.Property(x => x.ActorUserId).HasMaxLength(450).IsRequired();
            entity.Property(x => x.ActorEmail).HasMaxLength(256);
            entity.Property(x => x.ActorRole).HasMaxLength(64).IsRequired();
            entity.Property(x => x.ActionType).HasMaxLength(128).IsRequired();
            entity.Property(x => x.TargetType).HasMaxLength(64).IsRequired();
            entity.Property(x => x.TargetId).HasMaxLength(256);
            entity.Property(x => x.Reason).HasMaxLength(1000).IsRequired();
            entity.Property(x => x.Result).HasMaxLength(64).IsRequired();
            entity.Property(x => x.Details).HasMaxLength(2000);
            entity.Property(x => x.ActorIpAddress).HasMaxLength(45);
            entity.HasIndex(x => x.CreatedUtc);
            entity.HasIndex(x => x.ActorUserId);
        });

        builder.Entity<ApplicationUser>(entity =>
        {
            entity.Property(x => x.DisplayName).HasMaxLength(200);
            entity.Property(x => x.CreatedUtc).IsRequired();
            entity.Property(x => x.LastActiveUtc).IsRequired();
        });
    }
}
