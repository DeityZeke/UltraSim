using UltraSim.Portal.Authorization;
using UltraSim.Portal.Data;
using UltraSim.Portal.Models;
using UltraSim.Portal.Services;
using UltraSim.Portal.Tests.TestHelpers;
using Xunit;

namespace UltraSim.Portal.Tests.Services;

public sealed class ForumIdentityServiceTests
{
    [Fact]
    public async Task GetProfilesAsync_EmptyIds_ReturnsEmpty()
    {
        ApplicationDbContext db = PortalDbContextFactory.CreateInMemory();
        ForumIdentityService service = new(db);
        Dictionary<string, ForumAuthorProfile> result = await service.GetProfilesAsync([]);
        Assert.Empty(result);
    }

    [Fact]
    public async Task GetProfilesAsync_UnknownUserId_ReturnsEmpty()
    {
        ApplicationDbContext db = PortalDbContextFactory.CreateInMemory();
        ForumIdentityService service = new(db);
        Dictionary<string, ForumAuthorProfile> result = await service.GetProfilesAsync(["nonexistent"]);
        Assert.Empty(result);
    }

    [Fact]
    public async Task GetProfilesAsync_UserWithDisplayName_ReturnsDisplayName()
    {
        ApplicationDbContext db = PortalDbContextFactory.CreateInMemory();
        ApplicationUser user = new() { Id = "u1", UserName = "testuser", Email = "test@test.com", DisplayName = "Test User" };
        db.Users.Add(user);
        await db.SaveChangesAsync();

        ForumIdentityService service = new(db);
        Dictionary<string, ForumAuthorProfile> profiles = await service.GetProfilesAsync(["u1"]);

        Assert.True(profiles.ContainsKey("u1"));
        Assert.Equal("Test User", profiles["u1"].DisplayName);
    }

    [Fact]
    public async Task GetProfilesAsync_NoDisplayName_FallsBackToUserName()
    {
        ApplicationDbContext db = PortalDbContextFactory.CreateInMemory();
        ApplicationUser user = new() { Id = "u1", UserName = "testuser", Email = "test@test.com", DisplayName = null };
        db.Users.Add(user);
        await db.SaveChangesAsync();

        ForumIdentityService service = new(db);
        Dictionary<string, ForumAuthorProfile> profiles = await service.GetProfilesAsync(["u1"]);

        Assert.Equal("testuser", profiles["u1"].DisplayName);
    }

    [Fact]
    public async Task GetProfilesAsync_NoDisplayNameOrUserName_FallsBackToEmail()
    {
        ApplicationDbContext db = PortalDbContextFactory.CreateInMemory();
        ApplicationUser user = new() { Id = "u1", UserName = null, Email = "test@test.com", DisplayName = null };
        db.Users.Add(user);
        await db.SaveChangesAsync();

        ForumIdentityService service = new(db);
        Dictionary<string, ForumAuthorProfile> profiles = await service.GetProfilesAsync(["u1"]);

        Assert.Equal("test@test.com", profiles["u1"].DisplayName);
    }

    [Fact]
    public async Task GetProfilesAsync_NoDisplayNameOrUserNameOrEmail_FallsBackToUserId()
    {
        ApplicationDbContext db = PortalDbContextFactory.CreateInMemory();
        ApplicationUser user = new() { Id = "u1", UserName = null, Email = null, DisplayName = null };
        db.Users.Add(user);
        await db.SaveChangesAsync();

        ForumIdentityService service = new(db);
        Dictionary<string, ForumAuthorProfile> profiles = await service.GetProfilesAsync(["u1"]);

        Assert.Equal("u1", profiles["u1"].DisplayName);
    }

    [Fact]
    public async Task GetProfilesAsync_IsPrimaryOwner_ReturnsPrimaryOwnerBadge()
    {
        ApplicationDbContext db = PortalDbContextFactory.CreateInMemory();
        ApplicationUser user = new() { Id = "u1", UserName = "owner", IsPrimaryOwner = true };
        db.Users.Add(user);
        await db.SaveChangesAsync();

        ForumIdentityService service = new(db);
        Dictionary<string, ForumAuthorProfile> profiles = await service.GetProfilesAsync(["u1"]);

        Assert.Equal(PortalRoles.PrimaryOwner, profiles["u1"].EffectiveRole);
        Assert.Equal("Primary Owner", profiles["u1"].BadgeLabel);
        Assert.Equal("forum-role-primary-owner", profiles["u1"].BadgeCssClass);
    }

    [Fact]
    public async Task GetProfilesAsync_RegularUser_ReturnsUserCssClass()
    {
        ApplicationDbContext db = PortalDbContextFactory.CreateInMemory();
        ApplicationUser user = new() { Id = "u1", UserName = "user" };
        db.Users.Add(user);
        await db.SaveChangesAsync();

        ForumIdentityService service = new(db);
        Dictionary<string, ForumAuthorProfile> profiles = await service.GetProfilesAsync(["u1"]);

        Assert.Equal("forum-role-user", profiles["u1"].BadgeCssClass);
        Assert.Null(profiles["u1"].BadgeLabel);
    }

    [Fact]
    public async Task GetProfilesAsync_NullIdsFiltered()
    {
        ApplicationDbContext db = PortalDbContextFactory.CreateInMemory();
        ForumIdentityService service = new(db);
        // Should not throw — nulls/whitespace are filtered
        Dictionary<string, ForumAuthorProfile> result = await service.GetProfilesAsync([null, "  ", null]);
        Assert.Empty(result);
    }

    [Fact]
    public async Task GetProfilesAsync_DuplicateIds_QueriedOnce()
    {
        ApplicationDbContext db = PortalDbContextFactory.CreateInMemory();
        ApplicationUser user = new() { Id = "u1", UserName = "testuser" };
        db.Users.Add(user);
        await db.SaveChangesAsync();

        ForumIdentityService service = new(db);
        Dictionary<string, ForumAuthorProfile> profiles = await service.GetProfilesAsync(["u1", "u1", "u1"]);

        // Should have exactly one entry, not throw
        Assert.Single(profiles);
    }
}
