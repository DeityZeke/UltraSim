using Microsoft.EntityFrameworkCore;
using UltraSim.Portal.Data;
using UltraSim.Portal.Models;
using UltraSim.Portal.Services;
using UltraSim.Portal.Tests.TestHelpers;
using Xunit;

namespace UltraSim.Portal.Tests.Services;

public sealed class AdminAuditServiceTests
{
    [Fact]
    public async Task WriteAsync_PersistsRecord()
    {
        using ApplicationDbContext db = PortalDbContextFactory.CreateInMemory();
        AdminAuditService service = new(db);

        await service.WriteAsync(
            actorUserId: "user-1",
            actorEmail: "actor@example.com",
            actorRole: "Admin",
            actionType: "listing.disable",
            targetType: "server",
            targetId: "server-abc",
            reason: "spamming",
            result: "success");

        AdminAuditRecord record = Assert.Single(await db.AdminAuditRecords.ToListAsync());
        Assert.Equal("user-1", record.ActorUserId);
        Assert.Equal("actor@example.com", record.ActorEmail);
        Assert.Equal("Admin", record.ActorRole);
        Assert.Equal("listing.disable", record.ActionType);
        Assert.Equal("server", record.TargetType);
        Assert.Equal("server-abc", record.TargetId);
        Assert.Equal("spamming", record.Reason);
        Assert.Equal("success", record.Result);
        Assert.True(record.CreatedUtc > DateTimeOffset.UtcNow.AddSeconds(-5));
    }

    [Fact]
    public async Task WriteAsync_TrimsWhitespace_OnEmailTargetReasonResultDetails()
    {
        using ApplicationDbContext db = PortalDbContextFactory.CreateInMemory();
        AdminAuditService service = new(db);

        await service.WriteAsync(
            actorUserId: "user-1",
            actorEmail: "  actor@example.com  ",
            actorRole: "Admin",
            actionType: "listing.disable",
            targetType: "server",
            targetId: "  server-abc  ",
            reason: "  spamming  ",
            result: "  success  ",
            details: "  some details  ");

        AdminAuditRecord record = Assert.Single(await db.AdminAuditRecords.ToListAsync());
        Assert.Equal("actor@example.com", record.ActorEmail);
        Assert.Equal("server-abc", record.TargetId);
        Assert.Equal("spamming", record.Reason);
        Assert.Equal("success", record.Result);
        Assert.Equal("some details", record.Details);
    }

    [Fact]
    public async Task WriteAsync_NullEmail_StoresNull()
    {
        using ApplicationDbContext db = PortalDbContextFactory.CreateInMemory();
        AdminAuditService service = new(db);

        await service.WriteAsync(
            actorUserId: "user-1",
            actorEmail: null,
            actorRole: "Admin",
            actionType: "test",
            targetType: "server",
            targetId: null,
            reason: "reason",
            result: "success");

        AdminAuditRecord record = Assert.Single(await db.AdminAuditRecords.ToListAsync());
        Assert.Null(record.ActorEmail);
        Assert.Null(record.TargetId);
    }

    [Fact]
    public async Task WriteAsync_WhitespaceEmail_StoresNull()
    {
        using ApplicationDbContext db = PortalDbContextFactory.CreateInMemory();
        AdminAuditService service = new(db);

        await service.WriteAsync(
            actorUserId: "user-1",
            actorEmail: "   ",
            actorRole: "Admin",
            actionType: "test",
            targetType: "server",
            targetId: null,
            reason: "reason",
            result: "success");

        AdminAuditRecord record = Assert.Single(await db.AdminAuditRecords.ToListAsync());
        Assert.Null(record.ActorEmail);
    }

    [Fact]
    public async Task WriteAsync_NullDetails_StoresNull()
    {
        using ApplicationDbContext db = PortalDbContextFactory.CreateInMemory();
        AdminAuditService service = new(db);

        await service.WriteAsync(
            actorUserId: "user-1",
            actorEmail: null,
            actorRole: "Admin",
            actionType: "test",
            targetType: "server",
            targetId: null,
            reason: "reason",
            result: "success",
            details: null);

        AdminAuditRecord record = Assert.Single(await db.AdminAuditRecords.ToListAsync());
        Assert.Null(record.Details);
    }

    [Fact]
    public async Task WriteAsync_WhitespaceDetails_StoresNull()
    {
        using ApplicationDbContext db = PortalDbContextFactory.CreateInMemory();
        AdminAuditService service = new(db);

        await service.WriteAsync(
            actorUserId: "user-1",
            actorEmail: null,
            actorRole: "Admin",
            actionType: "test",
            targetType: "server",
            targetId: null,
            reason: "reason",
            result: "success",
            details: "   ");

        AdminAuditRecord record = Assert.Single(await db.AdminAuditRecords.ToListAsync());
        Assert.Null(record.Details);
    }

    [Fact]
    public async Task WriteAsync_WithActorIpAddress_PersistsIp()
    {
        using ApplicationDbContext db = PortalDbContextFactory.CreateInMemory();
        AdminAuditService service = new(db);

        await service.WriteAsync(
            actorUserId: "user-1",
            actorEmail: null,
            actorRole: "Admin",
            actionType: "test",
            targetType: "server",
            targetId: null,
            reason: "reason",
            result: "success",
            actorIpAddress: "192.168.1.1");

        AdminAuditRecord record = Assert.Single(await db.AdminAuditRecords.ToListAsync());
        Assert.Equal("192.168.1.1", record.ActorIpAddress);
    }

    [Fact]
    public async Task WriteAsync_NullIpAddress_StoresNull()
    {
        using ApplicationDbContext db = PortalDbContextFactory.CreateInMemory();
        AdminAuditService service = new(db);

        await service.WriteAsync(
            actorUserId: "user-1",
            actorEmail: null,
            actorRole: "Admin",
            actionType: "test",
            targetType: "server",
            targetId: null,
            reason: "reason",
            result: "success",
            actorIpAddress: null);

        AdminAuditRecord record = Assert.Single(await db.AdminAuditRecords.ToListAsync());
        Assert.Null(record.ActorIpAddress);
    }

    [Fact]
    public async Task WriteAsync_MultipleRecords_AllPersisted()
    {
        using ApplicationDbContext db = PortalDbContextFactory.CreateInMemory();
        AdminAuditService service = new(db);

        await service.WriteAsync("u1", null, "Admin", "action1", "server", "s1", "reason", "success");
        await service.WriteAsync("u2", null, "Owner", "action2", "account", "a1", "reason", "failure");

        Assert.Equal(2, await db.AdminAuditRecords.CountAsync());
    }

    [Fact]
    public async Task WriteAsync_CreatedUtc_IsRecentUtcTime()
    {
        using ApplicationDbContext db = PortalDbContextFactory.CreateInMemory();
        AdminAuditService service = new(db);

        DateTimeOffset before = DateTimeOffset.UtcNow;
        await service.WriteAsync("u1", null, "Admin", "test", "server", null, "reason", "success");
        DateTimeOffset after = DateTimeOffset.UtcNow;

        AdminAuditRecord record = Assert.Single(await db.AdminAuditRecords.ToListAsync());
        Assert.True(record.CreatedUtc >= before.AddSeconds(-1));
        Assert.True(record.CreatedUtc <= after.AddSeconds(1));
    }
}
