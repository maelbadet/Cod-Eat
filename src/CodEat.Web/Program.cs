using CodEat.Web;
using System.Globalization;
using System.Net;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using PdfSharp.Fonts;
var builder = WebApplication.CreateBuilder(args);
builder.Logging.ClearProviders(); builder.Logging.AddConsole();
var sqlite = builder.Configuration.GetValue<bool>("Demo:Sqlite");
if (sqlite && !builder.Environment.IsDevelopment() && !builder.Configuration.GetValue<bool>("Demo:AllowProduction")) throw new InvalidOperationException("SQLite réservé à la démonstration explicite.");
builder.Services.AddDbContext<AppDb>(o => { if (builder.Configuration.GetValue<bool>("Demo:Sqlite")) o.UseSqlite(builder.Configuration.GetConnectionString("Demo") ?? "Data Source=codeat-demo.db"); else o.UseNpgsql(builder.Configuration.GetConnectionString("Database")); });
builder.Services.AddIdentity<AppUser, IdentityRole>(o => { o.Password.RequiredLength = 12; o.User.RequireUniqueEmail = true; o.Lockout.MaxFailedAccessAttempts = 5; o.SignIn.RequireConfirmedEmail = !builder.Environment.IsDevelopment(); }).AddEntityFrameworkStores<AppDb>().AddDefaultTokenProviders();
builder.Services.ConfigureApplicationCookie(o => { o.LoginPath = "/Account/Login"; o.AccessDeniedPath = "/Account/Denied"; o.Cookie.HttpOnly = true; o.Cookie.SecurePolicy = builder.Environment.IsDevelopment() ? CookieSecurePolicy.SameAsRequest : CookieSecurePolicy.Always; o.Cookie.SameSite = SameSiteMode.Lax; });
builder.Services.AddAuthorization(o => { o.FallbackPolicy = new AuthorizationPolicyBuilder().RequireAuthenticatedUser().Build(); o.AddPolicy("Admin", p => p.RequireRole("Admin")); });
builder.Services.AddRazorPages(o => { o.Conventions.AuthorizeFolder("/Admin", "Admin"); o.Conventions.AllowAnonymousToFolder("/Account"); o.Conventions.AllowAnonymousToFolder("/Public"); o.Conventions.ConfigureFilter(new Microsoft.AspNetCore.Mvc.AutoValidateAntiforgeryTokenAttribute()); });
builder.Services.Configure<Microsoft.AspNetCore.Mvc.MvcOptions>(o => { o.SuppressImplicitRequiredAttributeForNonNullableReferenceTypes = true; o.ModelBinderProviders.Insert(0, new DecimalBinderProvider()); });
builder.Services.AddRateLimiter(o => { o.RejectionStatusCode = 429; o.AddPolicy("public-forms", context => RateLimitPartition.GetFixedWindowLimiter(context.Connection.RemoteIpAddress?.ToString() ?? "unknown", _ => new FixedWindowRateLimiterOptions { PermitLimit = 15, Window = TimeSpan.FromMinutes(1), QueueLimit = 0 })); });
builder.Services.Configure<ForwardedHeadersOptions>(o => { o.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto; o.KnownIPNetworks.Clear(); o.KnownProxies.Clear(); foreach (var ip in builder.Configuration.GetSection("Proxy:KnownProxies").Get<string[]>() ?? []) o.KnownProxies.Add(IPAddress.Parse(ip)); foreach (var network in builder.Configuration.GetSection("Proxy:KnownNetworks").Get<string[]>() ?? []) o.KnownIPNetworks.Add(System.Net.IPNetwork.Parse(network)); });
builder.Services.AddDataProtection().PersistKeysToFileSystem(new DirectoryInfo(builder.Configuration["DataProtection:Path"] ?? Path.Combine(builder.Environment.ContentRootPath, ".keys"))).SetApplicationName("CodEat");
builder.Services.AddScoped<HouseholdService>(); builder.Services.AddScoped<BookStackSync>(); builder.Services.AddScoped<MailService>(); builder.Services.AddSingleton<PdfExport>();
builder.Services.AddSingleton<BookStackSyncQueue>();
builder.Services.AddHostedService<BookStackSyncWorker>();
builder.Services.AddHttpClient<BookStackClient>(c => c.Timeout = TimeSpan.FromSeconds(30)).ConfigurePrimaryHttpMessageHandler(() => new HttpClientHandler { AllowAutoRedirect = false });
GlobalFontSettings.FontResolver = new LocalFontResolver();
var app = builder.Build();
CultureInfo.DefaultThreadCurrentCulture = CultureInfo.GetCultureInfo("fr-FR"); CultureInfo.DefaultThreadCurrentUICulture = CultureInfo.GetCultureInfo("fr-FR");
if (args.Contains("--check-bookstack"))
{
    using var checkScope = app.Services.CreateScope();
    try
    {
        var pages = await checkScope.ServiceProvider.GetRequiredService<BookStackClient>().Read(CancellationToken.None);
        Console.WriteLine($"BookStack accessible : {pages.Count} page(s) lisible(s). Diagnostic en lecture seule, aucun catalogue modifié.");
    }
    catch (Exception failure)
    {
        var diagnostic = BookStackDiagnostic.From(failure);
        Console.Error.WriteLine($"[{diagnostic.Code}] {diagnostic.Guidance}");
        Environment.ExitCode = 1;
    }
    return;
}
if (args.Contains("--migrate") || args.Contains("--bootstrap") || args.Contains("--seed-demo")) {
 using var scope = app.Services.CreateScope(); var db = scope.ServiceProvider.GetRequiredService<AppDb>();
 if (sqlite) await db.Database.EnsureCreatedAsync(); else await db.Database.MigrateAsync();
 if (!await db.Settings.AnyAsync()) { db.Settings.Add(new()); await db.SaveChangesAsync(); }
 Console.WriteLine("Base de données prête : initialisation et migrations terminées.");
 if (args.Contains("--bootstrap")) await Setup.Bootstrap(scope.ServiceProvider, builder.Configuration);
 if (args.Contains("--seed-demo")) await Setup.Demo(scope.ServiceProvider); return;
}
if (builder.Configuration.GetSection("Proxy:KnownProxies").Get<string[]>()?.Length > 0 || builder.Configuration.GetSection("Proxy:KnownNetworks").Get<string[]>()?.Length > 0) app.UseForwardedHeaders();
if (!app.Environment.IsDevelopment()) { app.UseExceptionHandler("/Error"); app.UseHsts(); app.UseHttpsRedirection(); }
app.Use(async (context, next) => {
 context.Response.Headers.ContentSecurityPolicy = "default-src 'self'; script-src 'self'; style-src 'self'; img-src 'self'; font-src 'self'; object-src 'none'; base-uri 'self'; form-action 'self'; frame-ancestors 'none'";
 context.Response.Headers.XContentTypeOptions = "nosniff"; context.Response.Headers["Referrer-Policy"] = "same-origin";
 if (!context.Request.Path.StartsWithSegments("/catalogue") && context.Request.Path != "/sitemap.xml" && context.Request.Path != "/robots.txt") { context.Response.Headers["X-Robots-Tag"] = "noindex, nofollow"; context.Response.Headers.CacheControl = "no-store, private"; }
 await next();
});
app.UseStaticFiles(); app.UseRouting(); app.UseRateLimiter(); app.UseAuthentication(); app.UseAuthorization();
app.MapGet("/health", async (AppDb db) => await db.Database.CanConnectAsync() ? Results.Ok(new { status = "ok" }) : Results.StatusCode(503)).AllowAnonymous();
app.MapGet("/robots.txt", async (AppDb db, IConfiguration c) => Results.Text((await db.Settings.SingleAsync()).PublicCatalog ? $"User-agent: *\nAllow: /catalogue\nDisallow: /Account/\nDisallow: /Admin/\nSitemap: {Seo.Origin(c)}/sitemap.xml" : "User-agent: *\nDisallow: /", "text/plain")).AllowAnonymous();
app.MapGet("/sitemap.xml", async (AppDb db, IConfiguration c) => { var urls = new List<string>(); if ((await db.Settings.SingleAsync()).PublicCatalog) { urls.Add(Seo.Origin(c) + "/catalogue"); urls.AddRange((await db.Recipes.Where(x => x.Status == "Publiée").ToListAsync()).Select(x => $"{Seo.Origin(c)}/catalogue/{x.Id}/{x.Slug}")); } return Results.Text("<?xml version=\"1.0\" encoding=\"UTF-8\"?><urlset xmlns=\"http://www.sitemaps.org/schemas/sitemap/0.9\">" + string.Concat(urls.Select(x => "<url><loc>" + System.Security.SecurityElement.Escape(x) + "</loc></url>")) + "</urlset>", "application/xml"); }).AllowAnonymous();
app.MapRazorPages(); app.Run();
public partial class Program { }



