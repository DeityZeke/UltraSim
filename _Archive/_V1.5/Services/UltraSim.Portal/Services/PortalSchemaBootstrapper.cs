using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using UltraSim.Portal.Data;

namespace UltraSim.Portal.Services;

public sealed class PortalSchemaBootstrapper
{
    private readonly ApplicationDbContext _db;

    public PortalSchemaBootstrapper(ApplicationDbContext db)
    {
        _db = db;
    }

    public async Task EnsureSchemaAsync(CancellationToken cancellationToken = default)
    {
        await _db.Database.EnsureCreatedAsync(cancellationToken);
        await EnsureForumTablesAsync(cancellationToken);
        await EnsureServerClaimsTableAsync(cancellationToken);
        await EnsureServerVotesTableAsync(cancellationToken);
        await EnsureAspNetUsersColumnsAsync(cancellationToken);
        await EnsureAdminAuditTableAsync(cancellationToken);
    }

    private async Task EnsureServerClaimsTableAsync(CancellationToken cancellationToken)
    {
        await ExecuteSqlAsync(
            """
            CREATE TABLE IF NOT EXISTS "ServerClaims" (
                "Id" INTEGER NOT NULL CONSTRAINT "PK_ServerClaims" PRIMARY KEY AUTOINCREMENT,
                "OwnerUserId" TEXT NOT NULL,
                "DisplayName" TEXT NOT NULL,
                "Description" TEXT NULL,
                "TargetHost" TEXT NOT NULL,
                "TargetPort" INTEGER NOT NULL,
                "WebsiteUrl" TEXT NULL,
                "DiscordUrl" TEXT NULL,
                "RequestedServerId" TEXT NULL,
                "OwnershipToken" TEXT NULL,
                "Status" INTEGER NOT NULL,
                "LastError" TEXT NULL,
                "SubmittedUtc" TEXT NOT NULL,
                "UpdatedUtc" TEXT NOT NULL
            );
            """,
            cancellationToken);

        await ExecuteSqlAsync(
            """
            CREATE INDEX IF NOT EXISTS "IX_ServerClaims_OwnerUserId"
            ON "ServerClaims" ("OwnerUserId");
            """,
            cancellationToken);

        await ExecuteSqlAsync(
            """
            CREATE INDEX IF NOT EXISTS "IX_ServerClaims_RequestedServerId"
            ON "ServerClaims" ("RequestedServerId");
            """,
            cancellationToken);

        await EnsureColumnAsync(
            tableName: "ServerClaims",
            columnName: "OwnershipToken",
            alterSql: "ALTER TABLE \"ServerClaims\" ADD COLUMN \"OwnershipToken\" TEXT NULL;",
            cancellationToken: cancellationToken);
    }

    private async Task EnsureServerVotesTableAsync(CancellationToken cancellationToken)
    {
        await ExecuteSqlAsync(
            """
            CREATE TABLE IF NOT EXISTS "ServerVotes" (
                "Id" INTEGER NOT NULL CONSTRAINT "PK_ServerVotes" PRIMARY KEY AUTOINCREMENT,
                "UserId" TEXT NOT NULL,
                "ServerId" TEXT NOT NULL,
                "VotedUtc" TEXT NOT NULL,
                "LastChangedUtc" TEXT NOT NULL
            );
            """,
            cancellationToken);

        await ExecuteSqlAsync(
            """
            CREATE INDEX IF NOT EXISTS "IX_ServerVotes_UserId"
            ON "ServerVotes" ("UserId");
            """,
            cancellationToken);

        await ExecuteSqlAsync(
            """
            CREATE INDEX IF NOT EXISTS "IX_ServerVotes_ServerId"
            ON "ServerVotes" ("ServerId");
            """,
            cancellationToken);

        await ExecuteSqlAsync(
            """
            CREATE UNIQUE INDEX IF NOT EXISTS "IX_ServerVotes_UserId_ServerId"
            ON "ServerVotes" ("UserId", "ServerId");
            """,
            cancellationToken);
    }

