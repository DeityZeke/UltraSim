using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using UltraSim.Portal.Authorization;
using UltraSim.Portal.Data;
using UltraSim.Portal.Models;
using UltraSim.Portal.Pages.Forums;
using UltraSim.Portal.Services;
using UltraSim.Portal.Tests.TestHelpers;
using Xunit;

namespace UltraSim.Portal.Tests.Pages.Forums;

public sealed class DeleteThreadPageTests
{
    // --- CanDelete rules ---

    [Fact]
    public async Task OnGet_OwnThreadOnePost_CanDelete()
    {
        ApplicationDbContext db = PortalDbContextFactory.CreateInMemory();
        ForumSection section = ForumTestHelpers.AddSection(db);
        ForumThread thread = ForumTestHelpers.AddThread(db, section.Id, "user-1");
        ForumTestHelpers.AddPost(db, thread.Id, "user-1");

        DeleteThreadModel page = MakePage(db, "user-1");
        IActionResult result = await page.OnGetAsync(section.Slug, thread.Id, CancellationToken.None);

        Assert.IsType<PageResult>(result);
        Assert.True(page.CanDelete);
    }

    [Fact]
    public async Task OnGet_OwnThreadMultiplePosts_CannotDelete()
    {
        ApplicationDbContext db = PortalDbContextFactory.CreateInMemory();
        ForumSection section = ForumTestHelpers.AddSection(db);
        ForumThread thread = ForumTestHelpers.AddThread(db, section.Id, "user-1");
        ForumTestHelpers.AddPost(db, thread.Id, "user-1");
        ForumTestHelpers.AddPost(db, thread.Id, "user-2");

        DeleteThreadModel page = MakePage(db, "user-1");
        IActionResult result = await page.OnGetAsync(section.Slug, thread.Id, CancellationToken.None);

        Assert.IsType<ForbidResult>(result);
        Assert.False(page.CanDelete);
    }

    [Fact]
    public async Task OnGet_OtherUsersThread_NonModerator_CannotDelete()
    {
        ApplicationDbContext db = PortalDbContextFactory.CreateInMemory();
        ForumSection section = ForumTestHelpers.AddSection(db);
        ForumThread thread = ForumTestHelpers.AddThread(db, section.Id, "user-2");
        ForumTestHelpers.AddPost(db, thread.Id, "user-2");

        DeleteThreadModel page = MakePage(db, "user-1");
        IActionResult result = await page.OnGetAsync(section.Slug, thread.Id, CancellationToken.None);

        Assert.IsType<ForbidResult>(result);
        Assert.False(page.CanDelete);
    }

    [Fact]
    public async Task OnGet_ModeratorCanDeleteAnyThread()
    {
        ApplicationDbContext db = PortalDbContextFactory.CreateInMemory();
        ForumSection section = ForumTestHelpers.AddSection(db);
        ForumThread thread = ForumTestHelpers.AddThread(db, section.Id, "user-2");
        ForumTestHelpers.AddPost(db, thread.Id, "user-2");
        ForumTestHelpers.AddPost(db, thread.Id, "user-3");

        DeleteThreadModel page = MakePage(db, "mod-1", PortalRoles.Moderator);
        IActionResult result = await page.OnGetAsync(section.Slug, thread.Id, CancellationToken.None);

        Assert.IsType<PageResult>(result);
        Assert.True(page.CanDelete);
    }

    [Fact]
    public async Task OnGet_OwnHiddenThread_CannotDelete()
    {
        ApplicationDbContext db = PortalDbContextFactory.CreateInMemory();
        ForumSection section = ForumTestHelpers.AddSection(db);
        ForumThread thread = ForumTestHelpers.AddThread(db, section.Id, "user-1", isHidden: true);
        ForumTestHelpers.AddPost(db, thread.Id, "user-1");

        DeleteThreadModel page = MakePage(db, "user-1");
        IActionResult result = await page.OnGetAsync(section.Slug, thread.Id, CancellationToken.None);

        Assert.IsType<ForbidResult>(result);
        Assert.False(page.CanDelete);
    }

    // --- RequiresModerationReason rules ---

    [Fact]
    public async Task OnGet_ModeratorDeletingOwnThread_DoesNotRequireReason()
    {
        ApplicationDbContext db = PortalDbContextFactory.CreateInMemory();
        ForumSection section = ForumTestHelpers.AddSection(db);
        ForumThread thread = ForumTestHelpers.AddThread(db, section.Id, "mod-1");
        ForumTestHelpers.AddPost(db, thread.Id, "mod-1");

        DeleteThreadModel page = MakePage(db, "mod-1", PortalRoles.Moderator);
        await page.OnGetAsync(section.Slug, thread.Id, CancellationToken.None);

        Assert.False(page.RequiresModerationReason);
    }

