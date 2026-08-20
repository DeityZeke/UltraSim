using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using Moq;
using System.Net;
using System.Net.Http.Json;
using UltraSim.Discovery.Official;
using UltraSim.Portal.Data;
using UltraSim.Portal.Models;
using UltraSim.Portal.Pages.Account.Servers;
using UltraSim.Portal.Services.RegistryApi;
using UltraSim.Portal.Tests.TestHelpers;
using Xunit;

namespace UltraSim.Portal.Tests.Pages.Account.Servers;

public sealed class AddPageTests
{
    private static (AddModel page, ApplicationDbContext db) CreatePage(
        RegistryQueryClient registryClient,
        ApplicationUser user)
    {
        ApplicationDbContext db = PortalDbContextFactory.CreateInMemory();
        Mock<Microsoft.AspNetCore.Identity.UserManager<ApplicationUser>> userManagerMock =
            MockUserManager(user);

        AddModel page = new(db, userManagerMock.Object, registryClient)
        {
            PageContext = CreatePageContext(),
        };

        return (page, db);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task OnPost_BlankToken_AddsModelError(string ownershipToken)
    {
        RegistryQueryClient registry = CreateFakeRegistry(_ =>
            throw new InvalidOperationException("should not be called"));

        ApplicationUser user = new() { Id = "user-1", UserName = "test", Email = "test@example.com" };
        (AddModel page, _) = CreatePage(registry, user);
        page.Input = new()
        {
            DisplayName = "My Server",
            TargetHost = "1.2.3.4",
            TargetPort = 7777,
            OwnershipToken = ownershipToken
        };

        IActionResult result = await page.OnPostAsync(CancellationToken.None);

        Assert.IsType<PageResult>(result);
        Assert.False(page.ModelState.IsValid);
    }

    [Fact]
    public async Task OnPost_RegistrySuccess_SavesClaimWithPendingVerification()
    {
        RegistryQueryClient registry = CreateFakeRegistry(_ =>
            new OfficialRegistryOwnerClaimResponse(
                OfficialRegistryOwnerClaimState.PendingVerification,
                "srv-abc", "reg-1", "Pending"));

        ApplicationUser user = new() { Id = "user-1", UserName = "test", Email = "test@example.com" };
        (AddModel page, ApplicationDbContext db) = CreatePage(registry, user);
        page.Input = new()
        {
            DisplayName = "My Server",
            TargetHost = "1.2.3.4",
            TargetPort = 7777,
            OwnershipToken = "mytoken"
        };

        IActionResult result = await page.OnPostAsync(CancellationToken.None);

        Assert.IsType<RedirectToPageResult>(result);
        ServerClaim claim = Assert.Single(await db.ServerClaims.ToListAsync());
        Assert.Equal(ServerClaimStatus.PendingVerification, claim.Status);
        Assert.Equal("user-1", claim.OwnerUserId);
        Assert.Equal("My Server", claim.DisplayName);
        Assert.Null(claim.OwnershipToken); // token must NOT be persisted
        Assert.Equal("srv-abc", claim.RequestedServerId);
    }

    [Fact]
    public async Task OnPost_RegistryReturnsNoServerDetected_SavesClaimWithFailed()
    {
        RegistryQueryClient registry = CreateFakeRegistry(_ =>
            new OfficialRegistryOwnerClaimResponse(
                OfficialRegistryOwnerClaimState.NoServerDetected,
                null, null, "No server found"));

        ApplicationUser user = new() { Id = "user-1", UserName = "test", Email = "test@example.com" };
        (AddModel page, ApplicationDbContext db) = CreatePage(registry, user);
        page.Input = new()
        {
            DisplayName = "My Server",
            TargetHost = "1.2.3.4",
            TargetPort = 7777,
            OwnershipToken = "mytoken"
        };

        IActionResult result = await page.OnPostAsync(CancellationToken.None);

        Assert.IsType<RedirectToPageResult>(result);
        ServerClaim claim = Assert.Single(await db.ServerClaims.ToListAsync());
        Assert.Equal(ServerClaimStatus.Failed, claim.Status);
        Assert.Null(claim.OwnershipToken);
    }

    [Fact]
    public async Task OnPost_RegistryHttpException_SavesClaimWithFailed()
    {
        RegistryQueryClient registry = CreateFakeRegistryThrowing(
            new HttpRequestException("Registry down", null, HttpStatusCode.ServiceUnavailable));

        ApplicationUser user = new() { Id = "user-1", UserName = "test", Email = "test@example.com" };
        (AddModel page, ApplicationDbContext db) = CreatePage(registry, user);
        page.Input = new()
        {
            DisplayName = "My Server",
            TargetHost = "1.2.3.4",
            TargetPort = 7777,
            OwnershipToken = "mytoken"
        };

        IActionResult result = await page.OnPostAsync(CancellationToken.None);

        // Should redirect (not stay on page) - claim is saved as Failed
        Assert.IsType<RedirectToPageResult>(result);
        ServerClaim claim = Assert.Single(await db.ServerClaims.ToListAsync());
        Assert.Equal(ServerClaimStatus.Failed, claim.Status);
        Assert.Null(claim.OwnershipToken);
        Assert.NotNull(claim.LastError);
        Assert.Contains("503", claim.LastError);
    }

    [Fact]
    public async Task OnPost_RegistryGenericException_SavesClaimWithFailed()
    {
        RegistryQueryClient registry = CreateFakeRegistryThrowing(
            new InvalidOperationException("unexpected"));

        ApplicationUser user = new() { Id = "user-1", UserName = "test", Email = "test@example.com" };
        (AddModel page, ApplicationDbContext db) = CreatePage(registry, user);
        page.Input = new()
        {
            DisplayName = "My Server",
            TargetHost = "1.2.3.4",
            TargetPort = 7777,
            OwnershipToken = "mytoken"
        };

        IActionResult result = await page.OnPostAsync(CancellationToken.None);

        Assert.IsType<RedirectToPageResult>(result);
        ServerClaim claim = Assert.Single(await db.ServerClaims.ToListAsync());
        Assert.Equal(ServerClaimStatus.Failed, claim.Status);
        Assert.Null(claim.OwnershipToken);
    }

    [Fact]
    public async Task OnPost_TokenNotPersistedInDb()
    {
        RegistryQueryClient registry = CreateFakeRegistry(_ =>
            new OfficialRegistryOwnerClaimResponse(
                OfficialRegistryOwnerClaimState.PendingVerification,
                "srv-1", "reg-1", "Pending"));

        ApplicationUser user = new() { Id = "user-1", UserName = "test", Email = "test@example.com" };
        (AddModel page, ApplicationDbContext db) = CreatePage(registry, user);
        page.Input = new()
        {
            DisplayName = "Server",
            TargetHost = "5.6.7.8",
            TargetPort = 7777,
            OwnershipToken = "supersecrettoken"
        };

        await page.OnPostAsync(CancellationToken.None);

        ServerClaim claim = Assert.Single(await db.ServerClaims.ToListAsync());
        Assert.Null(claim.OwnershipToken);
    }

    [Fact]
    public async Task OnPost_PortAndHostStoredCorrectly()
    {
        RegistryQueryClient registry = CreateFakeRegistry(_ =>
            new OfficialRegistryOwnerClaimResponse(
                OfficialRegistryOwnerClaimState.PendingVerification,
                "srv-2", "reg-2", "Pending"));

        ApplicationUser user = new() { Id = "user-1", UserName = "test", Email = "test@example.com" };
        (AddModel page, ApplicationDbContext db) = CreatePage(registry, user);
        page.Input = new()
        {
            DisplayName = "My Server",
            TargetHost = "10.0.0.1",
            TargetPort = 25565,
            OwnershipToken = "tok"
        };

        await page.OnPostAsync(CancellationToken.None);

        ServerClaim claim = Assert.Single(await db.ServerClaims.ToListAsync());
        Assert.Equal("10.0.0.1", claim.TargetHost);
        Assert.Equal(25565, claim.TargetPort);
    }

    // Helpers

    private static RegistryQueryClient CreateFakeRegistry(
        Func<OfficialRegistryOwnerClaimRequest, OfficialRegistryOwnerClaimResponse> claimHandler)
    {
        FakeRegistryHandler handler = new(claimHandler);
        HttpClient http = new(handler);
        Microsoft.Extensions.Options.IOptions<Portal.Options.RegistryOptions> opts =
            Microsoft.Extensions.Options.Options.Create(new Portal.Options.RegistryOptions
            {
                BaseUrl = "http://localhost:5180",
            });
        return new RegistryQueryClient(http, opts);
    }

    private static RegistryQueryClient CreateFakeRegistryThrowing(Exception ex)
    {
        FakeRegistryHandlerThrowing handler = new(ex);
        HttpClient http = new(handler);
        Microsoft.Extensions.Options.IOptions<Portal.Options.RegistryOptions> opts =
            Microsoft.Extensions.Options.Options.Create(new Portal.Options.RegistryOptions
            {
                BaseUrl = "http://localhost:5180",
            });
        return new RegistryQueryClient(http, opts);
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

    private static PageContext CreatePageContext()
    {
        DefaultHttpContext httpContext = new();
        ActionContext actionContext = new(
            httpContext,
            new Microsoft.AspNetCore.Routing.RouteData(),
            new PageActionDescriptor());
        return new PageContext(actionContext);
    }

    private sealed class FakeRegistryHandler : HttpMessageHandler
    {
        private readonly Func<OfficialRegistryOwnerClaimRequest, OfficialRegistryOwnerClaimResponse> _handler;
        public FakeRegistryHandler(Func<OfficialRegistryOwnerClaimRequest, OfficialRegistryOwnerClaimResponse> handler)
            => _handler = handler;

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            OfficialRegistryOwnerClaimRequest? req = null;
            if (request.Content != null)
                req = await request.Content.ReadFromJsonAsync<OfficialRegistryOwnerClaimRequest>(ct);
            OfficialRegistryOwnerClaimResponse response = _handler(req!);
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = JsonContent.Create(response),
            };
        }
    }

    private sealed class FakeRegistryHandlerThrowing : HttpMessageHandler
    {
        private readonly Exception _ex;
        public FakeRegistryHandlerThrowing(Exception ex) => _ex = ex;
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
            => throw _ex;
    }
}