    private async Task EnsureAspNetUsersColumnsAsync(CancellationToken cancellationToken)
    {
        await EnsureColumnAsync(
            tableName: "AspNetUsers",
            columnName: "IsPrimaryOwner",
            alterSql: "ALTER TABLE \"AspNetUsers\" ADD COLUMN \"IsPrimaryOwner\" INTEGER NOT NULL DEFAULT 0;",
            cancellationToken: cancellationToken);

        await EnsureColumnAsync(
            tableName: "AspNetUsers",
            columnName: "CreatedUtc",
            alterSql: "ALTER TABLE \"AspNetUsers\" ADD COLUMN \"CreatedUtc\" TEXT NOT NULL DEFAULT '1970-01-01 00:00:00';",
            backfillSql: "UPDATE \"AspNetUsers\" SET \"CreatedUtc\" = CURRENT_TIMESTAMP WHERE \"CreatedUtc\" = '1970-01-01 00:00:00' OR \"CreatedUtc\" IS NULL;",
            cancellationToken: cancellationToken);

        await EnsureColumnAsync(
            tableName: "AspNetUsers",
            columnName: "LastActiveUtc",
            alterSql: "ALTER TABLE \"AspNetUsers\" ADD COLUMN \"LastActiveUtc\" TEXT NOT NULL DEFAULT '1970-01-01 00:00:00';",
            backfillSql: "UPDATE \"AspNetUsers\" SET \"LastActiveUtc\" = CURRENT_TIMESTAMP WHERE \"LastActiveUtc\" = '1970-01-01 00:00:00' OR \"LastActiveUtc\" IS NULL;",
            cancellationToken: cancellationToken);
    }

    private async Task EnsureAdminAuditTableAsync(CancellationToken cancellationToken)
    {
        await ExecuteSqlAsync(
            """
            CREATE TABLE IF NOT EXISTS "AdminAuditRecords" (
                "Id" INTEGER NOT NULL CONSTRAINT "PK_AdminAuditRecords" PRIMARY KEY AUTOINCREMENT,
                "ActorUserId" TEXT NOT NULL,
                "ActorEmail" TEXT NULL,
                "ActorRole" TEXT NOT NULL,
                "ActionType" TEXT NOT NULL,
                "TargetType" TEXT NOT NULL,
                "TargetId" TEXT NULL,
                "Reason" TEXT NOT NULL,
                "Result" TEXT NOT NULL,
                "Details" TEXT NULL,
                "ActorIpAddress" TEXT NULL,
                "CreatedUtc" TEXT NOT NULL
            );
            """,
            cancellationToken);

        await ExecuteSqlAsync(
            """
            CREATE INDEX IF NOT EXISTS "IX_AdminAuditRecords_CreatedUtc"
            ON "AdminAuditRecords" ("CreatedUtc");
            """,
            cancellationToken);

        await ExecuteSqlAsync(
            """
            CREATE INDEX IF NOT EXISTS "IX_AdminAuditRecords_ActorUserId"
            ON "AdminAuditRecords" ("ActorUserId");
            """,
            cancellationToken);

        await EnsureColumnAsync(
            tableName: "AdminAuditRecords",
            columnName: "ActorIpAddress",
            alterSql: "ALTER TABLE \"AdminAuditRecords\" ADD COLUMN \"ActorIpAddress\" TEXT NULL;",
            cancellationToken: cancellationToken);
    }