    [Fact]
    public async Task OnGet_ModeratorDeletingOtherThread_RequiresReason()
    {
        ApplicationDbContext db = PortalDbContextFactory.CreateInMemory();
        ForumSection section = ForumTestHelpers.AddSection(db);
        ForumThread thread = ForumTestHelpers.AddThread(db, section.Id, "user-99");
        ForumTestHelpers.AddPost(db, thread.Id, "user-99");

        DeleteThreadModel page = MakePage(db, "mod-1", PortalRoles.Moderator);
        await page.OnGetAsync(section.Slug, thread.Id, CancellationToken.None);

        Assert.True(page.RequiresModerationReason);
    }

    // --- OnPost deletion ---

    [Fact]
    public async Task OnPost_OwnThread_DeletesThread()
    {
        ApplicationDbContext db = PortalDbContextFactory.CreateInMemory();
        ForumSection section = ForumTestHelpers.AddSection(db);
        ForumThread thread = ForumTestHelpers.AddThread(db, section.Id, "user-1");
        ForumTestHelpers.AddPost(db, thread.Id, "user-1");

        DeleteThreadModel page = MakePage(db, "user-1");
        IActionResult result = await page.OnPostAsync(section.Slug, thread.Id, CancellationToken.None);

        Assert.IsType<RedirectToPageResult>(result);
        Assert.Null(await db.ForumThreads.FindAsync(thread.Id));
    }

    [Fact]
    public async Task OnPost_ModeratorNoReason_StaysOnPage()
    {
        ApplicationDbContext db = PortalDbContextFactory.CreateInMemory();
        ForumSection section = ForumTestHelpers.AddSection(db);
        ForumThread thread = ForumTestHelpers.AddThread(db, section.Id, "user-99");
        ForumTestHelpers.AddPost(db, thread.Id, "user-99");
        ForumTestHelpers.AddPost(db, thread.Id, "user-99");

        DeleteThreadModel page = MakePage(db, "mod-1", PortalRoles.Moderator);
        page.Input = new DeleteThreadModel.InputModel { Reason = "" };
        IActionResult result = await page.OnPostAsync(section.Slug, thread.Id, CancellationToken.None);

        Assert.IsType<PageResult>(result);
        Assert.NotNull(await db.ForumThreads.FindAsync(thread.Id)); // not deleted
    }

    [Fact]
    public async Task OnPost_ModeratorWithReason_DeletesThreadAndWritesAudit()
    {
        string dbName = Guid.NewGuid().ToString();
        ApplicationDbContext db = PortalDbContextFactory.CreateInMemory(dbName);
        (AdminAuditService audit, ApplicationDbContext auditDb) = ForumTestHelpers.MakeAuditService(dbName);
        ForumSection section = ForumTestHelpers.AddSection(db);
        ForumThread thread = ForumTestHelpers.AddThread(db, section.Id, "user-99", title: "Spam thread");
        ForumTestHelpers.AddPost(db, thread.Id, "user-99");

        ApplicationUser actor = new() { Id = "mod-1", Email = "mod@test.com" };
        var userManager = ForumTestHelpers.MakeUserManagerWithActor(actor);

        DeleteThreadModel page = new(db, userManager.Object, audit)
        {
            PageContext = ForumTestHelpers.MakePageContext(ForumTestHelpers.MakeUser("mod-1", PortalRoles.Moderator)),
            Input = new DeleteThreadModel.InputModel { Reason = "Spam content" },
        };

        IActionResult result = await page.OnPostAsync(section.Slug, thread.Id, CancellationToken.None);

        Assert.IsType<RedirectToPageResult>(result);
        Assert.Null(await db.ForumThreads.FindAsync(thread.Id));

        UltraSim.Portal.Models.AdminAuditRecord audit_record = Assert.Single(await auditDb.AdminAuditRecords.ToListAsync());
        Assert.Equal("forum.thread.delete", audit_record.ActionType);
        Assert.Equal("mod-1", audit_record.ActorUserId);
    }

    [Fact]
    public async Task OnPost_WrongSectionSlug_ReturnsNotFound()
    {
        ApplicationDbContext db = PortalDbContextFactory.CreateInMemory();
        ForumSection section = ForumTestHelpers.AddSection(db, slug: "general");
        ForumThread thread = ForumTestHelpers.AddThread(db, section.Id, "user-1");
        ForumTestHelpers.AddPost(db, thread.Id, "user-1");

        DeleteThreadModel page = MakePage(db, "user-1");
        IActionResult result = await page.OnPostAsync("wrong-slug", thread.Id, CancellationToken.None);

        Assert.IsType<NotFoundResult>(result);
    }

    // Helper
    private static DeleteThreadModel MakePage(ApplicationDbContext db, string userId, params string[] roles)
    {
        ApplicationUser actor = new() { Id = userId };
        var userManager = ForumTestHelpers.MakeUserManagerWithActor(actor);
        (AdminAuditService audit, _) = ForumTestHelpers.MakeAuditService();
        return new DeleteThreadModel(db, userManager.Object, audit)
        {
            PageContext = ForumTestHelpers.MakePageContext(ForumTestHelpers.MakeUser(userId, roles)),
            Input = new DeleteThreadModel.InputModel(),
        };
    }
}
