namespace UltraSim.Portal.Services;

public sealed class PortalDatabaseInitializer
{
    private readonly PortalSchemaBootstrapper _schemaBootstrapper;
    private readonly ForumSectionSeeder _forumSectionSeeder;
    private readonly PortalSensitiveDataCleanup _sensitiveDataCleanup;
    private readonly PortalRoleInitializer _roleInitializer;

    public PortalDatabaseInitializer(
        PortalSchemaBootstrapper schemaBootstrapper,
        ForumSectionSeeder forumSectionSeeder,
        PortalSensitiveDataCleanup sensitiveDataCleanup,
        PortalRoleInitializer roleInitializer)
    {
        _schemaBootstrapper = schemaBootstrapper;
        _forumSectionSeeder = forumSectionSeeder;
        _sensitiveDataCleanup = sensitiveDataCleanup;
        _roleInitializer = roleInitializer;
    }

    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        await _schemaBootstrapper.EnsureSchemaAsync(cancellationToken);
        await _forumSectionSeeder.SeedAsync(cancellationToken);
        await _sensitiveDataCleanup.ClearPersistedOwnershipTokensAsync(cancellationToken);
        await _roleInitializer.EnsureRolesExistAsync();
    }

    public Task SeedPrimaryOwnerAsync(string identity, CancellationToken cancellationToken = default)
    {
        return _roleInitializer.SeedPrimaryOwnerAsync(identity, cancellationToken);
    }
}
