using System.Globalization;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using CodEat.Web;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace CodEat.Tests;

public class RecipePageParserTests
{
    [Theory]
    [InlineData("# Accueil\n[France](https://bookstack.test/france) · [Italie](https://bookstack.test/italie)")]
    [InlineData("# France\n## Recettes\n1. [Poêlée provençale](https://bookstack.test/poelee)\n2. [Cuire le riz](https://bookstack.test/riz)")]
    [InlineData("# Conservation et transport\nRéfrigérer rapidement et conserver au froid.")]
    [InlineData("# Index ingrédients\n| Ingrédient | Quantité pour 4 |\n|---|---|\n| riz | 300 g |\n## Préparation\n1. [Cuire le riz](https://bookstack.test/riz)")]
    [InlineData("# Index ingrédients\n| Ingrédient | Quantité pour 4 |\n|---|---|\n| riz | 300 g |\n## Préparation\n1. Consulter les recettes de France.\n2. Retour à l’accueil.")]
    public void NavigationAndDocumentationAreNotRecognizedAsRecipes(string source)
    {
        var parsed=RecipePageParser.Parse(source);
        Assert.False(parsed.Recognized);
        var recipe=new Recipe{BookStackId=123,Title="Page source"};
        RecipeImportMerge.Apply(recipe,parsed,true);
        Assert.Equal("unrecognized",RecipeImportState.Read(recipe.ImportStateJson).SourceKind);
        Assert.Contains(RecipeAvailability.PrivatePlanningProblems(recipe),x=>x.Contains("Page de liaison"));
    }

    static string Source => File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures", "poelee-provencale.md"));
    static Recipe Clone(Recipe recipe) => JsonSerializer.Deserialize<Recipe>(JsonSerializer.Serialize(recipe))!;

    [Fact]
    public void ParsesTheProvidedRecipeWithoutInventingMissingValues()
    {
        var result = RecipePageParser.Parse(Source);
        Assert.True(result.Recognized);
        Assert.Equal(4m, result.Servings);
        Assert.Equal(11, result.Ingredients.Count);
        Assert.Equal(600m, result.Ingredients.Single(x => x.Canonical == "filet de poulet").Quantity);
        Assert.Equal("cru, net sans os", result.Ingredients[0].Comment);
        Assert.Equal("Boucherie", result.Ingredients[0].Aisle);
        var water = result.Ingredients.Single(x => x.Canonical.StartsWith("eau du plat"));
        Assert.Equal(120m, water.Quantity);
        Assert.Equal("ml", water.Unit);
        Assert.DoesNotContain(result.Ingredients, x => x.Canonical is "moutarde" or "sel" or "piment");
        Assert.Equal("Terre", result.Category);
        Assert.Contains("France", result.Tags);
        Assert.Equal(20, result.ActiveMinutes);
        Assert.Equal(45, result.TotalMinutes);
        Assert.Null(result.CookMinutes);
        Assert.Equal(610, result.Kcal);
        Assert.Contains("estimée", result.KcalSource);
        Assert.Equal(48, result.FridgeHours);
        Assert.True(result.Freezable);
        Assert.Null(result.FreezerDays);
        Assert.Equal("", result.Thaw);
        Assert.Equal("", result.Allergens);
        Assert.Equal("", result.Equipment);
        Assert.StartsWith("réchauffer seulement", result.Reheat);
        Assert.Contains("8. Goûter", result.Steps);
        Assert.DoesNotContain("Budget et substitutions", result.Steps);
        Assert.DoesNotContain("Allergènes identifiés", result.Steps);
        Assert.DoesNotContain("bookstack.mael-badet.fr", result.Steps);
        Assert.Contains(result.ReviewNotes, x => x.Contains("Étiquettes à vérifier") && x.Contains("coulis"));
        Assert.Contains(result.ReviewNotes, x => x.Contains("aucun identifié"));
        Assert.Contains(result.ReviewNotes, x => x.Contains("pas été testée"));
        Assert.Contains(result.ReviewNotes, x => x.Contains("hors décongélation"));
    }

