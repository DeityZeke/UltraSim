using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.Mvc.ViewFeatures;
using Moq;
using UltraSim.Portal.Authorization;
using UltraSim.Portal.Data;
using UltraSim.Portal.Models;
using UltraSim.Portal.Services;

namespace UltraSim.Portal.Tests.TestHelpers;

internal static class ForumTestHelpers
{
    public static ForumSection AddSection(ApplicationDbContext db, string slug = "general", bool isReadOnly = false)
    {
        ForumSection section = new()
        {
            Slug = slug,
            Title = "General",
            Description = "General discussion",
            DisplayOrder = 0,
            IsReadOnly = isReadOnly,
            CreatedUtc = DateTime.UtcNow,
        };
        db.ForumSections.Add(section);
        db.SaveChanges();
        return section;
    }

    public static ForumThread AddThread(ApplicationDbContext db, int sectionId, string authorUserId,
        string title = "Test Thread", bool isHidden = false, bool isLocked = false, bool isPinned = false)
    {
        ForumThread thread = new()
        {
            SectionId = sectionId,
            AuthorUserId = authorUserId,
            Title = title,
            IsHidden = isHidden,
            IsLocked = isLocked,
            IsPinned = isPinned,
            CreatedUtc = DateTime.UtcNow,
            UpdatedUtc = DateTime.UtcNow,
            LastPostedUtc = DateTime.UtcNow,
        };
        db.ForumThreads.Add(thread);
        db.SaveChanges();
        return thread;
    }

    public static ForumPost AddPost(ApplicationDbContext db, long threadId, string authorUserId,
        string content = "Post content", bool isHidden = false, DateTime? createdUtc = null)
    {
        ForumPost post = new()
        {
            ThreadId = threadId,
            AuthorUserId = authorUserId,
            Content = content,
            IsHidden = isHidden,
            CreatedUtc = createdUtc ?? DateTime.UtcNow,
            UpdatedUtc = createdUtc ?? DateTime.UtcNow,
        };
        db.ForumPosts.Add(post);
        db.SaveChanges();
        return post;
    }

    public static ClaimsPrincipal MakeUser(string userId, params string[] roles)
    {
        List<Claim> claims = [new Claim(ClaimTypes.NameIdentifier, userId)];
        foreach (string role in roles)
            claims.Add(new Claim(ClaimTypes.Role, role));
        return new ClaimsPrincipal(new ClaimsIdentity(claims, "test"));
    }

    public static ClaimsPrincipal MakeAnonymous() =>
        new(new ClaimsIdentity());

    public static PageContext MakePageContext(ClaimsPrincipal user)
    {
        DefaultHttpContext httpContext = new() { User = user };
        ActionContext actionContext = new(
            httpContext,
            new Microsoft.AspNetCore.Routing.RouteData(),
            new PageActionDescriptor());
        return new PageContext(actionContext);
    }

    public static ITempDataDictionary MakeTempData()
    {
        Mock<ITempDataProvider> provider = new();
        provider.Setup(p => p.LoadTempData(It.IsAny<HttpContext>())).Returns(new Dictionary<string, object?>());
        provider.Setup(p => p.SaveTempData(It.IsAny<HttpContext>(), It.IsAny<IDictionary<string, object?>>()));
        DefaultHttpContext httpContext = new();
        return new TempDataDictionary(httpContext, provider.Object);
    }

    public static (AdminAuditService auditService, ApplicationDbContext auditDb) MakeAuditService(string? dbName = null)
    {
        ApplicationDbContext auditDb = PortalDbContextFactory.CreateInMemory(dbName ?? Guid.NewGuid().ToString());
        return (new AdminAuditService(auditDb), auditDb);
    }

    public static Mock<UserManager<ApplicationUser>> MakeUserManagerWithActor(ApplicationUser actor)
    {
        Mock<IUserStore<ApplicationUser>> store = new();
        Mock<UserManager<ApplicationUser>> mock = new(store.Object, null!, null!, null!, null!, null!, null!, null!, null!);
        mock.Setup(m => m.FindByIdAsync(actor.Id)).ReturnsAsync(actor);
        mock.Setup(m => m.GetRolesAsync(actor)).ReturnsAsync([]);
        return mock;
    }
}
