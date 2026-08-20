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

public sealed class DeletePostPageTests
{
    [Fact]
    public async Task OnGet_OpeningPost_ModeratorRedirectsToDeleteThread()
    {
        ApplicationDbContext db = PortalDbContextFactory.CreateInMemory();
        ForumSection section = ForumTestHelpers.AddSection(db);
        ForumThread thread = ForumTestHelpers.AddThread(db, section.Id, "user-1");
        ForumPost openingPost = ForumTestHelpers.AddPost(db, thread.Id, "user-1");

        DeletePostModel page = MakePage(db, "mod-1", PortalRoles.Moderator);
        IActionResult result = await page.OnGetAsync(section.Slug, thread.Id, openingPost.Id, CancellationToken.None);

        RedirectToPageResult redirect = Assert.IsType<RedirectToPageResult>(result);
        Assert.Equal("/Forums/DeleteThread", redirect.PageName);
    }

    [Fact]
    public async Task OnGet_OwnReplyPost_CanDelete()
    {
        ApplicationDbContext db = PortalDbContextFactory.CreateInMemory();
        ForumSection section = ForumTestHelpers.AddSection(db);
        ForumThread thread = ForumTestHelpers.AddThread(db, section.Id, "user-1");
        ForumTestHelpers.AddPost(db, thread.Id, "user-1", createdUtc: DateTime.UtcNow.AddMinutes(-1));
        ForumPost replyPost = ForumTestHelpers.AddPost(db, thread.Id, "user-1");

        DeletePostModel page = MakePage(db, "user-1");
        IActionResult result = await page.OnGetAsync(section.Slug, thread.Id, replyPost.Id, CancellationToken.None);

        Assert.IsType<PageResult>(result);
    }

    [Fact]
    public async Task OnGet_OtherUsersPost_NonModerator_IsForbidden()
    {
        ApplicationDbContext db = PortalDbContextFactory.CreateInMemory();
        ForumSection section = ForumTestHelpers.AddSection(db);
        ForumThread thread = ForumTestHelpers.AddThread(db, section.Id, "user-2");
        ForumTestHelpers.AddPost(db, thread.Id, "user-2", createdUtc: DateTime.UtcNow.AddMinutes(-1));
        ForumPost otherPost = ForumTestHelpers.AddPost(db, thread.Id, "user-2");

        DeletePostModel page = MakePage(db, "user-1");
        IActionResult result = await page.OnGetAsync(section.Slug, thread.Id, otherPost.Id, CancellationToken.None);

        Assert.IsType<ForbidResult>(result);
    }

    [Fact]
    public async Task OnGet_OwnHiddenPost_CannotDelete()
    {
        ApplicationDbContext db = PortalDbContextFactory.CreateInMemory();
        ForumSection section = ForumTestHelpers.AddSection(db);
        ForumThread thread = ForumTestHelpers.AddThread(db, section.Id, "user-1");
        ForumTestHelpers.AddPost(db, thread.Id, "user-1", createdUtc: DateTime.UtcNow.AddMinutes(-1));
        ForumPost hiddenPost = ForumTestHelpers.AddPost(db, thread.Id, "user-1", isHidden: true);

        DeletePostModel page = MakePage(db, "user-1");
        IActionResult result = await page.OnGetAsync(section.Slug, thread.Id, hiddenPost.Id, CancellationToken.None);

        Assert.IsType<ForbidResult>(result);
    }

    [Fact]
    public async Task OnGet_ModeratorCanDeleteAnyPost()
    {
        ApplicationDbContext db = PortalDbContextFactory.CreateInMemory();
        ForumSection section = ForumTestHelpers.AddSection(db);
        ForumThread thread = ForumTestHelpers.AddThread(db, section.Id, "user-2");
        ForumTestHelpers.AddPost(db, thread.Id, "user-2", createdUtc: DateTime.UtcNow.AddMinutes(-1));
        ForumPost otherPost = ForumTestHelpers.AddPost(db, thread.Id, "user-2");

        DeletePostModel page = MakePage(db, "mod-1", PortalRoles.Moderator);
        IActionResult result = await page.OnGetAsync(section.Slug, thread.Id, otherPost.Id, CancellationToken.None);

        Assert.IsType<PageResult>(result);
        Assert.True(page.RequiresModerationReason);
    }

    [Fact]
    public async Task OnPost_OwnPost_DeletesPost()
    {
        ApplicationDbContext db = PortalDbContextFactory.CreateInMemory();
        ForumSection section = ForumTestHelpers.AddSection(db);
        ForumThread thread = ForumTestHelpers.AddThread(db, section.Id, "user-1");
        ForumTestHelpers.AddPost(db, thread.Id, "user-1", createdUtc: DateTime.UtcNow.AddMinutes(-1));
        ForumPost reply = ForumTestHelpers.AddPost(db, thread.Id, "user-1");

        DeletePostModel page = MakePage(db, "user-1");
        IActionResult result = await page.OnPostAsync(section.Slug, thread.Id, reply.Id, CancellationToken.None);

        Assert.IsType<RedirectToPageResult>(result);
        Assert.Null(await db.ForumPosts.FindAsync(reply.Id));
    }

