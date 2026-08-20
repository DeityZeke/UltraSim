using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Moq;
using System.Net;
using UltraSim.Portal.Data;
using UltraSim.Portal.Models;
using UltraSim.Portal.Pages.Account.Servers;
using UltraSim.Portal.Services.RegistryApi;
using UltraSim.Portal.Tests.TestHelpers;
using Xunit;

namespace UltraSim.Portal.Tests.Pages.Account.Servers;

public sealed class DeletePageTests
{
    [Fact]
    public async Task OnPost_ConfirmationTextWrong_StaysOnPage()
    {
        ApplicationDbContext db = PortalDbContextFactory.CreateInMemory();
        ApplicationUser user = new() { Id = "user-1" };
        ServerClaim claim = AddClaim(db, user.Id, "My Server");

        DeleteModel page = CreatePage(db, user, CreateNoOpRegistry());
        page.Id = claim.Id;
        page.ConfirmationText = "delete"; // wrong case

        IActionResult result = await page.OnPostAsync(CancellationToken.None);

        Assert.IsType<PageResult>(result);
        Assert.NotNull(await db.ServerClaims.FindAsync(claim.Id));
    }

    [Fact]
    public async Task OnPost_CorrectConfirmation_NoClaim_DeletesLocalRecord()
    {
        ApplicationDbContext db = PortalDbContextFactory.CreateInMemory();
        ApplicationUser user = new() { Id = "user-1" };
        // No RequestedServerId - draft claim with no registry entry
        ServerClaim claim = AddClaim(db, user.Id, "Draft Server", requestedServerId: null);

        DeleteModel page = CreatePage(db, user, CreateNoOpRegistry());
        page.Id = claim.Id;
        page.ConfirmationText = "DELETE";

        IActionResult result = await page.OnPostAsync(CancellationToken.None);

        Assert.IsType<RedirectToPageResult>(result);
        Assert.Null(await db.ServerClaims.FindAsync(claim.Id));
    }

    [Fact]
    public async Task OnPost_WithRegistryClaim_CallsRegistryAndDeletesLocal()
    {
        bool registryCalled = false;
        HttpRequestMessage? capturedRequest = null;
        ApplicationDbContext db = PortalDbContextFactory.CreateInMemory();
        ApplicationUser user = new() { Id = "user-1" };
        ServerClaim claim = AddClaim(db, user.Id, "Server", requestedServerId: "srv-abc");

        RegistryQueryClient registry = CreateRegistry(request =>
        {
            registryCalled = true;
            capturedRequest = request;
        });
        DeleteModel page = CreatePage(db, user, registry);
        page.Id = claim.Id;
        page.ConfirmationText = "DELETE";

        IActionResult result = await page.OnPostAsync(CancellationToken.None);

        Assert.IsType<RedirectToPageResult>(result);
        Assert.True(registryCalled);
        Assert.NotNull(capturedRequest);
        Assert.Equal(HttpMethod.Delete, capturedRequest!.Method);
        Assert.Equal("/api/v1/servers/srv-abc/owner", capturedRequest.RequestUri?.AbsolutePath);
        Assert.Null(await db.ServerClaims.FindAsync(claim.Id));
    }

    [Fact]
    public async Task OnPost_RegistryFails_StaysOnPageAndKeepsLocalRecord()
    {
        ApplicationDbContext db = PortalDbContextFactory.CreateInMemory();
        ApplicationUser user = new() { Id = "user-1" };
        ServerClaim claim = AddClaim(db, user.Id, "Server", requestedServerId: "srv-abc");

        RegistryQueryClient registry = CreateRegistryThrowing(
            new HttpRequestException("fail", null, HttpStatusCode.InternalServerError));
        DeleteModel page = CreatePage(db, user, registry);
        page.Id = claim.Id;
        page.ConfirmationText = "DELETE";

        IActionResult result = await page.OnPostAsync(CancellationToken.None);

        Assert.IsType<PageResult>(result);
        Assert.NotNull(await db.ServerClaims.FindAsync(claim.Id));
    }

    [Fact]
    public async Task OnPost_WrongUser_ReturnsNotFound()
    {
        ApplicationDbContext db = PortalDbContextFactory.CreateInMemory();
        ApplicationUser user = new() { Id = "user-1" };
        ServerClaim claim = AddClaim(db, "user-2", "Server");

        DeleteModel page = CreatePage(db, user, CreateNoOpRegistry());
        page.Id = claim.Id;
        page.ConfirmationText = "DELETE";

        IActionResult result = await page.OnPostAsync(CancellationToken.None);

        Assert.IsType<NotFoundResult>(result);
    }

