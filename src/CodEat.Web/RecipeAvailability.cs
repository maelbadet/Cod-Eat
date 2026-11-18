using System.Linq.Expressions;
using System.Text.Json;

namespace CodEat.Web;

public static class RecipeAvailability
{
    public static Expression<Func<Recipe, bool>> Available => recipe => recipe.Status == "Validée"
        || recipe.Status == "Publiée" || recipe.Status == "Brouillon" && recipe.AvailablePrivately;

    public static string[] PrivatePlanningProblems(Recipe recipe)
    {
        var problems = new List<string>();
        if (recipe.Missing) problems.Add("Page absente du livre source");
        if (recipe.BookStackId != null)
        {
            var state = RecipeImportState.Read(recipe.ImportStateJson);
            if (state.Version < RecipePageParser.Version || state.SourceKind == "unknown")
                problems.Add("Type de page à analyser : resynchronisez BookStack");
            else if (state.SourceKind != "recipe")
                problems.Add("Page de liaison, documentation ou format de recette non reconnu : revue manuelle requise");
        }
        try { problems.AddRange(recipe.MissingFields().Where(x => x != "allergènes vérifiés")); }
        catch (JsonException) { problems.Add("Tableau d’ingrédients illisible"); }
        try
        {
            if (recipe.Ingredients.Any(x => string.IsNullOrWhiteSpace(x.Canonical) || x.Quantity < 0))
                problems.Add("Ingrédient ou quantité invalide");
        }
        catch (JsonException) { problems.Add("Tableau d’ingrédients illisible"); }
        if (recipe.ActiveMinutes < 0 || recipe.CookMinutes < 0 || recipe.TotalMinutes < 0 || recipe.Kcal < 0
            || recipe.FridgeHours < 0 || recipe.FreezerDays < 0) problems.Add("Valeur numérique négative");
        return problems.Distinct().ToArray();
    }
}
