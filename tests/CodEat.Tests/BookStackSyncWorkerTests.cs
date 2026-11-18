using System.Net;
using System.Net.Http.Json;
using System.Text;
using CodEat.Web;
using Microsoft.AspNetCore.Hosting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace CodEat.Tests;

public class BookStackSyncWorkerTests
{
    [Fact]
    public async Task SimultaneousClicksAcceptOnlyOneJob()
    {
        var queue = new BookStackSyncQueue();
        var accepted = await Task.WhenAll(Enumerable.Range(0, 20).Select(_ => Task.Run(queue.TryEnqueue)));
        Assert.Single(accepted, x => x);
        var id = await queue.ReadAsync(default);
        queue.Started(id);
        Assert.False(queue.TryEnqueue());
        queue.Report(id, new(3, 10, "Lecture"));
        Assert.Equal(3, queue.Snapshot.Completed);
        queue.Succeeded(id, "Terminé");
        Assert.False(queue.Snapshot.IsBusy);
        Assert.True(queue.TryEnqueue());
        Assert.NotEqual(id, queue.Snapshot.JobId);
    }

    public sealed class SlowApi : HttpMessageHandler
    {
        public readonly TaskCompletionSource<bool> Started = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public readonly TaskCompletionSource<bool> Release = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public int Calls;
        public volatile bool FailDetail;

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            Interlocked.Increment(ref Calls);
            Started.TrySetResult(true);
            await Release.Task.WaitAsync(ct);
            var path = request.RequestUri!.AbsolutePath;
            if (FailDetail && path.EndsWith("pages/20")) return new(HttpStatusCode.GatewayTimeout);
            var json = path.EndsWith("books/7") ? "{\"id\":7}"
                : path.EndsWith("pages") ? "{\"total\":1,\"data\":[{\"id\":20}]}"
                : "{\"id\":20,\"book_id\":7,\"name\":\"Plat importé\",\"markdown\":\"Cuire les légumes.\"}";
            return new(HttpStatusCode.OK) { Content = new StringContent(json, Encoding.UTF8, "application/json") };
        }
    }

    sealed class SlowApp : AppFactory
    {
        public readonly SlowApi Api = new();
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
                .ConfigurePrimaryHttpMessageHandler(() => Api));
        }
    }

    [Fact]
    public async Task StartReturnsBeforeBookStackRepliesAndImportSurvivesRequestCancellation()
    {
        await using var app = new SlowApp();
        await app.Initialize();
        using var client = app.Client();
        await AppFactory.Login(client, "admin@example.test");
        var html = await client.GetStringAsync("/Admin/Recipes");
        var form = new Dictionary<string, string> { ["__RequestVerificationToken"] = AppFactory.Token(html) };
        using var request = new CancellationTokenSource(TimeSpan.FromSeconds(3));
        var response = await client.PostAsync("/Admin/Recipes?handler=Sync", new FormUrlEncodedContent(form), request.Token);
        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        await app.Api.Started.Task.WaitAsync(TimeSpan.FromSeconds(3));
        request.Cancel();

        var running = await client.GetFromJsonAsync<BookStackSyncStatus>("/Admin/Recipes?handler=SyncStatus");
        Assert.Equal("Running", running!.State);
        Assert.True(running.IsBusy);
        Assert.Equal(HttpStatusCode.Redirect, (await client.PostAsync("/Admin/Recipes?handler=Sync", new FormUrlEncodedContent(form))).StatusCode);
        Assert.Equal(running.JobId, app.Services.GetRequiredService<BookStackSyncQueue>().Snapshot.JobId);
        Assert.Equal(1, app.Api.Calls);

        app.Api.Release.TrySetResult(true);
        BookStackSyncStatus? completed = null;
        for (var attempt = 0; attempt < 200; attempt++)
        {
            completed = await client.GetFromJsonAsync<BookStackSyncStatus>("/Admin/Recipes?handler=SyncStatus");
            if (!completed!.IsBusy) break;
            await Task.Delay(20);
        }
        Assert.Equal("Succeeded", completed!.State);
        Assert.Equal(1, completed.Completed);
        Assert.Equal(1, completed.Total);
        Assert.Equal(3, app.Api.Calls);
        using var scope = app.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDb>();
        Assert.Single(await db.Recipes.Where(x => x.BookStackId == 20).ToListAsync());
    }

    [Fact]
    public async Task ProgressRequiresAdministratorAndIsNeverCached()
    {
        await using var app = new AppFactory();
        await app.Initialize();
        using var anonymous = app.Client();
        Assert.Equal(HttpStatusCode.Redirect, (await anonymous.GetAsync("/Admin/Recipes?handler=SyncStatus")).StatusCode);
        using var member = app.Client();
        await AppFactory.Login(member, "member@example.test");
        Assert.Equal(HttpStatusCode.Redirect, (await member.GetAsync("/Admin/Recipes?handler=SyncStatus")).StatusCode);
        using var admin = app.Client();
        await AppFactory.Login(admin, "admin@example.test");
        var result = await admin.GetAsync("/Admin/Recipes?handler=SyncStatus");
        Assert.Equal(HttpStatusCode.OK, result.StatusCode);
        Assert.True(result.Headers.CacheControl!.NoStore);
        Assert.Equal("application/json", result.Content.Headers.ContentType!.MediaType);
    }

    [Fact]
    public async Task Upstream504PreservesCatalogAndWorkerCanRetry()
    {
        await using var app = new SlowApp();
        app.Api.FailDetail = true;
        app.Api.Release.TrySetResult(true);
        await app.Initialize();
        using var client = app.Client();
        await AppFactory.Login(client, "admin@example.test");

        async Task<BookStackSyncStatus> StartAndWait()
        {
            var html = await client.GetStringAsync("/Admin/Recipes");
            await client.PostAsync("/Admin/Recipes?handler=Sync", new FormUrlEncodedContent(new Dictionary<string, string>
            { ["__RequestVerificationToken"] = AppFactory.Token(html) }));
            for (var attempt = 0; attempt < 200; attempt++)
            {
                var state = (await client.GetFromJsonAsync<BookStackSyncStatus>("/Admin/Recipes?handler=SyncStatus"))!;
                if (!state.IsBusy) return state;
                await Task.Delay(20);
            }
            throw new TimeoutException("La tâche de synchronisation ne se termine pas.");
        }

        var failed = await StartAndWait();
        Assert.Equal("Failed", failed.State);
        Assert.Equal("BS_HTTP_504", failed.DiagnosticCode);
        using (var scope = app.Services.CreateScope())
            Assert.Equal(2, await scope.ServiceProvider.GetRequiredService<AppDb>().Recipes.CountAsync());
        app.Api.FailDetail = false;
        Assert.Equal("Succeeded", (await StartAndWait()).State);
        using var verify = app.Services.CreateScope();
        Assert.Equal(3, await verify.ServiceProvider.GetRequiredService<AppDb>().Recipes.CountAsync());
    }
}
