using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using UltraSim.Portal.Authorization;
using UltraSim.Portal.Data;
using UltraSim.Portal.Models;
using UltraSim.Portal.Options;
using UltraSim.Portal.Services;
using UltraSim.Portal.Services.Forums;
using UltraSim.Portal.Services.RegistryApi;

var builder = WebApplication.CreateBuilder(args);

builder.Services.Configure<RegistryOptions>(builder.Configuration.GetSection(RegistryOptions.SectionName));
builder.Services.Configure<PortalOptions>(builder.Configuration.GetSection(PortalOptions.SectionName));
PortalOptions portalOptions = builder.Configuration.GetSection(PortalOptions.SectionName).Get<PortalOptions>() ?? new PortalOptions();

builder.Services.AddDbContext<ApplicationDbContext>(options =>
    options.UseSqlite(builder.Configuration.GetConnectionString("PortalDb") ?? "Data Source=portal.db"));

builder.Services
    .AddDefaultIdentity<ApplicationUser>(options =>
    {
        options.SignIn.RequireConfirmedAccount = portalOptions.RequireConfirmedAccount;
    })
    .AddRoles<IdentityRole>()
    .AddEntityFrameworkStores<ApplicationDbContext>();

RegistryOptions registryClientOptions = builder.Configuration
    .GetSection(RegistryOptions.SectionName)
    .Get<RegistryOptions>() ?? new RegistryOptions();
builder.Services.AddHttpClient<RegistryQueryClient>()
    .ConfigureHttpClient(client =>
    {
        client.Timeout = TimeSpan.FromSeconds(Math.Max(1, registryClientOptions.TimeoutSeconds));
    });
builder.Services.AddAuthorization(options =>
{
    options.AddPolicy(PortalPolicies.AdminPanel, policy =>
        policy.RequireRole(PortalRoles.PrimaryOwner, PortalRoles.Owner, PortalRoles.Admin));
    options.AddPolicy(PortalPolicies.ForumModeration, policy =>
        policy.RequireRole(PortalRoles.PrimaryOwner, PortalRoles.Owner, PortalRoles.Admin, PortalRoles.Moderator));
});
builder.Services.AddDatabaseDeveloperPageExceptionFilter();
builder.Services.AddRazorPages();
builder.Services.AddScoped<PortalDatabaseInitializer>();
builder.Services.AddScoped<PortalSchemaBootstrapper>();
builder.Services.AddScoped<PortalRoleInitializer>();
builder.Services.AddScoped<ForumSectionSeeder>();
builder.Services.AddScoped<PortalSensitiveDataCleanup>();
builder.Services.AddScoped<AdminAuditService>();
builder.Services.AddScoped<ForumIdentityService>();
builder.Services.AddScoped<ForumThreadQueryService>();
builder.Services.AddScoped<ForumReplyService>();
builder.Services.AddScoped<ForumModerationService>();
builder.Services.AddScoped<ForumModerationDashboardQueryService>();
builder.Services.AddScoped<ForumAuditWriter>();
builder.Services.AddScoped<UserActivityTracker>();
builder.Services.AddScoped<UserActivityTrackingMiddleware>();
builder.Services.AddScoped<VoteProjectionService>();

var app = builder.Build();

// Startup config validation.
{
    ILogger startupLogger = app.Services.GetRequiredService<ILoggerFactory>()
        .CreateLogger("UltraSim.Portal.Startup");

    RegistryOptions registryOpts = builder.Configuration.GetSection(RegistryOptions.SectionName).Get<RegistryOptions>() ?? new RegistryOptions();

    if (!Uri.TryCreate(registryOpts.BaseUrl, UriKind.Absolute, out Uri? registryUri))
        startupLogger.LogError("Registry:BaseUrl '{BaseUrl}' is not a valid absolute URI. Portal cannot reach the registry.", registryOpts.BaseUrl);
    else if (!app.Environment.IsDevelopment() && registryUri.Scheme == Uri.UriSchemeHttp)
        startupLogger.LogWarning("Registry:BaseUrl uses plain HTTP. The PortalApiToken will be sent unencrypted. Use HTTPS for production.");

    if (!app.Environment.IsDevelopment() && string.IsNullOrWhiteSpace(registryOpts.PortalApiToken))
        startupLogger.LogWarning("Registry:PortalApiToken is not configured. All portal claim requests to the registry will be rejected (401).");

    if (!app.Environment.IsDevelopment() && string.IsNullOrWhiteSpace(registryOpts.AdminApiToken))
        startupLogger.LogWarning("Registry:AdminApiToken is not configured. Portal admin registry actions will be rejected (401).");
}

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error");
    app.UseHsts();
}

using (IServiceScope scope = app.Services.CreateScope())
{
    PortalDatabaseInitializer initializer = scope.ServiceProvider.GetRequiredService<PortalDatabaseInitializer>();
    await initializer.InitializeAsync();

    if (TryGetSeedPrimaryOwnerIdentity(args, out string? seedIdentity))
    {
        await initializer.SeedPrimaryOwnerAsync(seedIdentity!);
        return;
    }
}

app.UseHttpsRedirection();
app.UseStaticFiles();
app.UseRouting();
app.UseAuthentication();
app.UseMiddleware<UserActivityTrackingMiddleware>();
app.UseAuthorization();
app.MapRazorPages();
app.Run();

static bool TryGetSeedPrimaryOwnerIdentity(string[] commandLineArgs, out string? identity)
{
    identity = null;

    for (int i = 0; i < commandLineArgs.Length; i++)
    {
        string arg = commandLineArgs[i];
        if (arg.StartsWith("--seed-primary-owner=", StringComparison.OrdinalIgnoreCase))
        {
            identity = arg["--seed-primary-owner=".Length..].Trim();
            return !string.IsNullOrWhiteSpace(identity);
        }

        if (!string.Equals(arg, "--seed-primary-owner", StringComparison.OrdinalIgnoreCase))
            continue;

        if (i + 1 >= commandLineArgs.Length)
            return false;

        identity = commandLineArgs[i + 1].Trim();
        return !string.IsNullOrWhiteSpace(identity);
    }

    return false;
}