    [Fact]
    public async Task OnPost_DeletePost_UpdatesThreadLastPost()
    {
        ApplicationDbContext db = PortalDbContextFactory.CreateInMemory();
        ForumSection section = ForumTestHelpers.AddSection(db);
        ForumThread thread = ForumTestHelpers.AddThread(db, section.Id, "user-1");
        ForumPost firstPost = ForumTestHelpers.AddPost(db, thread.Id, "user-1", createdUtc: DateTime.UtcNow.AddMinutes(-2));
        ForumPost secondPost = ForumTestHelpers.AddPost(db, thread.Id, "user-2", createdUtc: DateTime.UtcNow.AddMinutes(-1));
        ForumPost thirdPost = ForumTestHelpers.AddPost(db, thread.Id, "user-1", createdUtc: DateTime.UtcNow);

        // Delete third post — last post should become user-2's post
        DeletePostModel page = MakePage(db, "user-1");
        await page.OnPostAsync(section.Slug, thread.Id, thirdPost.Id, CancellationToken.None);

        db.ChangeTracker.Clear();
        ForumThread? updatedThread = await db.ForumThreads.FindAsync(thread.Id);
        Assert.Equal("user-2", updatedThread!.LastPostUserId);
    }

    [Fact]
    public async Task OnPost_ModeratorNoReason_StaysOnPage()
    {
        ApplicationDbContext db = PortalDbContextFactory.CreateInMemory();
        ForumSection section = ForumTestHelpers.AddSection(db);
        ForumThread thread = ForumTestHelpers.AddThread(db, section.Id, "user-99");
        ForumTestHelpers.AddPost(db, thread.Id, "user-99", createdUtc: DateTime.UtcNow.AddMinutes(-1));
        ForumPost post = ForumTestHelpers.AddPost(db, thread.Id, "user-99");

        DeletePostModel page = MakePage(db, "mod-1", PortalRoles.Moderator);
        page.Input = new DeletePostModel.InputModel { Reason = "" };
        IActionResult result = await page.OnPostAsync(section.Slug, thread.Id, post.Id, CancellationToken.None);

        Assert.IsType<PageResult>(result);
        Assert.NotNull(await db.ForumPosts.FindAsync(post.Id));
    }

    [Fact]
    public async Task OnPost_ModeratorWithReason_DeletesAndWritesAudit()
    {
        string dbName = Guid.NewGuid().ToString();
        ApplicationDbContext db = PortalDbContextFactory.CreateInMemory(dbName);
        (AdminAuditService audit, ApplicationDbContext auditDb) = ForumTestHelpers.MakeAuditService(dbName);
        ForumSection section = ForumTestHelpers.AddSection(db);
        ForumThread thread = ForumTestHelpers.AddThread(db, section.Id, "user-99");
        ForumTestHelpers.AddPost(db, thread.Id, "user-99", createdUtc: DateTime.UtcNow.AddMinutes(-1));
        ForumPost post = ForumTestHelpers.AddPost(db, thread.Id, "user-99");

        ApplicationUser actor = new() { Id = "mod-1", Email = "mod@test.com" };
        var userManager = ForumTestHelpers.MakeUserManagerWithActor(actor);

        DeletePostModel page = new(db, userManager.Object, audit)
        {
            PageContext = ForumTestHelpers.MakePageContext(ForumTestHelpers.MakeUser("mod-1", PortalRoles.Moderator)),
            Input = new DeletePostModel.InputModel { Reason = "Abuse" },
        };

        IActionResult result = await page.OnPostAsync(section.Slug, thread.Id, post.Id, CancellationToken.None);

        Assert.IsType<RedirectToPageResult>(result);
        Assert.Null(await db.ForumPosts.FindAsync(post.Id));

        UltraSim.Portal.Models.AdminAuditRecord record = Assert.Single(await auditDb.AdminAuditRecords.ToListAsync());
        Assert.Equal("forum.post.delete", record.ActionType);
    }

    private static DeletePostModel MakePage(ApplicationDbContext db, string userId, params string[] roles)
    {
        ApplicationUser actor = new() { Id = userId };
        var userManager = ForumTestHelpers.MakeUserManagerWithActor(actor);
        (AdminAuditService audit, _) = ForumTestHelpers.MakeAuditService();
        return new DeletePostModel(db, userManager.Object, audit)
        {
            PageContext = ForumTestHelpers.MakePageContext(ForumTestHelpers.MakeUser(userId, roles)),
            Input = new DeletePostModel.InputModel(),
        };
    }
}
