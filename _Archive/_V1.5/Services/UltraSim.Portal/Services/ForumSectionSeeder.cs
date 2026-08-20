using Microsoft.EntityFrameworkCore;
using UltraSim.Portal.Data;
using UltraSim.Portal.Models;

namespace UltraSim.Portal.Services;

public sealed class ForumSectionSeeder
{
    private static readonly ForumSectionSeed[] DefaultForumSections =
    [
        new("support", "Platform Support", "Get help with portal accounts, sign-in issues, and platform-facing support questions.", 10),
        new("hosting-runtime", "Hosting and Runtime", "Discuss shard hosting, runtime behavior, deployment issues, and operational questions.", 20),
        new("registry-portal-issues", "Registry and Portal Issues", "Report listing, discovery, polling, and portal workflow problems.", 30),
        new("development-api-requests", "Development and API Requests", "Request integrations, APIs, tooling, and developer-facing platform improvements.", 40),
        new("architecture-discussion", "Architecture Discussion", "Talk through design decisions, platform internals, and long-horizon architectural ideas.", 50),
        new("creator-operator-discussion", "Creator and Operator Discussion", "A platform space for shard creators and operators to compare notes and share practices.", 60),
        new("general-community", "General Community Discussion", "General community conversation that belongs on the platform forum instead of individual listings.", 70),
    ];

    private readonly ApplicationDbContext _db;

    public ForumSectionSeeder(ApplicationDbContext db)
    {
        _db = db;
    }

    public async Task SeedAsync(CancellationToken cancellationToken = default)
    {
        Dictionary<string, ForumSection> existingSections = await _db.ForumSections
            .ToDictionaryAsync(x => x.Slug, StringComparer.Ordinal, cancellationToken);

        DateTime nowUtc = DateTime.UtcNow;
        foreach (ForumSectionSeed seed in DefaultForumSections)
        {
            if (!existingSections.TryGetValue(seed.Slug, out ForumSection? section))
            {
                _db.ForumSections.Add(new ForumSection
                {
                    Slug = seed.Slug,
                    Title = seed.Title,
                    Description = seed.Description,
                    DisplayOrder = seed.DisplayOrder,
                    CreatedUtc = nowUtc,
                });

                continue;
            }

            section.Title = seed.Title;
            section.Description = seed.Description;
            section.DisplayOrder = seed.DisplayOrder;
        }

        await _db.SaveChangesAsync(cancellationToken);
    }

    private sealed record ForumSectionSeed(
        string Slug,
        string Title,
        string Description,
        int DisplayOrder);
}