    private async Task EnsureForumTablesAsync(CancellationToken cancellationToken)
    {
        await ExecuteSqlAsync(
            """
            CREATE TABLE IF NOT EXISTS "ForumSections" (
                "Id" INTEGER NOT NULL CONSTRAINT "PK_ForumSections" PRIMARY KEY AUTOINCREMENT,
                "Slug" TEXT NOT NULL,
                "Title" TEXT NOT NULL,
                "Description" TEXT NOT NULL,
                "DisplayOrder" INTEGER NOT NULL,
                "IsReadOnly" INTEGER NOT NULL DEFAULT 0,
                "CreatedUtc" TEXT NOT NULL
            );
            """,
            cancellationToken);

        await ExecuteSqlAsync(
            """
            CREATE UNIQUE INDEX IF NOT EXISTS "IX_ForumSections_Slug"
            ON "ForumSections" ("Slug");
            """,
            cancellationToken);

        await ExecuteSqlAsync(
            """
            CREATE INDEX IF NOT EXISTS "IX_ForumSections_DisplayOrder"
            ON "ForumSections" ("DisplayOrder");
            """,
            cancellationToken);

        await ExecuteSqlAsync(
            """
            CREATE TABLE IF NOT EXISTS "ForumThreads" (
                "Id" INTEGER NOT NULL CONSTRAINT "PK_ForumThreads" PRIMARY KEY AUTOINCREMENT,
                "SectionId" INTEGER NOT NULL,
                "AuthorUserId" TEXT NOT NULL,
                "LastPostUserId" TEXT NULL,
                "Title" TEXT NOT NULL,
                "IsPinned" INTEGER NOT NULL DEFAULT 0,
                "IsLocked" INTEGER NOT NULL DEFAULT 0,
                "IsHidden" INTEGER NOT NULL DEFAULT 0,
                "CreatedUtc" TEXT NOT NULL,
                "UpdatedUtc" TEXT NOT NULL,
                "LastPostedUtc" TEXT NOT NULL,
                CONSTRAINT "FK_ForumThreads_ForumSections_SectionId"
                    FOREIGN KEY ("SectionId") REFERENCES "ForumSections" ("Id") ON DELETE CASCADE
            );
            """,
            cancellationToken);

        await ExecuteSqlAsync(
            """
            CREATE INDEX IF NOT EXISTS "IX_ForumThreads_SectionId"
            ON "ForumThreads" ("SectionId");
            """,
            cancellationToken);

        await ExecuteSqlAsync(
            """
            CREATE INDEX IF NOT EXISTS "IX_ForumThreads_SectionId_IsHidden_IsPinned_LastPostedUtc"
            ON "ForumThreads" ("SectionId", "IsHidden", "IsPinned", "LastPostedUtc");
            """,
            cancellationToken);

        await ExecuteSqlAsync(
            """
            CREATE INDEX IF NOT EXISTS "IX_ForumThreads_AuthorUserId"
            ON "ForumThreads" ("AuthorUserId");
            """,
            cancellationToken);

        await ExecuteSqlAsync(
            """
            CREATE INDEX IF NOT EXISTS "IX_ForumThreads_LastPostUserId"
            ON "ForumThreads" ("LastPostUserId");
            """,
            cancellationToken);

        await ExecuteSqlAsync(
            """
            CREATE TABLE IF NOT EXISTS "ForumPosts" (
                "Id" INTEGER NOT NULL CONSTRAINT "PK_ForumPosts" PRIMARY KEY AUTOINCREMENT,
                "ThreadId" INTEGER NOT NULL,
                "AuthorUserId" TEXT NOT NULL,
                "Content" TEXT NOT NULL,
                "IsHidden" INTEGER NOT NULL DEFAULT 0,
                "CreatedUtc" TEXT NOT NULL,
                "UpdatedUtc" TEXT NOT NULL,
                CONSTRAINT "FK_ForumPosts_ForumThreads_ThreadId"
                    FOREIGN KEY ("ThreadId") REFERENCES "ForumThreads" ("Id") ON DELETE CASCADE
            );
            """,
            cancellationToken);

        await ExecuteSqlAsync(
            """
            CREATE INDEX IF NOT EXISTS "IX_ForumPosts_ThreadId"
            ON "ForumPosts" ("ThreadId");
            """,
            cancellationToken);

        await ExecuteSqlAsync(
            """
            CREATE INDEX IF NOT EXISTS "IX_ForumPosts_AuthorUserId"
            ON "ForumPosts" ("AuthorUserId");
            """,
            cancellationToken);

        await ExecuteSqlAsync(
            """
            CREATE INDEX IF NOT EXISTS "IX_ForumPosts_ThreadId_CreatedUtc"
            ON "ForumPosts" ("ThreadId", "IsHidden", "CreatedUtc");
            """,
            cancellationToken);

        await EnsureColumnAsync(
            tableName: "ForumPosts",
            columnName: "IsHidden",
            alterSql: "ALTER TABLE \"ForumPosts\" ADD COLUMN \"IsHidden\" INTEGER NOT NULL DEFAULT 0;",
            cancellationToken: cancellationToken);
    }

    private Task ExecuteSqlAsync(string sql, CancellationToken cancellationToken)
    {
        return _db.Database.ExecuteSqlRawAsync(sql, cancellationToken);
    }

    private async Task EnsureColumnAsync(
        string tableName,
        string columnName,
        string alterSql,
        CancellationToken cancellationToken,
        string? backfillSql = null)
    {
        SqliteConnection connection = (SqliteConnection)_db.Database.GetDbConnection();
        await connection.OpenAsync(cancellationToken);
        try
        {
            await using SqliteCommand command = connection.CreateCommand();
            command.CommandText = $"PRAGMA table_info(\"{tableName}\");";
            await using SqliteDataReader reader = await command.ExecuteReaderAsync(cancellationToken);

            bool hasColumn = false;
            while (await reader.ReadAsync(cancellationToken))
            {
                if (string.Equals(reader.GetString(1), columnName, StringComparison.Ordinal))
                {
                    hasColumn = true;
                    break;
                }
            }

            await reader.CloseAsync();

            if (hasColumn)
                return;

            await using SqliteCommand alter = connection.CreateCommand();
            alter.CommandText = alterSql;
            await alter.ExecuteNonQueryAsync(cancellationToken);

            if (string.IsNullOrWhiteSpace(backfillSql))
                return;

            await using SqliteCommand backfill = connection.CreateCommand();
            backfill.CommandText = backfillSql;
            await backfill.ExecuteNonQueryAsync(cancellationToken);
        }
        finally
        {
            await connection.CloseAsync();
        }
    }
}