    [Theory]
    [InlineData("1,5 kg", "1.5", "kg")]
    [InlineData("1.5 kg", "1.5", "kg")]
    [InlineData("½ pièce", "0.5", "pièce")]
    [InlineData("1/2 L", "0.5", "l")]
    [InlineData("2 c. à soupe", "2", "c. à soupe")]
    [InlineData("300 grammes", "300", "g")]
    public void KeepsExplicitUnitsAndDecimalOrFractionQuantities(string source, string number, string unit)
    {
        var parsed = RecipePageParser.Quantity(source);
        Assert.Equal(decimal.Parse(number, CultureInfo.InvariantCulture), parsed.Quantity);
        Assert.Equal(unit, parsed.Unit);
    }

    [Theory]
    [InlineData("selon le goût")]
    [InlineData("2 à 3 g")]
    [InlineData("une pincée")]
    [InlineData("-20 g")]
    [InlineData("1/0 kg")]
    public void AmbiguousQuantitiesRemainUnknown(string source)
        => Assert.Null(RecipePageParser.Quantity(source).Quantity);

    [Fact]
    public void HtmlTablesAndNumberedStepsRemainStructuredAndInert()
    {
        const string html = """
            <script>do-not-import-this-script()</script><style>.bad{color:red}</style>
            <h1>Poêlée</h1><p><strong>Inspiration : France · Terre · Chaud</strong></p>
            <p>Adaptation pour <strong>4 portions individuelles</strong>.</p>
            <table><thead><tr><th>Repère</th><th>Valeur</th></tr></thead><tbody>
            <tr><td>Temps actif indicatif</td><td>20 min</td></tr>
            <tr><td>Temps total indicatif</td><td>45 min, hors refroidissement</td></tr>
            <tr><td>Énergie estimée</td><td>~610 kcal par portion</td></tr></tbody></table>
            <h2>Ingrédients</h2><table><tr><th>Ingrédient</th><th>Quantité pour 4</th><th>État au pesage</th></tr>
            <tr><td>filet de poulet</td><td>600 g</td><td>cru, net sans os</td></tr>
            <tr><td>huile de colza ou d&rsquo;olive</td><td>30 g</td><td>prêt à utiliser</td></tr></table>
            <h2>Préparation</h2><ol><li>Couper les légumes.</li><li>Cuire à la poêle.</li></ol>
            <h2>Allergènes et énergie</h2><ul><li>Allergènes identifiés dans les ingrédients : <b>aucun identifié</b>.</li>
            <li>Étiquettes à vérifier : huile.</li></ul>
            <a href="https://private-bookstack.test/page/secret">Accueil</a>
            """;
        var text = BookStackContent.FromHtml(html);
        var result = RecipePageParser.Parse(text);
        Assert.True(result.Recognized);
        Assert.Equal(2, result.Ingredients.Count);
        Assert.Equal(600m, result.Ingredients[0].Quantity);
        Assert.Equal("huile de colza ou d’olive", result.Ingredients[1].Canonical);
        Assert.Equal(4m, result.Servings);
        Assert.Equal(610, result.Kcal);
        Assert.Contains("2. Cuire", result.Steps);
        Assert.DoesNotContain("do-not-import", text);
        Assert.DoesNotContain("private-bookstack.test", text);
    }

    [Fact]
    public void ExplicitLocalCorrectionsAndClearingsSurviveSourceUpdates()
    {
        var recipe = new Recipe { BookStackId = 70 };
        RecipeImportMerge.Apply(recipe, RecipePageParser.Parse(Source), true);
        var original = Clone(recipe);
        recipe.Ingredients = recipe.Ingredients.Select(x => x.Canonical == "filet de poulet" ? x with { Quantity = 550, Aisle = "Mon rayon" } : x).ToList();
        recipe.Servings = 2;
        recipe.Description = "Ma correction locale";
        recipe.TotalMinutes = null;
        recipe.ImportStateJson = RecipeImportMerge.RememberLocalEdits(original, recipe);
        var updated = RecipePageParser.Parse(Source.Replace("600 g", "800 g").Replace("20 min |", "25 min |").Replace("~610", "~700"));
        RecipeImportMerge.Apply(recipe, updated, false);
        RecipeImportMerge.Apply(recipe, updated, false);
        Assert.Equal(550m, recipe.Ingredients[0].Quantity);
        Assert.Equal("Mon rayon", recipe.Ingredients[0].Aisle);
        Assert.Equal(2m, recipe.Servings);
        Assert.Equal("Ma correction locale", recipe.Description);
        Assert.Null(recipe.TotalMinutes);
        Assert.Equal(25, recipe.ActiveMinutes);
        Assert.Equal(700, recipe.Kcal);
        Assert.False(recipe.AllergensVerified);
        Assert.Equal("Brouillon", recipe.Status);
    }