    [Fact]
    public async Task OnPost_RegistryGenericException_StaysOnPage()
    {
        ApplicationDbContext db = PortalDbContextFactory.CreateInMemory();
        ApplicationUser user = new() { Id = "user-1" };
        ServerClaim claim = AddClaim(db, user.Id, "Server", requestedServerId: "srv-abc");

        RegistryQueryClient registry = CreateRegistryThrowing(new InvalidOperationException("unexpected"));
        DeleteModel page = CreatePage(db, user, registry);
        page.Id = claim.Id;
        page.ConfirmationText = "DELETE";

        IActionResult result = await page.OnPostAsync(CancellationToken.None);

        Assert.IsType<PageResult>(result);
        Assert.NotNull(await db.ServerClaims.FindAsync(claim.Id));
    }

    private static ServerClaim AddClaim(
        ApplicationDbContext db,
        string userId,
        string name,
        string? requestedServerId = "srv-123")
    {
        ServerClaim claim = new()
        {
            OwnerUserId = userId,
            DisplayName = name,
            TargetHost = "1.2.3.4",
            TargetPort = 7777,
            RequestedServerId = requestedServerId,
            Status = ServerClaimStatus.PendingVerification,
            SubmittedUtc = DateTimeOffset.UtcNow,
            UpdatedUtc = DateTimeOffset.UtcNow,
        };
        db.ServerClaims.Add(claim);
        db.SaveChanges();
        return claim;
    }

    private static DeleteModel CreatePage(
        ApplicationDbContext db,
        ApplicationUser user,
        RegistryQueryClient registry)
    {
        Mock<Microsoft.AspNetCore.Identity.UserManager<ApplicationUser>> mock =
            MockUserManager(user);

        DefaultHttpContext httpContext = new();
        ActionContext actionContext = new(
            httpContext,
            new Microsoft.AspNetCore.Routing.RouteData(),
            new PageActionDescriptor());
        PageContext pageContext = new(actionContext);

        return new DeleteModel(db, mock.Object, registry)
        {
            PageContext = pageContext,
        };
    }

    private static Mock<Microsoft.AspNetCore.Identity.UserManager<ApplicationUser>> MockUserManager(ApplicationUser user)
    {
        Mock<Microsoft.AspNetCore.Identity.IUserStore<ApplicationUser>> store = new();
        Mock<Microsoft.AspNetCore.Identity.UserManager<ApplicationUser>> mock = new(
            store.Object, null!, null!, null!, null!, null!, null!, null!, null!);
        mock.Setup(m => m.GetUserAsync(It.IsAny<System.Security.Claims.ClaimsPrincipal>()))
            .ReturnsAsync(user);
        return mock;
    }

    private static RegistryQueryClient CreateNoOpRegistry()
        => CreateRegistry(_ => { });

    private static RegistryQueryClient CreateRegistry(Action<HttpRequestMessage> onRequest)
    {
        FakeDeleteHandler handler = new(onRequest);
        HttpClient http = new(handler);
        Microsoft.Extensions.Options.IOptions<Portal.Options.RegistryOptions> opts =
            Microsoft.Extensions.Options.Options.Create(
                new Portal.Options.RegistryOptions { BaseUrl = "http://localhost:5180" });
        return new RegistryQueryClient(http, opts);
    }

    private static RegistryQueryClient CreateRegistryThrowing(Exception ex)
    {
        FakeThrowingHandler handler = new(ex);
        HttpClient http = new(handler);
        Microsoft.Extensions.Options.IOptions<Portal.Options.RegistryOptions> opts =
            Microsoft.Extensions.Options.Options.Create(
                new Portal.Options.RegistryOptions { BaseUrl = "http://localhost:5180" });
        return new RegistryQueryClient(http, opts);
    }

    private sealed class FakeDeleteHandler : HttpMessageHandler
    {
        private readonly Action<HttpRequestMessage> _onRequest;
        public FakeDeleteHandler(Action<HttpRequestMessage> onRequest) => _onRequest = onRequest;
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            _onRequest(request);
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NoContent));
        }
    }

    private sealed class FakeThrowingHandler : HttpMessageHandler
    {
        private readonly Exception _ex;
        public FakeThrowingHandler(Exception ex) => _ex = ex;
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
            => throw _ex;
    }
}
