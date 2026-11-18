using System.Net;
using System.Text.RegularExpressions;
using CodEat.Web;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace CodEat.Tests;

public class RecipeBrowserTests
{
    [Fact]
    public async Task SearchFacetsPagingAndDedicatedRecipeKeepStepsOutOfCards()
    {
        await using var app=new AppFactory();await app.Initialize();
        using(var scope=app.Services.CreateScope())
        {
            var db=scope.ServiceProvider.GetRequiredService<AppDb>();
            db.Recipes.AddRange(Enumerable.Range(1,55).Select(n=>new Recipe{Title=$"Poulet {n:00}",Status="Validée",Kcal=n==55?null:500,
                Ingredients=[new("filet de poulet",600,"g","Boucherie")],Steps="Couper puis cuire.",AllergensVerified=true}));
            await db.SaveChangesAsync();
        }
        using var client=app.Client();await AppFactory.Login(client,"member@example.test");
        var html=WebUtility.HtmlDecode(await client.GetStringAsync("/?Tab=recipes"));
        Assert.Equal(12,Regex.Matches(html,"<article class=\"recipe-card\">").Count);
        Assert.Contains("LES PLUS AJOUTÉS À VOTRE SEMAINE",html);Assert.Contains("2 repas",html);
        Assert.Contains("Poulet (55)",html);Assert.DoesNotContain("1. Laver et couper",html);
        html=await client.GetStringAsync("/?Tab=recipes&Protein=Poulet&MinKcal=450&MaxKcal=550&PageSize=48");
        Assert.Equal(48,Regex.Matches(html,"<article class=\"recipe-card\">").Count);
        Assert.Contains("MinKcal=450",WebUtility.HtmlDecode(html));
        var partial=WebUtility.HtmlDecode(await client.GetStringAsync("/?handler=RecipeResults&Protein=Poulet&MinKcal=450&MaxKcal=550&PageSize=48&CatalogPage=2"));
        Assert.Equal(6,Regex.Matches(partial,"<article class=\"recipe-card\">").Count);Assert.DoesNotContain("<!DOCTYPE",partial);
        Assert.DoesNotContain("Poulet 55",partial);
        html=await client.GetStringAsync("/?Tab=recipes&PageSize=900");
        Assert.Equal(12,Regex.Matches(html,"<article class=\"recipe-card\">").Count);
        int recipeId;using(var scope=app.Services.CreateScope())recipeId=await scope.ServiceProvider.GetRequiredService<AppDb>().Recipes.Where(x=>x.Category=="Mer").Select(x=>x.Id).SingleAsync();
        var detail=WebUtility.HtmlDecode(await client.GetStringAsync($"/recettes/{recipeId}?ReturnUrl=https://evil.example"));
        Assert.Contains("1. Laver et couper",detail);Assert.Contains("href=\"/?Tab=recipes\"",detail);
        using var anonymous=app.Client();Assert.Equal(HttpStatusCode.Redirect,(await anonymous.GetAsync($"/recettes/{recipeId}")).StatusCode);
    }

    [Theory]
    [InlineData(false,false)]
    [InlineData(true,false)]
    [InlineData(true,true)]
    public async Task PlanningScalesShoppingAndReservesTomorrowWithoutDoubleCounting(bool leftovers,bool sunday)
    {
        await using var app=new AppFactory();await app.Initialize();
        int recipeId;Guid household;
        using(var scope=app.Services.CreateScope())
        {
            var db=scope.ServiceProvider.GetRequiredService<AppDb>();recipeId=await db.Recipes.Where(x=>x.Category=="Mer").Select(x=>x.Id).SingleAsync();
            household=await db.Users.Where(x=>x.Email=="member@example.test").Select(x=>x.HouseholdId).SingleAsync();
        }
        var date=Kitchen.Monday(Kitchen.Today).AddDays(14+(sunday?6:2));
        using var client=app.Client();await AppFactory.Login(client,"member@example.test");
        var html=await client.GetStringAsync($"/recettes/{recipeId}");
        Assert.Equal(HttpStatusCode.BadRequest,(await client.PostAsync($"/recettes/{recipeId}",new FormUrlEncodedContent([]))).StatusCode);
        var form=new Dictionary<string,string>{["__RequestVerificationToken"]=AppFactory.Token(html),["Id"]=recipeId.ToString(),
            ["Date"]=date.ToString("yyyy-MM-dd"),["Slot"]="Soir",["People"]="3",["Leftovers"]=leftovers.ToString()};
        Assert.Equal(HttpStatusCode.Redirect,(await client.PostAsync($"/recettes/{recipeId}",new FormUrlEncodedContent(form))).StatusCode);
        using(var scope=app.Services.CreateScope())
        {
            var db=scope.ServiceProvider.GetRequiredService<AppDb>();
            var data=(await db.Weeks.SingleAsync(x=>x.HouseholdId==household && x.Monday==Kitchen.Monday(date))).Data;
            Assert.Equal(leftovers?6:3,data.Preparations.Single().Portions);
            Assert.Equal(leftovers && !sunday?2:1,data.Meals.Count);
            Assert.Equal(leftovers?900:450,data.Shopping.Single(x=>x.Name=="cabillaud").Calculated);
            Assert.False(await db.Weeks.AnyAsync(x=>x.HouseholdId!=household && x.Monday==Kitchen.Monday(date)));
            if(leftovers && sunday)
            {
                var next=(await db.Weeks.SingleAsync(x=>x.HouseholdId==household && x.Monday==Kitchen.Monday(date.AddDays(1)))).Data;
                Assert.Single(next.Meals);Assert.Equal(3,next.Meals.Single().Portions);Assert.True(next.Preparations.Single().Carryover);
                Assert.Empty(next.Shopping);Assert.Equal(data.Preparations.Single().Id,next.Preparations.Single().Id);
                Assert.Throws<InvalidOperationException>(()=>Kitchen.Assign(next,new(1,"Soir",next.Preparations.Single().Id,1)));
                // Removing Sunday's served meal must not remove the shopping for Monday's reserved food.
                data.Meals.Clear();Kitchen.Regenerate(data);
                Assert.Equal(900,data.Shopping.Single(x=>x.Name=="cabillaud").Calculated);
            }
        }
        // Repeating the submission must leave the existing meals intact.
        var conflict=await client.PostAsync($"/recettes/{recipeId}",new FormUrlEncodedContent(form));
        Assert.Contains("Un repas occupe déjà",WebUtility.HtmlDecode(await conflict.Content.ReadAsStringAsync()));
    }

    [Fact]
    public void ProteinFacetsUseActualIngredients()
    {
        var recipe=new Recipe{Title="Poulet ou saumon en substitution",Ingredients=[new("lentilles",300,"g","Sec")]};
        Assert.Equal(new[]{"Légumineuses"},RecipeDiscovery.ProteinKinds(recipe));
        recipe.Ingredients=[new("bœuf",300,"g","Boucherie"),new("œufs",2,"pièce","Frais")];
        Assert.Equal(new[]{"Bœuf","Œufs"},RecipeDiscovery.ProteinKinds(recipe));
    }
}
