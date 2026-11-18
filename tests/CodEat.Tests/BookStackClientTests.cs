using System.Net;
using System.Text;
using CodEat.Web;
using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace CodEat.Tests;

public class BookStackClientTests
{
    static IConfiguration Configuration(params (string Key, string Value)[] overrides)
    {
        var values = new Dictionary<string, string?>
        {
            ["BookStack:BaseUrl"] = "https://private-bookstack.test/wiki/",
            ["BookStack:BookId"] = "7",
            ["BookStack:TokenId"] = "fake-id",
            ["BookStack:TokenSecret"] = "fake-secret"
        };
        foreach (var (key, value) in overrides) values[key] = value;
        return new ConfigurationBuilder().AddInMemoryCollection(values).Build();
    }

    sealed class Handler(Func<HttpRequestMessage, HttpResponseMessage> callback) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
            => Task.FromResult(callback(request));
    }

    static HttpResponseMessage Json(string value) => new(HttpStatusCode.OK) { Content = new StringContent(value, Encoding.UTF8, "application/json") };

    [Theory]
    [InlineData(401)]
    [InlineData(403)]
    [InlineData(404)]
    [InlineData(302)]
    [InlineData(429)]
    [InlineData(503)]
    public async Task HttpFailuresGiveSafeActionableDiagnostics(int status)
    {
        using var http = new HttpClient(new Handler(_ => new((HttpStatusCode)status)
        { Content = new StringContent("Do not expose https://private-bookstack.test fake-secret") }));
        var failure = await Assert.ThrowsAsync<BookStackFailure>(() => new BookStackClient(http, Configuration()).Read(default));
        var diagnostic = BookStackDiagnostic.From(failure);
        Assert.Equal($"BS_HTTP_{status}", diagnostic.Code);
        Assert.Contains(status.ToString(), diagnostic.Guidance);
        Assert.DoesNotContain("fake-secret", diagnostic.Guidance);
        Assert.DoesNotContain("private-bookstack.test", diagnostic.Guidance);
    }

    [Theory]
    [InlineData("BookStack:TokenSecret", "", "BS_CONFIG_TOKEN")]
    [InlineData("BookStack:BookId", "terre-mer", "BS_CONFIG_BOOK")]
    [InlineData("BookStack:BaseUrl", "https://example.test/books/terre-mer", "BS_CONFIG_URL")]
    [InlineData("BookStack:BaseUrl", "http://example.test", "BS_CONFIG_URL")]
    public async Task InvalidConfigurationFailsBeforeMakingRequests(string key, string value, string code)
    {
        var called = false;
        using var http = new HttpClient(new Handler(_ => { called = true; return Json("{}"); }));
        var failure = await Assert.ThrowsAsync<BookStackFailure>(() => new BookStackClient(http, Configuration((key, value))).Read(default));
        Assert.Equal(code, failure.Code);
        Assert.False(called);
    }

    [Fact]
    public async Task ReadsBookThenPaginatesUsingActualCountAndRequestsJson()
    {
        var offsets = new List<int>();
        using var http = new HttpClient(new Handler(request =>
        {
            Assert.Equal("Token", request.Headers.Authorization!.Scheme);
            Assert.Equal("fake-id:fake-secret", request.Headers.Authorization.Parameter);
            Assert.Contains(request.Headers.Accept, x => x.MediaType == "application/json");
            Assert.StartsWith("/wiki/api/", request.RequestUri!.AbsolutePath);
            if (request.RequestUri.AbsolutePath.EndsWith("books/7")) return Json("{\"id\":7}");
            if (request.RequestUri.AbsolutePath.EndsWith("pages"))
            {
                var query = request.RequestUri.Query;
                Assert.Contains("book_id", Uri.UnescapeDataString(query));
                var offset = query.Contains("offset=0&") ? 0 : 1;
                offsets.Add(offset);
                return Json($"{{\"total\":2,\"data\":[{{\"id\":{offset + 20}}}]}}");
            }
            var id = request.RequestUri.AbsolutePath.EndsWith("/20") ? 20 : 21;
            return Json($"{{\"id\":{id},\"book_id\":7,\"name\":\"Recette {id}\",\"markdown\":\"Cuire les légumes.\"}}");
        }));
        var client = new BookStackClient(http, Configuration());
        var pages = await client.Read(default);
        Assert.Equal(new[] { 0, 1 }, offsets);
        Assert.Equal(new[] { 20, 21 }, pages.Select(x => x.Id));
        await client.Read(default); // A reused typed client must not attempt to reset its BaseAddress.
    }

    [Fact]
    public async Task InaccessibleBookDoesNotMarkExistingRecipesAsMissing()
    {
        await using var app = new AppFactory();
        await app.Initialize();
        using var scope = app.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDb>();
        var existing = await db.Recipes.FirstAsync();
        existing.BookStackId = 20;
        await db.SaveChangesAsync();
        using var http = new HttpClient(new Handler(_ => new(HttpStatusCode.NotFound)));
        var sync = new BookStackSync(db, new BookStackClient(http, Configuration()));
        await Assert.ThrowsAsync<BookStackFailure>(() => sync.Run(default));
        db.ChangeTracker.Clear();
        Assert.False((await db.Recipes.SingleAsync(x => x.BookStackId == 20)).Missing);
    }

    sealed class RefusedBookStackApp : AppFactory
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            base.ConfigureWebHost(builder);
            builder.ConfigureAppConfiguration((_, config) => config.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["BookStack:Simulated"] = "false",
                ["BookStack:BaseUrl"] = "https://private-bookstack.test/",
                ["BookStack:BookId"] = "7",
                ["BookStack:TokenId"] = "fake-id",
                ["BookStack:TokenSecret"] = "fake-secret"
            }));
            builder.ConfigureServices(services => services.AddHttpClient<BookStackClient>()
                .ConfigurePrimaryHttpMessageHandler(() => new Handler(_ => new(HttpStatusCode.Unauthorized)
                { Content = new StringContent("private-bookstack.test fake-secret") })));
        }
    }

    [Fact]
    public async Task AdminSeesSafeDiagnosticWithoutSecretOrInternalUrl()
    {
        await using var app = new RefusedBookStackApp();
        await app.Initialize();
        using var client = app.Client();
        await AppFactory.Login(client, "admin@example.test");
        var page = await client.GetStringAsync("/Admin/Recipes");
        var response = await client.PostAsync("/Admin/Recipes?handler=Sync", new FormUrlEncodedContent(new Dictionary<string, string>
        { ["__RequestVerificationToken"] = AppFactory.Token(page) }));
        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        var result = await client.GetStringAsync("/Admin/Recipes");
        Assert.Contains("BS_HTTP_401", result);
        Assert.DoesNotContain("private-bookstack.test", result);
        Assert.DoesNotContain("fake-secret", result);
    }
}
