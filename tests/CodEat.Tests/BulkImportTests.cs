using System.Net;
using System.Text.RegularExpressions;
using CodEat.Web;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace CodEat.Tests;

public class BulkImportTests
{
    private const string Route="/Admin/ReviewImports";
    private static string Ticket(string html)=>WebUtility.HtmlDecode(Regex.Match(html,"name=\"Ticket\" value=\"([^\"]+)\"").Groups[1].Value);
    private static FormUrlEncodedContent Form(string html,bool confirm=true)=>new(new Dictionary<string,string>{
        ["__RequestVerificationToken"]=AppFactory.Token(html),["Ticket"]=Ticket(html),["Confirm"]=confirm.ToString()});
    private static Recipe Imported(int n)=>new(){BookStackId=10000+n,Title=$"Import {n:0000}",Steps="Cuire puis servir.",
        Ingredients=[new("riz",200,"g","Épicerie")],AllergensVerified=false,Status="Brouillon",
        ImportStateJson=System.Text.Json.JsonSerializer.Serialize(new RecipeImportState{Version=RecipePageParser.Version,SourceKind="recipe"})};

    [Fact]
    public async Task LargeBatchNeedsPreviewAndConfirmationKeepsDraftsPrivateAndBoundsLists()
    {
        await using var app=new AppFactory(); await app.Initialize();
        using(var scope=app.Services.CreateScope())
        {
            var db=scope.ServiceProvider.GetRequiredService<AppDb>();
            db.Recipes.AddRange(Enumerable.Range(1,2600).Select(Imported));
            db.Recipes.Add(new Recipe{BookStackId=9999,Title="Page de navigation",Steps="Liens",Ingredients=[]});
            var legacy=Imported(3000); legacy.ImportStateJson="{}"; legacy.Title="Ancien index enrichi";
            db.Recipes.Add(legacy);
            var navigation=Imported(3001); navigation.Title="France — index des recettes";
            navigation.ImportStateJson=System.Text.Json.JsonSerializer.Serialize(new RecipeImportState{Version=RecipePageParser.Version,SourceKind="unrecognized"});
            db.Recipes.Add(navigation);
            (await db.Settings.SingleAsync()).PublicCatalog=true;
            await db.SaveChangesAsync();
        }
        using var admin=app.Client(); await AppFactory.Login(admin,"admin@example.test");
        using var member=app.Client(); await AppFactory.Login(member,"member@example.test");
        Assert.Equal(HttpStatusCode.Redirect,(await member.GetAsync(Route)).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest,(await admin.PostAsync(Route+"?handler=Apply",new FormUrlEncodedContent([]))).StatusCode);
        var html=await admin.GetStringAsync(Route);
        var preview=await admin.PostAsync(Route+"?handler=Preview",Form(html));
        Assert.Equal(HttpStatusCode.OK,preview.StatusCode);
        html=await preview.Content.ReadAsStringAsync();
        Assert.Contains("2600 recettes",WebUtility.HtmlDecode(html));
        Assert.NotEmpty(Ticket(html));
        using(var scope=app.Services.CreateScope()) Assert.Equal(0,await scope.ServiceProvider.GetRequiredService<AppDb>().Recipes.CountAsync(x=>x.AvailablePrivately));
        var unconfirmed=await admin.PostAsync(Route+"?handler=Apply",Form(html,false));
        Assert.Contains("Cochez la confirmation",WebUtility.HtmlDecode(await unconfirmed.Content.ReadAsStringAsync()));
        Assert.Equal(HttpStatusCode.Redirect,(await admin.PostAsync(Route+"?handler=Apply",Form(html))).StatusCode);
        using(var scope=app.Services.CreateScope())
        {
            var db=scope.ServiceProvider.GetRequiredService<AppDb>();
            Assert.Equal(2600,await db.Recipes.CountAsync(x=>x.AvailablePrivately));
            Assert.Equal(2600,await db.Recipes.CountAsync(x=>x.AvailablePrivately && x.Status=="Brouillon" && !x.AllergensVerified));
            Assert.False((await db.Recipes.SingleAsync(x=>x.BookStackId==9999)).AvailablePrivately);
            Assert.False((await db.Recipes.SingleAsync(x=>x.BookStackId==13000)).AvailablePrivately);
            Assert.False((await db.Recipes.SingleAsync(x=>x.BookStackId==13001)).AvailablePrivately);
        }
        var catalog=WebUtility.HtmlDecode(await member.GetStringAsync("/?Tab=recipes"));
        Assert.Equal(24,Regex.Matches(catalog,"<article class=\"panel\">").Count);
        Assert.Contains("Allergènes non vérifiés",catalog);
        Assert.Contains("Suivante",catalog);
        var last=WebUtility.HtmlDecode(await member.GetStringAsync("/?Tab=recipes&CatalogPage=109"));
        Assert.Contains("Import 2600",last);
        var week=await member.GetStringAsync("/");
        Assert.True(Regex.Matches(week,"<option value=\"\\d+\">Import").Count<=14*50);
        var filtered=WebUtility.HtmlDecode(await member.GetStringAsync("/?Tab=recipes&Search=Import&Allergens=lait"));
        Assert.DoesNotContain("<article class=\"panel\">",filtered);
        using var anonymous=app.Client();
        Assert.DoesNotContain("Import 0001",await anonymous.GetStringAsync("/catalogue"));
        using(var scope=app.Services.CreateScope())
        {
            var id=await scope.ServiceProvider.GetRequiredService<AppDb>().Recipes.Where(x=>x.BookStackId==10001).Select(x=>x.Id).SingleAsync();
            var assign=new Dictionary<string,string>{["__RequestVerificationToken"]=AppFactory.Token(week),["revision"]=AppFactory.Revision(week),
                ["action"]="assign",["recipeId"]=id.ToString(),["day"]="0",["slot"]="Midi",["portions"]="2",["prepared"]="2"};
            Assert.Equal(HttpStatusCode.Redirect,(await member.PostAsync("/",new FormUrlEncodedContent(assign))).StatusCode);
        }
        var shopping=WebUtility.HtmlDecode(await member.GetStringAsync("/?Tab=shopping"));
        Assert.Contains("allergènes non vérifiés",shopping);
    }