    [Fact]
    public void LegacyImportsFillEmptyFieldsAndPreservePopulatedLocalValues()
    {
        var recipe = new Recipe { Id = 1, BookStackId = 70, Servings = 7, Kcal = 999, Description = "Description locale" };
        RecipeImportMerge.Apply(recipe, RecipePageParser.Parse(Source), false);
        Assert.Equal(11, recipe.Ingredients.Count);
        Assert.Equal(7m, recipe.Servings);
        Assert.Equal(999, recipe.Kcal);
        Assert.Equal("", recipe.KcalSource);
        Assert.Equal("Description locale", recipe.Description);
        Assert.Equal(20, recipe.ActiveMinutes);
        Assert.Contains(recipe.ImportReviewNotes, x => x.Contains("Portions") && x.Contains("locale conservée"));
    }

    [Fact]
    public void ConflictingPortionsAndQualitativeRowsAreFlaggedForReview()
    {
        var source = Source.Replace("Quantité pour 4", "Quantité pour 6").Replace("| ail | 8 g |", "| ail | selon le goût |");
        var result = RecipePageParser.Parse(source);
        Assert.Null(result.Servings);
        Assert.Null(result.Ingredients.Single(x => x.Canonical == "ail").Quantity);
        Assert.Contains("selon le goût", result.Ingredients.Single(x => x.Canonical == "ail").Comment);
        Assert.Contains(result.ReviewNotes, x => x.Contains("contradictoires"));
        Assert.False(RecipePageParser.Parse("# Méthode et limites\nUne page de documentation.").Recognized);
    }

    [Fact]
    public void ExtractedQuantitiesScaleAndUnknownAllergensStayExcluded()
    {
        var recipe = new Recipe();
        RecipeImportMerge.Apply(recipe, RecipePageParser.Parse(Source), true);
        Assert.False(new RecipeFilter(Allergens: "lait").Matches(recipe));
        Assert.False(new RecipeFilter(Equipment: "poêle,four").Matches(recipe));
        var preparation = new Preparation { Recipe = recipe, Portions = 2 };
        var data = new WeekData { Preparations = [preparation], Meals = [new(0, "Midi", preparation.Id, 2)] };
        Kitchen.Regenerate(data);
        Assert.Equal(300m, data.Shopping.Single(x => x.Name == "filet de poulet").Calculated);
        Assert.Equal(60m, data.Shopping.Single(x => x.Name.StartsWith("eau du plat")).Calculated);
        preparation.Portions = 4;
        Kitchen.Assign(data, new(1, "Soir", preparation.Id, 2));
        Kitchen.Regenerate(data);
        Assert.Equal(600m, data.Shopping.Single(x => x.Name == "filet de poulet").Calculated);
    }

    [Fact]
    public void ALocallyChangedKcalValueDoesNotKeepTheImportedAttribution()
    {
        var recipe = new Recipe();
        RecipeImportMerge.Apply(recipe, RecipePageParser.Parse(Source), true);
        var original = Clone(recipe);
        recipe.Kcal = 700;
        recipe.ImportStateJson = RecipeImportMerge.RememberLocalEdits(original, recipe);
        Assert.Equal("", recipe.KcalSource);
        Assert.Contains("provenance des kcal", recipe.MissingFields());
        RecipeImportMerge.Apply(recipe, RecipePageParser.Parse(Source.Replace("~610", "~800")), false);
        Assert.Equal(700, recipe.Kcal);
        Assert.Equal("", recipe.KcalSource);
    }

