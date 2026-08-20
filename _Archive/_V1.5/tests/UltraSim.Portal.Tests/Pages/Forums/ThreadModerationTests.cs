using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using UltraSim.Portal.Authorization;
using UltraSim.Portal.Data;
using UltraSim.Portal.Models;
using UltraSim.Portal.Pages.Forums;
using UltraSim.Portal.Services;
using UltraSim.Portal.Tests.TestHelpers;
using Xunit;

namespace UltraSim.Portal.Tests.Pages.Forums;

public sealed class ThreadModerationTests
{
    // --- ToggleLock ---

    [Fact]
    public async Task ToggleLock_NonModerator_ReturnsForbid()
    {
        ApplicationDbContext db = PortalDbContextFactory.CreateInMemory();
        ForumSection section = ForumTestHelpers.AddSection(db);
        ForumThread thread = ForumTestHelpers.AddThread(db, section.Id, "user-1");

        ThreadModel page = MakePage(db, "user-1"); // no moderator role
        IActionResult result = await page.OnPostToggleLockAsync(section.Slug, thread.Id, 1, "reason", CancellationToken.None);

        Assert.IsType<ForbidResult>(result);
    }

    [Fact]
    public async Task ToggleLock_NoReason_DoesNotToggle()
    {
        ApplicationDbContext db = PortalDbContextFactory.CreateInMemory();
        ForumSection section = ForumTestHelpers.AddSection(db);
        ForumThread thread = ForumTestHelpers.AddThread(db, section.Id, "user-1", isLocked: false);

        ThreadModel page = MakePage(db, "mod-1", PortalRoles.Moderator);
        await page.OnPostToggleLockAsync(section.Slug, thread.Id, 1, null, CancellationToken.None);

        db.ChangeTracker.Clear();
        ForumThread? t = await db.ForumThreads.FindAsync(thread.Id);
        Assert.False(t!.IsLocked); // unchanged
    }

    [Fact]
    public async Task ToggleLock_WithReason_TogglesLocked()
    {
        ApplicationDbContext db = PortalDbContextFactory.CreateInMemory();
        ForumSection section = ForumTestHelpers.AddSection(db);
        ForumThread thread = ForumTestHelpers.AddThread(db, section.Id, "user-1", isLocked: false);

        ThreadModel page = MakePage(db, "mod-1", PortalRoles.Moderator);
        await page.OnPostToggleLockAsync(section.Slug, thread.Id, 1, "off-topic", CancellationToken.None);

        db.ChangeTracker.Clear();
        ForumThread? t = await db.ForumThreads.FindAsync(thread.Id);
        Assert.True(t!.IsLocked);
    }

    [Fact]
    public async Task ToggleLock_AlreadyLocked_Unlocks()
    {
        ApplicationDbContext db = PortalDbContextFactory.CreateInMemory();
        ForumSection section = ForumTestHelpers.AddSection(db);
        ForumThread thread = ForumTestHelpers.AddThread(db, section.Id, "user-1", isLocked: true);

        ThreadModel page = MakePage(db, "mod-1", PortalRoles.Moderator);
        await page.OnPostToggleLockAsync(section.Slug, thread.Id, 1, "re-open", CancellationToken.None);

        db.ChangeTracker.Clear();
        ForumThread? t = await db.ForumThreads.FindAsync(thread.Id);
        Assert.False(t!.IsLocked);
    }

    // --- TogglePin ---

    [Fact]
    public async Task TogglePin_WithReason_TogglesPin()
    {
        ApplicationDbContext db = PortalDbContextFactory.CreateInMemory();
        ForumSection section = ForumTestHelpers.AddSection(db);
        ForumThread thread = ForumTestHelpers.AddThread(db, section.Id, "user-1", isPinned: false);

        ThreadModel page = MakePage(db, "mod-1", PortalRoles.Moderator);
        await page.OnPostTogglePinAsync(section.Slug, thread.Id, 1, "important", CancellationToken.None);

        db.ChangeTracker.Clear();
        ForumThread? t = await db.ForumThreads.FindAsync(thread.Id);
        Assert.True(t!.IsPinned);
    }

    [Fact]
    public async Task TogglePin_NonModerator_ReturnsForbid()
    {
        ApplicationDbContext db = PortalDbContextFactory.CreateInMemory();
        ForumSection section = ForumTestHelpers.AddSection(db);
        ForumThread thread = ForumTestHelpers.AddThread(db, section.Id, "user-1");

        ThreadModel page = MakePage(db, "user-1");
        IActionResult result = await page.OnPostTogglePinAsync(section.Slug, thread.Id, 1, "reason", CancellationToken.None);

        Assert.IsType<ForbidResult>(result);
    }

    // --- ToggleHidden (thread) ---

    [Fact]
    public async Task ToggleHidden_WithReason_HidesThread()
    {
        ApplicationDbContext db = PortalDbContextFactory.CreateInMemory();
        ForumSection section = ForumTestHelpers.AddSection(db);
        ForumThread thread = ForumTestHelpers.AddThread(db, section.Id, "user-1", isHidden: false);

        ThreadModel page = MakePage(db, "mod-1", PortalRoles.Moderator);
        await page.OnPostToggleHiddenAsync(section.Slug, thread.Id, 1, "spam", CancellationToken.None);

        db.ChangeTracker.Clear();
        ForumThread? t = await db.ForumThreads.FindAsync(thread.Id);
        Assert.True(t!.IsHidden);
    }