    [Fact]
    public async Task PublicBatchPublishesOnlyReviewedRecipesAndCannotReusePrivateConsent()
    {
        await using var app=new AppFactory();await app.Initialize();
        using(var scope=app.Services.CreateScope())
        {
            var db=scope.ServiceProvider.GetRequiredService<AppDb>();
            foreach(var n in Enumerable.Range(1,26)) {var r=Imported(n);r.AllergensVerified=true;r.AvailablePrivately=true;r.Status=n==1?"Validée":"Brouillon";db.Recipes.Add(r);}
            db.Recipes.Add(Imported(30)); // Recognized but allergens not reviewed.
            var index=Imported(31);index.AllergensVerified=true;
            index.ImportStateJson=System.Text.Json.JsonSerializer.Serialize(new RecipeImportState{Version=RecipePageParser.Version,SourceKind="unrecognized"});db.Recipes.Add(index);
            await db.SaveChangesAsync();
        }
        using var admin=app.Client();await AppFactory.Login(admin,"admin@example.test");
        var html=await admin.GetStringAsync(Route+"?Mode=public");
        Assert.Contains("fermé",WebUtility.HtmlDecode(html));
        var preview=await admin.PostAsync(Route+"?handler=Preview&Mode=public",Form(html));
        html=await preview.Content.ReadAsStringAsync();
        Assert.Contains("26 recettes à publier",WebUtility.HtmlDecode(html));
        var mismatch=await admin.PostAsync(Route+"?handler=Apply&Mode=private",Form(html));
        Assert.Contains("Aperçu invalide",WebUtility.HtmlDecode(await mismatch.Content.ReadAsStringAsync()));
        Assert.Equal(HttpStatusCode.Redirect,(await admin.PostAsync(Route+"?handler=Apply&Mode=public",Form(html))).StatusCode);
        int publishedId;string slug;
        using(var scope=app.Services.CreateScope())
        {
            var db=scope.ServiceProvider.GetRequiredService<AppDb>();
            Assert.Equal(26,await db.Recipes.CountAsync(x=>x.BookStackId!=null && x.Status=="Publiée"));
            Assert.Equal(2,await db.Recipes.CountAsync(x=>x.BookStackId!=null && x.Status=="Brouillon"));
            Assert.False((await db.Recipes.SingleAsync(x=>x.BookStackId==10030)).AllergensVerified);
            var r=await db.Recipes.SingleAsync(x=>x.BookStackId==10001);publishedId=r.Id;slug=r.Slug;
            Assert.NotEmpty(slug);Assert.True(r.AvailablePrivately);Assert.True(r.AllergensVerified);
            (await db.Settings.SingleAsync()).PublicCatalog=true;await db.SaveChangesAsync();
        }
        using var anonymous=app.Client();
        var catalog=await anonymous.GetStringAsync("/catalogue");
        Assert.Equal(24,Regex.Matches(catalog,"<article class=\"panel\">").Count);
        Assert.Contains("Suivante",catalog);
        Assert.DoesNotContain("Import 0030",catalog);
        var page=WebUtility.HtmlDecode(await anonymous.GetStringAsync($"/catalogue/{publishedId}/{slug}"));
        Assert.Contains("Import 0001",page);Assert.Contains("application/ld+json",page);
        Assert.Contains("Import 0026",WebUtility.HtmlDecode(await anonymous.GetStringAsync("/catalogue?CatalogPage=2")));
        Assert.Contains($"/catalogue/{publishedId}/",await anonymous.GetStringAsync("/sitemap.xml"));
    }

    [Fact]
    public async Task ChangedRecipeRejectsWholePreviewAndInvalidTicketsDoNotActivate()
    {
        await using var app=new AppFactory(); await app.Initialize();
        using(var scope=app.Services.CreateScope()) {var db=scope.ServiceProvider.GetRequiredService<AppDb>();db.Recipes.AddRange(Imported(1),Imported(2));await db.SaveChangesAsync();}
        using var admin=app.Client();await AppFactory.Login(admin,"admin@example.test");
        var html=await admin.GetStringAsync(Route);
        html=await (await admin.PostAsync(Route+"?handler=Preview",Form(html))).Content.ReadAsStringAsync();
        using(var scope=app.Services.CreateScope()) await scope.ServiceProvider.GetRequiredService<AppDb>().Recipes.Where(x=>x.BookStackId==10001).ExecuteUpdateAsync(s=>s.SetProperty(x=>x.Revision,Guid.NewGuid()));
        var response=await admin.PostAsync(Route+"?handler=Apply",Form(html));
        Assert.Contains("Des fiches ont changé",WebUtility.HtmlDecode(await response.Content.ReadAsStringAsync()));
        response=await admin.PostAsync(Route+"?handler=Apply",new FormUrlEncodedContent(new Dictionary<string,string>{["__RequestVerificationToken"]=AppFactory.Token(html),["Ticket"]="invalid",["Confirm"]="true"}));
        Assert.Contains("invalide",WebUtility.HtmlDecode(await response.Content.ReadAsStringAsync()));
        using(var scope=app.Services.CreateScope()) Assert.Equal(0,await scope.ServiceProvider.GetRequiredService<AppDb>().Recipes.CountAsync(x=>x.AvailablePrivately));
    }
}