    sealed class SourceHandler(Func<string> source, bool html = false) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            var path = request.RequestUri!.AbsolutePath;
            var json = path.EndsWith("books/7") ? "{\"id\":7}"
                : path.EndsWith("pages") ? "{\"total\":1,\"data\":[{\"id\":70}]}"
                : html ? JsonSerializer.Serialize(new { id = 70, book_id = 7, name = "Poêlée provençale", html = source() })
                : JsonSerializer.Serialize(new { id = 70, book_id = 7, name = "Poêlée provençale", markdown = source() });
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(json, Encoding.UTF8, "application/json") });
        }
    }

    [Fact]
    public async Task ResyncBackfillsAnUnchangedExistingPageAndThenIsIdempotent()
    {
        await using var app = new AppFactory();
        await app.Initialize();
        using var scope = app.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDb>();
        var source = Source;
        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes("Poêlée provençale\n" + source)));
        db.Recipes.Add(new Recipe { BookStackId = 70, Title = "Poêlée provençale", Steps = source, SourceHash = hash });
        await db.SaveChangesAsync();
        using var http = new HttpClient(new SourceHandler(() => source));
        var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        { ["BookStack:BaseUrl"] = "https://bookstack.test", ["BookStack:BookId"] = "7", ["BookStack:TokenId"] = "test-id", ["BookStack:TokenSecret"] = "test-secret" }).Build();
        var sync = new BookStackSync(db, new BookStackClient(http, config));
        Assert.Contains("1 préremplie(s)", await sync.Run(default));
        var recipe = await db.Recipes.SingleAsync(x => x.BookStackId == 70);
        Assert.Equal(11, recipe.Ingredients.Count);
        Assert.Equal(610, recipe.Kcal);
        Assert.Equal("Brouillon", recipe.Status);
        Assert.False(recipe.AllergensVerified);
        Assert.DoesNotContain("Budget et substitutions", recipe.Steps);
        var revision = recipe.Revision;
        Assert.Contains("1 inchangée(s)", await sync.Run(default));
        Assert.Equal(revision, recipe.Revision);
        Assert.Single(await db.Recipes.Where(x => x.BookStackId == 70).ToListAsync());
        recipe.Ingredients = recipe.Ingredients.Select(x => x.Canonical == "filet de poulet" ? x with { Quantity = 550 } : x).ToList();
        recipe.Status = "Publiée";
        recipe.AllergensVerified = true;
        await db.SaveChangesAsync();
        source = source.Replace("600 g", "800 g").Replace("~610", "~700");
        await sync.Run(default);
        Assert.Equal(550m, recipe.Ingredients[0].Quantity);
        Assert.Equal(700, recipe.Kcal);
        Assert.Equal("Brouillon", recipe.Status);
        Assert.False(recipe.AllergensVerified);
    }

    [Fact]
    public async Task AdminFormKeepsImportMetadataAndLocalCorrectionsWhenValidating()
    {
        await using var app = new AppFactory();
        await app.Initialize();
        using var scope = app.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDb>();
        var recipe = new Recipe { BookStackId = 70, Title = "Poêlée provençale" };
        var parsed = RecipePageParser.Parse(Source);
        RecipeImportMerge.Apply(recipe, parsed, true);
        recipe.Steps = parsed.Steps;
        recipe.SourceHash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes("Poêlée provençale\n" + Source)));
        db.Recipes.Add(recipe);
        await db.SaveChangesAsync();
        using var client = app.Client();
        await AppFactory.Login(client, "admin@example.test");
        var html = await client.GetStringAsync($"/Admin/Recipes?id={recipe.Id}");
        Assert.Contains("Données importées", WebUtility.HtmlDecode(html));
        Assert.Contains("value=\"610\"", html);
        var response = await client.PostAsync("/Admin/Recipes", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["__RequestVerificationToken"] = AppFactory.Token(html), ["action"] = "validate",
            ["Input.Id"] = recipe.Id.ToString(), ["Input.Revision"] = recipe.Revision.ToString(),
            ["Input.Title"] = recipe.Title, ["Input.Steps"] = recipe.Steps, ["Input.Description"] = "Description corrigée localement",
            ["Input.Category"] = recipe.Category, ["Input.Tags"] = recipe.Tags, ["Input.Servings"] = "4",
            ["Input.ActiveMinutes"] = "20", ["Input.TotalMinutes"] = "45", ["Input.Kcal"] = "610", ["Input.KcalSource"] = recipe.KcalSource,
            ["Input.AllergensVerified"] = "true", ["Input.Freezable"] = "true", ["Input.FridgeHours"] = "48", ["Input.Reheat"] = recipe.Reheat,
            ["Input.Equipment"] = "poêle,thermomètre",
            ["IngredientsText"] = string.Join('\n', recipe.Ingredients.Select(x => $"{x.Canonical}|{x.Quantity?.ToString(CultureInfo.InvariantCulture)}|{x.Unit}|{x.Aisle}|{x.Comment}"))
        }));
        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        await db.Entry(recipe).ReloadAsync();
        Assert.Equal("Validée", recipe.Status);
        Assert.True(recipe.AllergensVerified);
        Assert.Equal(RecipePageParser.Version, RecipeImportState.Read(recipe.ImportStateJson).Version);
        Assert.Contains(nameof(Recipe.Description), RecipeImportState.Read(recipe.ImportStateJson).ManualFields);
        Assert.Equal(11, recipe.Ingredients.Count);
        var changed = RecipePageParser.Parse(Source.Replace("~610", "~700"));
        RecipeImportMerge.Apply(recipe, changed, false);
        Assert.Equal("Description corrigée localement", recipe.Description);
        Assert.Equal("poêle,thermomètre", recipe.Equipment);
        Assert.Equal(700, recipe.Kcal);
    }

    [Fact]
    public async Task TheApiHtmlFallbackKeepsIngredientTablesParseable()
    {
        const string html = "<p>Pour 4 portions individuelles.</p><h2>Ingrédients</h2><table><tr><th>Ingrédient</th><th>Quantité pour 4</th></tr><tr><td>filet de poulet</td><td>600 g</td></tr></table><h2>Préparation</h2><ol><li>Cuire les ingrédients.</li></ol>";
        using var http = new HttpClient(new SourceHandler(() => html, true));
        var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        { ["BookStack:BaseUrl"] = "https://bookstack.test", ["BookStack:BookId"] = "7", ["BookStack:TokenId"] = "test-id", ["BookStack:TokenSecret"] = "test-secret" }).Build();
        var pages = await new BookStackClient(http, config).Read(default);
        var parsed = RecipePageParser.Parse(pages.Single().Text);
        Assert.True(parsed.Recognized);
        Assert.Equal(4m, parsed.Servings);
        Assert.Equal(600m, parsed.Ingredients.Single().Quantity);
        Assert.Contains("1. Cuire", parsed.Steps);
    }

    [Fact]
    public async Task IncompleteImportedDataCanBeSavedAsDraftButCannotBeValidated()
    {
        await using var app = new AppFactory();
        await app.Initialize();
        using var client = app.Client();
        await AppFactory.Login(client, "admin@example.test");
        var html = await client.GetStringAsync("/Admin/Recipes");
        var form = new Dictionary<string, string>
        {
            ["__RequestVerificationToken"] = AppFactory.Token(html), ["action"] = "draft",
            ["Input.Title"] = "Fiche incomplète", ["Input.Servings"] = "0", ["Input.Category"] = "", ["IngredientsText"] = ""
        };
        Assert.Equal(HttpStatusCode.Redirect, (await client.PostAsync("/Admin/Recipes", new FormUrlEncodedContent(form))).StatusCode);
        using var scope = app.Services.CreateScope();
        var recipe = await scope.ServiceProvider.GetRequiredService<AppDb>().Recipes.SingleAsync(x => x.Title == "Fiche incomplète");
        Assert.Equal("Brouillon", recipe.Status);
        Assert.Equal(0m, recipe.Servings);
        Assert.Contains("portions", recipe.MissingFields());
        Assert.Contains("catégorie", recipe.MissingFields());
        form["action"] = "validate";
        var invalid = await client.PostAsync("/Admin/Recipes", new FormUrlEncodedContent(form));
        Assert.Equal(HttpStatusCode.OK, invalid.StatusCode);
        Assert.Contains("À renseigner", WebUtility.HtmlDecode(await invalid.Content.ReadAsStringAsync()));
    }
}
