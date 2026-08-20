using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using Moq;
using System.Net;
using System.Net.Http.Json;
using UltraSim.Discovery;
using UltraSim.Portal.Data;
using UltraSim.Portal.Models;
using UltraSim.Portal.Pages.Account.Servers;
using UltraSim.Portal.Services.RegistryApi;
using UltraSim.Portal.Tests.TestHelpers;
using Xunit;

namespace UltraSim.Portal.Tests.Pages.Account.Servers;

public sealed class IndexPageTests
{
    [Fact]
    public async Task OnGet_VerifiedAtUtc_SetsStatusToVerified()
    {
        ApplicationDbContext db = PortalDbContextFactory.CreateInMemory();
        ApplicationUser user = new() { Id = "user-1" };
        AddClaim(db, user.Id, "srv-abc", ServerClaimStatus.PendingVerification);

        ServerDiscoveryDetail detail = CreateDetail("srv-abc", verifiedAtUtc: DateTimeOffset.UtcNow);
        IndexModel page = CreatePage(db, user, detail);

        await page.OnGetAsync(CancellationToken.None);

        ServerClaim updated = page.Claims.First(c => c.RequestedServerId == "srv-abc");
        Assert.Equal(ServerClaimStatus.Verified, updated.Status);
        Assert.Null(updated.LastError);
    }

    [Fact]
    public async Task OnGet_OwnershipErrorSet_SetsStatusToFailed()
    {
        ApplicationDbContext db = PortalDbContextFactory.CreateInMemory();
        ApplicationUser user = new() { Id = "user-1" };
        AddClaim(db, user.Id, "srv-abc", ServerClaimStatus.PendingVerification);

        ServerDiscoveryDetail detail = CreateDetail("srv-abc",
            verifiedAtUtc: null,
            ownershipClaimRequestedUtc: DateTimeOffset.UtcNow,
            ownershipError: "Token mismatch.");
        IndexModel page = CreatePage(db, user, detail);

        await page.OnGetAsync(CancellationToken.None);

        ServerClaim updated = page.Claims.First(c => c.RequestedServerId == "srv-abc");
        Assert.Equal(ServerClaimStatus.Failed, updated.Status);
        Assert.Equal("Token mismatch.", updated.LastError);
    }

    [Fact]
    public async Task OnGet_ClaimRequestedNoError_SetsStatusToPendingVerification()
    {
        ApplicationDbContext db = PortalDbContextFactory.CreateInMemory();
        ApplicationUser user = new() { Id = "user-1" };
        AddClaim(db, user.Id, "srv-abc", ServerClaimStatus.Failed);

        ServerDiscoveryDetail detail = CreateDetail("srv-abc",
            verifiedAtUtc: null,
            ownershipClaimRequestedUtc: DateTimeOffset.UtcNow,
            ownershipError: null);
        IndexModel page = CreatePage(db, user, detail);

        await page.OnGetAsync(CancellationToken.None);

        ServerClaim updated = page.Claims.First(c => c.RequestedServerId == "srv-abc");
        Assert.Equal(ServerClaimStatus.PendingVerification, updated.Status);
    }

    [Fact]
    public async Task OnGet_RegistryReturnsNull_ClaimsPopulatedFromDb()
    {
        ApplicationDbContext db = PortalDbContextFactory.CreateInMemory();
        ApplicationUser user = new() { Id = "user-1" };
        AddClaim(db, user.Id, "srv-abc", ServerClaimStatus.Verified);

        IndexModel page = CreatePage(db, user, registryDetail: null);

        await page.OnGetAsync(CancellationToken.None);

        Assert.Single(page.Claims);
    }

    [Fact]
    public async Task OnGet_DoesNotWriteToDatabase()
    {
        ApplicationDbContext db = PortalDbContextFactory.CreateInMemory();
        ApplicationUser user = new() { Id = "user-1" };
        ServerClaim claim = AddClaim(db, user.Id, "srv-abc", ServerClaimStatus.PendingVerification);

        ServerDiscoveryDetail detail = CreateDetail("srv-abc", verifiedAtUtc: DateTimeOffset.UtcNow);
        IndexModel page = CreatePage(db, user, detail);

        await page.OnGetAsync(CancellationToken.None);

        ServerClaim inMemory = page.Claims.First(c => c.RequestedServerId == "srv-abc");
        Assert.Equal(ServerClaimStatus.Verified, inMemory.Status);

        // But reload from DB - should still be PendingVerification because OnGet does not SaveChanges
        db.ChangeTracker.Clear();
        ServerClaim? fromDb = await db.ServerClaims.FindAsync(claim.Id);
        Assert.NotNull(fromDb);
        Assert.Equal(ServerClaimStatus.PendingVerification, fromDb!.Status);
    }