    [Fact]
    public async Task ToggleHidden_AlreadyHidden_UnhidesThread()
    {
        ApplicationDbContext db = PortalDbContextFactory.CreateInMemory();
        ForumSection section = ForumTestHelpers.AddSection(db);
        ForumThread thread = ForumTestHelpers.AddThread(db, section.Id, "user-1", isHidden: true);

        ThreadModel page = MakePage(db, "mod-1", PortalRoles.Moderator);
        await page.OnPostToggleHiddenAsync(section.Slug, thread.Id, 1, "restored", CancellationToken.None);

        db.ChangeTracker.Clear();
        ForumThread? t = await db.ForumThreads.FindAsync(thread.Id);
        Assert.False(t!.IsHidden);
    }

    // --- TogglePostHidden ---

    [Fact]
    public async Task TogglePostHidden_WithReason_HidesPost()
    {
        ApplicationDbContext db = PortalDbContextFactory.CreateInMemory();
        ForumSection section = ForumTestHelpers.AddSection(db);
        ForumThread thread = ForumTestHelpers.AddThread(db, section.Id, "user-1");
        ForumPost post = ForumTestHelpers.AddPost(db, thread.Id, "user-1", isHidden: false);

        ThreadModel page = MakePage(db, "mod-1", PortalRoles.Moderator);
        await page.OnPostTogglePostHiddenAsync(section.Slug, thread.Id, post.Id, 1, "inappropriate", CancellationToken.None);

        db.ChangeTracker.Clear();
        ForumPost? p = await db.ForumPosts.FindAsync(post.Id);
        Assert.True(p!.IsHidden);
    }

    [Fact]
    public async Task TogglePostHidden_NonModerator_ReturnsForbid()
    {
        ApplicationDbContext db = PortalDbContextFactory.CreateInMemory();
        ForumSection section = ForumTestHelpers.AddSection(db);
        ForumThread thread = ForumTestHelpers.AddThread(db, section.Id, "user-1");
        ForumPost post = ForumTestHelpers.AddPost(db, thread.Id, "user-2");

        ThreadModel page = MakePage(db, "user-1");
        IActionResult result = await page.OnPostTogglePostHiddenAsync(section.Slug, thread.Id, post.Id, 1, "reason", CancellationToken.None);

        Assert.IsType<ForbidResult>(result);
    }

    [Fact]
    public async Task TogglePostHidden_NoReason_DoesNotToggle()
    {
        ApplicationDbContext db = PortalDbContextFactory.CreateInMemory();
        ForumSection section = ForumTestHelpers.AddSection(db);
        ForumThread thread = ForumTestHelpers.AddThread(db, section.Id, "user-1");
        ForumPost post = ForumTestHelpers.AddPost(db, thread.Id, "user-1", isHidden: false);

        ThreadModel page = MakePage(db, "mod-1", PortalRoles.Moderator);
        await page.OnPostTogglePostHiddenAsync(section.Slug, thread.Id, post.Id, 1, "", CancellationToken.None);

        db.ChangeTracker.Clear();
        ForumPost? p = await db.ForumPosts.FindAsync(post.Id);
        Assert.False(p!.IsHidden); // unchanged
    }

    // --- Audit logging for moderation actions ---

    [Fact]
    public async Task ToggleLock_WithReason_WritesAuditRecord()
    {
        string dbName = Guid.NewGuid().ToString();
        ApplicationDbContext db = PortalDbContextFactory.CreateInMemory(dbName);
        (AdminAuditService audit, ApplicationDbContext auditDb) = ForumTestHelpers.MakeAuditService(dbName);
        ForumSection section = ForumTestHelpers.AddSection(db);
        ForumThread thread = ForumTestHelpers.AddThread(db, section.Id, "user-1");

        ApplicationUser actor = new() { Id = "mod-1", Email = "mod@test.com" };
        var userManager = ForumTestHelpers.MakeUserManagerWithActor(actor);
        ForumIdentityService identityService = new(db);

        ThreadModel page = new(db, identityService, userManager.Object, audit)
        {
            PageContext = ForumTestHelpers.MakePageContext(ForumTestHelpers.MakeUser("mod-1", PortalRoles.Moderator)),
            TempData = ForumTestHelpers.MakeTempData(),
        };

        await page.OnPostToggleLockAsync(section.Slug, thread.Id, 1, "off-topic", CancellationToken.None);

        UltraSim.Portal.Models.AdminAuditRecord record = Assert.Single(await auditDb.AdminAuditRecords.ToListAsync());
        Assert.Equal("forum.thread.lock", record.ActionType);
        Assert.Equal("mod-1", record.ActorUserId);
    }

    private static ThreadModel MakePage(ApplicationDbContext db, string userId, params string[] roles)
    {
        ApplicationUser actor = new() { Id = userId };
        var userManager = ForumTestHelpers.MakeUserManagerWithActor(actor);
        (AdminAuditService audit, _) = ForumTestHelpers.MakeAuditService();
        ForumIdentityService identityService = new(db);
        return new ThreadModel(db, identityService, userManager.Object, audit)
        {
            PageContext = ForumTestHelpers.MakePageContext(ForumTestHelpers.MakeUser(userId, roles)),
            TempData = ForumTestHelpers.MakeTempData(),
        };
    }
}
