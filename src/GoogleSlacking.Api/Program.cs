using GoogleSlacking.Api.Auth;
using GoogleSlacking.Api.Endpoints;
using GoogleSlacking.Api.Realtime;
using GoogleSlacking.Domain;
using GoogleSlacking.Infrastructure;
using GoogleSlacking.Infrastructure.Auth;
using GoogleSlacking.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);

// Layered config: appsettings -> appsettings.{Env} -> user secrets (dev, shared solution ID) -> env vars
// -> appsettings.Local.json (gitignored, last so it's easy to override locally).
builder.Configuration.AddJsonFile("appsettings.Local.json", optional: true, reloadOnChange: true);

builder.Services.AddSingleton<IRealtimeNotifier, SignalRNotifier>(); // before AddInfrastructure (it TryAdds a null notifier)
builder.Services.AddInfrastructure(builder.Configuration);
builder.Services.AddGoogleSignIn(builder.Configuration);
builder.Services.AddSignalR();
builder.Services.AddProblemDetails();

var app = builder.Build();

if (app.Environment.IsDevelopment())
    await DevSetup.InitializeDatabaseAsync(app.Services, app.Configuration);

app.UseExceptionHandler();
app.UseDefaultFiles();
app.UseStaticFiles();
app.UseAuthentication();

// Tenant for HTTP requests comes from the auth cookie.
app.Use(async (ctx, next) =>
{
    if (ctx.User.Identity?.IsAuthenticated == true)
        ctx.RequestServices.GetRequiredService<ITenantContext>().Set(ctx.User.TenantId());
    try
    {
        await next();
    }
    catch (ReauthenticationRequiredException) when (!ctx.Response.HasStarted)
    {
        ctx.Response.StatusCode = StatusCodes.Status401Unauthorized;
        await ctx.Response.WriteAsJsonAsync(new { reauth = true });
    }
});

app.UseAuthorization();

app.MapAuthEndpoints();
app.MapChatEndpoints();
app.MapHub<ChatHub>("/hubs/chat");
app.MapGet("/healthz", () => Results.Ok("ok"));
app.MapFallbackToFile("index.html"); // SPA (built into wwwroot in production)

app.Run();

public partial class Program; // for WebApplicationFactory

internal static class DevSetup
{
    public static async Task InitializeDatabaseAsync(IServiceProvider services, IConfiguration config)
    {
        await using var scope = services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        if (db.Database.IsRelational() && db.Database.GetMigrations().Any())
            await db.Database.MigrateAsync();
        else
            await db.Database.EnsureCreatedAsync();

        // Seed tenants from config ("Tenants": [{ "Name": "...", "Domain": "..." }]).
        foreach (var t in config.GetSection("Tenants").GetChildren())
        {
            var domain = t["Domain"];
            if (string.IsNullOrWhiteSpace(domain) || await db.Tenants.AnyAsync(x => x.Domain == domain)) continue;
            db.Tenants.Add(new Tenant
            {
                Id = Guid.NewGuid(),
                Name = t["Name"] ?? domain,
                Domain = domain,
                CreatedAt = DateTimeOffset.UtcNow,
            });
        }
        await db.SaveChangesAsync();
    }
}
