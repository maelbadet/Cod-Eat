using System.Net;
using CodEat.Web;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using PdfSharp.Fonts;
using Xunit;

namespace CodEat.Tests;

public class PlannedRecipeTests
{
    [Fact]
    public async Task PlanningAndBatchOpenTheFullHouseholdSnapshotEvenIfCatalogChanges()
    {
        await using var app=new AppFactory();await app.Initialize();
        int weekId;Guid preparationId;
        using(var scope=app.Services.CreateScope())
        {
            var db=scope.ServiceProvider.GetRequiredService<AppDb>();
            var household=await db.Users.Where(x=>x.Email=="admin@example.test").Select(x=>x.HouseholdId).SingleAsync();
            var week=await db.Weeks.SingleAsync(x=>x.HouseholdId==household);weekId=week.Id;preparationId=week.Data.Preparations.Single().Id;
            var recipe=await db.Recipes.SingleAsync(x=>x.Category=="Mer");recipe.Steps="Nouvelle recette du catalogue";recipe.Status="Brouillon";recipe.Ingredients=[];await db.SaveChangesAsync();
        }
        using var member=app.Client();await AppFactory.Login(member,"member@example.test");
        var route=$"/preparations/{weekId}/{preparationId}";
        var weekHtml=WebUtility.HtmlDecode(await member.GetStringAsync("/"));Assert.Contains(route,weekHtml);Assert.Contains("Voir la recette complète",weekHtml);
        var batchHtml=WebUtility.HtmlDecode(await member.GetStringAsync("/?Tab=batch"));Assert.Contains(route,batchHtml);Assert.Contains("Ouvrir la recette complète",batchHtml);
        var detail=WebUtility.HtmlDecode(await member.GetStringAsync(route+"?From=batch"));
        Assert.Contains("Retour au batch cooking",detail);Assert.Contains("1. Laver et couper",detail);Assert.Contains("600 g",detail);
        Assert.DoesNotContain("Nouvelle recette du catalogue",detail);
        using var other=app.Client();await AppFactory.Login(other,"other@example.test");Assert.Equal(HttpStatusCode.NotFound,(await other.GetAsync(route)).StatusCode);
        using var anonymous=app.Client();Assert.Equal(HttpStatusCode.Redirect,(await anonymous.GetAsync(route)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound,(await member.GetAsync($"/preparations/{weekId}/{Guid.NewGuid()}")).StatusCode);
    }

    [Fact]
    public void CompleteMealPdfIncludesScaledIngredientsAllStepsAndSafePageBreaks()
    {
        GlobalFontSettings.FontResolver=new LocalFontResolver();
        var source=File.ReadAllText(Path.Combine(AppContext.BaseDirectory,"Fixtures","poelee-provencale.md"));
        var parsed=RecipePageParser.Parse(source);
        var recipe=new Recipe{Title="Poêlée provençale — recette complète",Steps=parsed.Steps};RecipeImportMerge.Apply(recipe,parsed,true);
        recipe.Ingredients=recipe.Ingredients.Append(new Ingredient("sel facultatif",null,"","Épicerie","selon le goût")).ToList();
        recipe.Steps+="\n"+string.Join('\n',Enumerable.Range(9,45).Select(i=>$"{i}. Étape détaillée {i:00} : mélanger délicatement les légumes avec la sauce et répartir dans les récipients prévus pour le service. Vérifier les portions avant de réserver."));
        var preparation=new Preparation{Recipe=recipe,Portions=6,At=new(2026,11,16,10,0,0)};
        var data=new WeekData{Preparations=[preparation],Meals=[new(0,"Midi",preparation.Id,3),new(1,"Midi",preparation.Id,3)]};
        var bytes=new PdfExport().CreateMeals(data,new(2026,11,16));
        using var document=UglyToad.PdfPig.PdfDocument.Open(bytes);
        var text=string.Join(" ",document.GetPages().Select(x=>x.Text));
        Assert.True(document.NumberOfPages>=4);Assert.Contains("filet de poulet",text);Assert.Contains("900 g",text);Assert.Contains("450 g",text);
        Assert.Contains("sel facultatif",text);Assert.Contains("À préciser",text);Assert.Contains("8. Goûter",text);Assert.Contains("Étape détaillée 53",text);
        Assert.Contains("texte des étapes reste celui de la source",text);Assert.Contains("Non vérifiés",text);
        foreach(var page in document.GetPages())Assert.All(page.Letters,letter=>{Assert.InRange(letter.BoundingBox.Left,40,553);Assert.InRange(letter.BoundingBox.Right,40,553);Assert.InRange(letter.BoundingBox.Bottom,20,820);});
        Assert.Contains("Étape détaillée 53",PdfExport.MealsText(data,new(2026,11,16)));
    }

    [Fact]
    public void MissingServingsOrQuantityNeverInventsScaledAmounts()
    {
        var recipe=new Recipe{Servings=0,Ingredients=[new("riz",300,"g","Sec")]};
        Assert.Null(RecipeQuantities.For(recipe,6).Single().Quantity);
        recipe.Servings=4;Assert.Equal(450,RecipeQuantities.For(recipe,6).Single().Quantity);
    }
}
