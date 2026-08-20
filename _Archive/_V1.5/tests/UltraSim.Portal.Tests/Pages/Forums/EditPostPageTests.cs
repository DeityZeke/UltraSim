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

public sealed class EditPostPageTests
{
    [Fact]
    public async Task OnPost_OwnPost_UpdatesContent()
    {
        ApplicationDbContext db = PortalDbContextFactory.CreateInMemory();
        ForumSection section = ForumTestHelpers.AddSection(db);
        ForumThread thread = ForumTestHelpers.AddThread(db, section.Id, "user-1");
        ForumTestHelpers.AddPost(db, thread.Id, "user-1", createdUtc: DateTime.UtcNow.AddMinutes(-1)); // opening post
        ForumPost replyPost = ForumTestHelpers.AddPost(db, thread.Id, "user-1", content: "original");

        EditPostModel page = MakePage(db, "user-1");
        page.Input = new EditPostModel.InputModel { Content = "updated content" };
        IActionResult result = await page.OnPostAsync(section.Slug, thread.Id, replyPost.Id, CancellationToken.None);

        Assert.IsType<RedirectToPageResult>(result);
        db.ChangeTracker.Clear();
        ForumPost? updated = await db.ForumPosts.FindAsync(replyPost.Id);
        Assert.Equal("updated content", updated!.Content);
    }

    [Fact]
    public async Task OnPost_OtherUsersPost_NonModerator_IsForbidden()
    {
        ApplicationDbContext db = PortalDbContextFactory.CreateInMemory();
        ForumSection section = ForumTestHelpers.AddSection(db);
        ForumThread thread = ForumTestHelpers.AddThread(db, section.Id, "user-2");
        ForumPost post = ForumTestHelpers.AddPost(db, thread.Id, "user-2");

        EditPostModel page = MakePage(db, "user-1");
        page.Input = new EditPostModel.InputModel { Content = "hack" };
        IActionResult result = await page.OnPostAsync(section.Slug, thread.Id, post.Id, CancellationToken.None);

        Assert.IsType<ForbidResult>(result);
    }

    [Fact]
    public async Task OnPost_OwnHiddenPost_IsForbidden()
    {
        ApplicationDbContext db = PortalDbContextFactory.CreateInMemory();
        ForumSection section = ForumTestHelpers.AddSection(db);
        ForumThread thread = ForumTestHelpers.AddThread(db, section.Id, "user-1");
        ForumPost post = ForumTestHelpers.AddPost(db, thread.Id, "user-1", isHidden: true);

        EditPostModel page = MakePage(db, "user-1");
        page.Input = new EditPostModel.InputModel { Content = "new content" };
        IActionResult result = await page.OnPostAsync(section.Slug, thread.Id, post.Id, CancellationToken.None);

        Assert.IsType<ForbidResult>(result);
    }

    [Fact]
    public async Task OnPost_OpeningPost_UpdatesThreadTitle()
    {
        ApplicationDbContext db = PortalDbContextFactory.CreateInMemory();
        ForumSection section = ForumTestHelpers.AddSection(db);
        ForumThread thread = ForumTestHelpers.AddThread(db, section.Id, "user-1", title: "Old Title");
        ForumPost openingPost = ForumTestHelpers.AddPost(db, thread.Id, "user-1");

        EditPostModel page = MakePage(db, "user-1");
        page.Input = new EditPostModel.InputModel { Title = "New Title", Content = "updated" };
        IActionResult result = await page.OnPostAsync(section.Slug, thread.Id, openingPost.Id, CancellationToken.None);

        Assert.IsType<RedirectToPageResult>(result);
        db.ChangeTracker.Clear();
        ForumThread? updatedThread = await db.ForumThreads.FindAsync(thread.Id);
        Assert.Equal("New Title", updatedThread!.Title);
    }