    [Fact]
    public async Task OnGet_MultipleClaims_UpdateIndependently()
    {
        ApplicationDbContext db = PortalDbContextFactory.CreateInMemory();
        ApplicationUser user = new() { Id = "user-1" };
        AddClaim(db, user.Id, "srv-verified", ServerClaimStatus.PendingVerification);
        AddClaim(db, user.Id, "srv-failed", ServerClaimStatus.PendingVerification);
        AddClaim(db, user.Id, "srv-pending", ServerClaimStatus.Failed);

        Dictionary<string, ServerDiscoveryDetail> details = new(StringComparer.Ordinal)
        {
            ["srv-verified"] = CreateDetail("srv-verified", verifiedAtUtc: DateTimeOffset.UtcNow),
            ["srv-failed"] = CreateDetail(
                "srv-failed",
                ownershipClaimRequestedUtc: DateTimeOffset.UtcNow,
                ownershipError: "Token mismatch."),
            ["srv-pending"] = CreateDetail(
                "srv-pending",
                ownershipClaimRequestedUtc: DateTimeOffset.UtcNow,
                ownershipError: null),
        };

        IndexModel page = CreatePage(db, user, details);

        await page.OnGetAsync(CancellationToken.None);

        Assert.Equal(ServerClaimStatus.Verified, page.Claims.Single(x => x.RequestedServerId == "srv-verified").Status);
        Assert.Equal(ServerClaimStatus.Failed, page.Claims.Single(x => x.RequestedServerId == "srv-failed").Status);
        Assert.Equal("Token mismatch.", page.Claims.Single(x => x.RequestedServerId == "srv-failed").LastError);
        Assert.Equal(ServerClaimStatus.PendingVerification, page.Claims.Single(x => x.RequestedServerId == "srv-pending").Status);
        Assert.Equal("Verification pending.", page.Claims.Single(x => x.RequestedServerId == "srv-pending").LastError);
    }

    [Fact]
    public async Task OnGet_UserWithNoClaims_ReturnsEmptyList()
    {
        ApplicationDbContext db = PortalDbContextFactory.CreateInMemory();
        ApplicationUser user = new() { Id = "user-1" };

        IndexModel page = CreatePage(db, user, registryDetail: null);
        await page.OnGetAsync(CancellationToken.None);

        Assert.Empty(page.Claims);
    }

    [Fact]
    public async Task OnGet_OnlyReturnsClaimsOwnedByCurrentUser()
    {
        ApplicationDbContext db = PortalDbContextFactory.CreateInMemory();
        ApplicationUser user = new() { Id = "user-1" };
        AddClaim(db, "user-2", "srv-other", ServerClaimStatus.Verified);
        AddClaim(db, user.Id, "srv-mine", ServerClaimStatus.PendingVerification);

        IndexModel page = CreatePage(db, user, registryDetail: null);
        await page.OnGetAsync(CancellationToken.None);

        ServerClaim single = Assert.Single(page.Claims);
        Assert.Equal("user-1", single.OwnerUserId);
    }

    private static ServerClaim AddClaim(
        ApplicationDbContext db,
        string userId,
        string serverId,
        ServerClaimStatus status)
    {
        ServerClaim claim = new()
        {
            OwnerUserId = userId,
            DisplayName = "Test Server",
            TargetHost = "1.2.3.4",
            TargetPort = 7777,
            RequestedServerId = serverId,
            Status = status,
            SubmittedUtc = DateTimeOffset.UtcNow,
            UpdatedUtc = DateTimeOffset.UtcNow,
        };
        db.ServerClaims.Add(claim);
        db.SaveChanges();
        db.ChangeTracker.Clear();
        return claim;
    }

    private static ServerDiscoveryDetail CreateDetail(
        string serverId,
        DateTimeOffset? verifiedAtUtc = null,
        DateTimeOffset? ownershipClaimRequestedUtc = null,
        string? ownershipError = null)
    {
        ServerDiscoveryRow row = new(
            ProviderId: "official",
            ProviderKind: ServerDiscoveryProviderKind.Official,
            ServerId: serverId,
            DisplayName: "Test");

        return new ServerDiscoveryDetail(
            Row: row,
            OwnershipClaimRequestedUtc: ownershipClaimRequestedUtc,
            OwnershipVerificationError: ownershipError,
            VerifiedAtUtc: verifiedAtUtc);
    }

    private static IndexModel CreatePage(
        ApplicationDbContext db,
        ApplicationUser user,
        ServerDiscoveryDetail? registryDetail)
    {
        return CreatePage(
            db,
            user,
            registryDetail == null
                ? new Dictionary<string, ServerDiscoveryDetail>(StringComparer.Ordinal)
                : new Dictionary<string, ServerDiscoveryDetail>(StringComparer.Ordinal)
                {
                    [registryDetail.Row.ServerId] = registryDetail
                });
    }

    private static IndexModel CreatePage(
        ApplicationDbContext db,
        ApplicationUser user,
        IReadOnlyDictionary<string, ServerDiscoveryDetail> registryDetails)
    {
        Mock<Microsoft.AspNetCore.Identity.UserManager<ApplicationUser>> userMock =
            MockUserManager(user);

        FakeRegistryHandler handler = new(registryDetails);
        HttpClient http = new(handler);
        Microsoft.Extensions.Options.IOptions<Portal.Options.RegistryOptions> opts =
            Microsoft.Extensions.Options.Options.Create(
                new Portal.Options.RegistryOptions { BaseUrl = "http://localhost:5180" });
        RegistryQueryClient registry = new(http, opts);

        DefaultHttpContext httpContext = new();
        ActionContext actionContext = new(
            httpContext,
            new Microsoft.AspNetCore.Routing.RouteData(),
            new PageActionDescriptor());

        return new IndexModel(db, registry, userMock.Object)
        {
            PageContext = new PageContext(actionContext),
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

    private sealed class FakeRegistryHandler : HttpMessageHandler
    {
        private readonly IReadOnlyDictionary<string, ServerDiscoveryDetail> _details;

        public FakeRegistryHandler(IReadOnlyDictionary<string, ServerDiscoveryDetail> details)
            => _details = details;

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            string serverId = request.RequestUri?.Segments.LastOrDefault()?.TrimEnd('/') ?? string.Empty;
            HttpResponseMessage response;
            if (!_details.TryGetValue(serverId, out ServerDiscoveryDetail? detail))
                response = new HttpResponseMessage(HttpStatusCode.NotFound);
            else
                response = new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = JsonContent.Create(detail),
                };
            return Task.FromResult(response);
        }
    }
}
