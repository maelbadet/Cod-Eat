namespace CodEat.Web;

public static class RecipeQuantities
{
    public static List<Ingredient> For(Recipe recipe,decimal portions) => recipe.Ingredients
        .Select(x=>x with {Quantity=recipe.Servings>0 && portions>0 ? x.Quantity*portions/recipe.Servings : null}).ToList();
    public static string Quantity(Ingredient ingredient)=>ingredient.Quantity.HasValue
        ? $"{ingredient.Quantity:0.###} {ingredient.Unit}".Trim() : "À préciser";
    public static string ScalingNote(Recipe recipe,decimal portions)=>recipe.Servings!=portions
        ? $"Les ingrédients ci-dessus sont adaptés à {portions:0.##} portions. Le texte des étapes reste celui de la source pour {recipe.Servings:0.##} portions ; utilisez les quantités du tableau."
        : "Les quantités correspondent aux portions du lot.";
}
