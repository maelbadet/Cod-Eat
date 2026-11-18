using System.Net;
using CodEat.Web;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace CodEat.Tests;

public class RecipeAvailabilityTests
{
    [Fact]
    public async Task ImportedDraftsExplainValidationAndBecomeAvailableWithoutPublication()
    {
        await using var app = new AppFactory();
        await app.Initialize();
        int recipeId;
        Guid revision;
        using (var scope = app.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDb>();
            await db.Recipes.ExecuteUpdateAsync(s => s.SetProperty(x => x.Status, "Brouillon"));
            var recipe = await db.Recipes.OrderBy(x => x.Id).FirstAsync();
            recipe.BookStackId = 9001;
            recipe.Ingredients = [];
            recipe.AllergensVerified = false;
            await db.SaveChangesAsync();
            recipeId = recipe.Id;
            revision = recipe.Revision;
        }
        using var client = app.Client();
        await AppFactory.Login(client, "admin@example.test");
        var empty = WebUtility.HtmlDecode(await client.GetStringAsync("/?Tab=recipes"));
        Assert.Contains("Vos recettes attendent leur validation", empty);
        Assert.Contains("2 fiche(s) en brouillon", empty);
        Assert.Contains("StatusFilter=draft", empty);
        Assert.DoesNotContain("Aucune recette compatible", empty);
        var admin = await client.GetStringAsync($"/Admin/Recipes?id={recipeId}");
        var result = await client.PostAsync("/Admin/Recipes", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["__RequestVerificationToken"] = AppFactory.Token(admin),
            ["action"] = "validate",
            ["Input.Id"] = recipeId.ToString(),
            ["Input.Revision"] = revision.ToString(),
            ["Input.Title"] = "Titre source conservé",
            ["Input.Steps"] = "Étapes source conservées",
            ["Input.Servings"] = "4",
            ["Input.Category"] = "Mer",
            ["Input.Allergens"] = "poisson",
            ["Input.AllergensVerified"] = "true",
            ["IngredientsText"] = "cabillaud|600|g|Poissonnerie|sans peau"
        }));
        Assert.Equal(HttpStatusCode.Redirect, result.StatusCode);
        using (var scope = app.Services.CreateScope())
        {
            var recipe = await scope.ServiceProvider.GetRequiredService<AppDb>().Recipes.SingleAsync(x => x.Id == recipeId);
            Assert.Equal("Validée", recipe.Status);
            Assert.Contains("Cabillaud", recipe.Title);
        }
        var available = WebUtility.HtmlDecode(await client.GetStringAsync("/?Tab=recipes"));
        Assert.Contains("Cabillaud citron", available);
        Assert.DoesNotContain("Vos recettes attendent leur validation", available);
    }

    [Fact]
    public async Task FiltersHaveTheirOwnEmptyState()
    {
        await using var app = new AppFactory();
        await app.Initialize();
        using var client = app.Client();
        await AppFactory.Login(client, "member@example.test");
        var html = WebUtility.HtmlDecode(await client.GetStringAsync("/?Tab=recipes&MaxKcal=1"));
        Assert.Contains("Aucun résultat pour ces filtres", html);
        Assert.Contains("Aucune recette ne correspond à ces filtres", html);
        Assert.DoesNotContain("Vos recettes attendent leur validation", html);
    }

    [Fact]
    public async Task DraftFilterInAdministrationShowsOnlyDrafts()
    {
        await using var app = new AppFactory();
        await app.Initialize();
        using (var scope = app.Services.CreateScope())
            await scope.ServiceProvider.GetRequiredService<AppDb>().Recipes
                .Where(x => x.Category == "Mer").ExecuteUpdateAsync(s => s.SetProperty(x => x.Status, "Brouillon"));
        using var client = app.Client();
        await AppFactory.Login(client, "admin@example.test");
        var html = WebUtility.HtmlDecode(await client.GetStringAsync("/Admin/Recipes?StatusFilter=draft"));
        Assert.Contains("Cabillaud citron", html);
        Assert.DoesNotContain("Lentilles aux carottes", html);
    }
}