    [Fact]
    public async Task OnPost_ModeratorEditOwnPost_NoReasonRequired()
    {
        ApplicationDbContext db = PortalDbContextFactory.CreateInMemory();
        ForumSection section = ForumTestHelpers.AddSection(db);
        ForumThread thread = ForumTestHelpers.AddThread(db, section.Id, "mod-1");
        ForumTestHelpers.AddPost(db, thread.Id, "mod-1", createdUtc: DateTime.UtcNow.AddMinutes(-1)); // opening post
        ForumPost replyPost = ForumTestHelpers.AddPost(db, thread.Id, "mod-1");

        EditPostModel page = MakePage(db, "mod-1", PortalRoles.Moderator);
        page.Input = new EditPostModel.InputModel { Content = "updated" };
        IActionResult result = await page.OnPostAsync(section.Slug, thread.Id, replyPost.Id, CancellationToken.None);

        Assert.IsType<RedirectToPageResult>(result);
        Assert.False(page.RequiresModerationReason);
    }

    [Fact]
    public async Task OnPost_ModeratorEditOtherPost_RequiresReason()
    {
        ApplicationDbContext db = PortalDbContextFactory.CreateInMemory();
        ForumSection section = ForumTestHelpers.AddSection(db);
        ForumThread thread = ForumTestHelpers.AddThread(db, section.Id, "user-99");
        ForumPost post = ForumTestHelpers.AddPost(db, thread.Id, "user-99");

        EditPostModel page = MakePage(db, "mod-1", PortalRoles.Moderator);
        page.Input = new EditPostModel.InputModel { Content = "cleaned up" }; // no reason
        IActionResult result = await page.OnPostAsync(section.Slug, thread.Id, post.Id, CancellationToken.None);

        Assert.IsType<PageResult>(result); // stays due to missing reason
        Assert.True(page.RequiresModerationReason);
    }

    [Fact]
    public async Task OnPost_ModeratorEditOtherPostWithReason_SucceedsAndWritesAudit()
    {
        string dbName = Guid.NewGuid().ToString();
        ApplicationDbContext db = PortalDbContextFactory.CreateInMemory(dbName);
        (AdminAuditService audit, ApplicationDbContext auditDb) = ForumTestHelpers.MakeAuditService(dbName);
        ForumSection section = ForumTestHelpers.AddSection(db);
        ForumThread thread = ForumTestHelpers.AddThread(db, section.Id, "user-99");
        ForumTestHelpers.AddPost(db, thread.Id, "user-99", createdUtc: DateTime.UtcNow.AddMinutes(-1)); // opening post
        ForumPost post = ForumTestHelpers.AddPost(db, thread.Id, "user-99", content: "bad content");

        ApplicationUser actor = new() { Id = "mod-1", Email = "mod@test.com" };
        var userManager = ForumTestHelpers.MakeUserManagerWithActor(actor);

        EditPostModel page = new(db, userManager.Object, audit)
        {
            PageContext = ForumTestHelpers.MakePageContext(ForumTestHelpers.MakeUser("mod-1", PortalRoles.Moderator)),
            Input = new EditPostModel.InputModel { Content = "cleaned up", ModerationReason = "rule violation" },
        };

        IActionResult result = await page.OnPostAsync(section.Slug, thread.Id, post.Id, CancellationToken.None);

        Assert.IsType<RedirectToPageResult>(result);
        db.ChangeTracker.Clear();
        ForumPost? updated = await db.ForumPosts.FindAsync(post.Id);
        Assert.Equal("cleaned up", updated!.Content);

        UltraSim.Portal.Models.AdminAuditRecord record = Assert.Single(await auditDb.AdminAuditRecords.ToListAsync());
        Assert.Equal("forum.post.edit", record.ActionType);
        Assert.Equal("mod-1", record.ActorUserId);
    }

    private static EditPostModel MakePage(ApplicationDbContext db, string userId, params string[] roles)
    {
        ApplicationUser actor = new() { Id = userId };
        var userManager = ForumTestHelpers.MakeUserManagerWithActor(actor);
        (AdminAuditService audit, _) = ForumTestHelpers.MakeAuditService();
        return new EditPostModel(db, userManager.Object, audit)
        {
            PageContext = ForumTestHelpers.MakePageContext(ForumTestHelpers.MakeUser(userId, roles)),
            Input = new EditPostModel.InputModel(),
        };
    }
}
