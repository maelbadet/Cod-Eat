using System.Globalization;
using System.Text.Json;

namespace CodEat.Web;

public sealed class RecipeImportState
{
    public int Version { get; set; }
    public string SourceKind { get; set; } = "unknown";
    public Dictionary<string, string> AppliedValues { get; set; } = [];
    public HashSet<string> ManualFields { get; set; } = [];
    public List<string> ReviewNotes { get; set; } = [];

    public static RecipeImportState Read(string json)
    {
        try { return JsonSerializer.Deserialize<RecipeImportState>(json) ?? new(); }
        catch (JsonException) { return new(); }
    }
}

public static class RecipeImportMerge
{
    static readonly Dictionary<string, string> Labels = new()
    {
        [nameof(Recipe.Ingredients)] = "Ingrédients", [nameof(Recipe.Servings)] = "Portions",
        [nameof(Recipe.Description)] = "Description", [nameof(Recipe.Category)] = "Catégorie", [nameof(Recipe.Tags)] = "Tags",
        [nameof(Recipe.ActiveMinutes)] = "Temps actif", [nameof(Recipe.CookMinutes)] = "Temps de cuisson",
        [nameof(Recipe.TotalMinutes)] = "Temps total", [nameof(Recipe.Kcal)] = "Kcal", [nameof(Recipe.KcalSource)] = "Provenance des kcal",
        [nameof(Recipe.Allergens)] = "Allergènes", [nameof(Recipe.Equipment)] = "Ustensiles",
        [nameof(Recipe.FridgeHours)] = "Conservation au réfrigérateur", [nameof(Recipe.Freezable)] = "Possibilité de congélation",
        [nameof(Recipe.FreezerDays)] = "Durée de congélation", [nameof(Recipe.Thaw)] = "Décongélation", [nameof(Recipe.Reheat)] = "Réchauffage"
    };

    static string Snapshot(object? value) => value switch
    {
        null => "null",
        decimal number => number.ToString("0.############################", CultureInfo.InvariantCulture),
        bool boolean => boolean ? "true" : "false",
        IEnumerable<Ingredient> ingredients => JsonSerializer.Serialize(ingredients.Select(x => new
        { x.Canonical, Quantity = x.Quantity?.ToString("0.############################", CultureInfo.InvariantCulture), x.Unit, x.Aisle, x.Comment })),
        IFormattable formatted => formatted.ToString(null, CultureInfo.InvariantCulture),
        _ => value.ToString() ?? ""
    };

    static Dictionary<string, object?> Values(ParsedRecipe parsed) => new()
    {
        [nameof(Recipe.Ingredients)] = parsed.Ingredients,
        [nameof(Recipe.Servings)] = parsed.Servings ?? 0,
        [nameof(Recipe.Description)] = parsed.Description,
        [nameof(Recipe.Category)] = parsed.Category,
        [nameof(Recipe.Tags)] = parsed.Tags,
        [nameof(Recipe.ActiveMinutes)] = parsed.ActiveMinutes, [nameof(Recipe.CookMinutes)] = parsed.CookMinutes,
        [nameof(Recipe.TotalMinutes)] = parsed.TotalMinutes, [nameof(Recipe.Kcal)] = parsed.Kcal,
        [nameof(Recipe.KcalSource)] = parsed.KcalSource, [nameof(Recipe.Allergens)] = parsed.Allergens,
        [nameof(Recipe.Equipment)] = parsed.Equipment, [nameof(Recipe.FridgeHours)] = parsed.FridgeHours,
        [nameof(Recipe.Freezable)] = parsed.Freezable ?? false, [nameof(Recipe.FreezerDays)] = parsed.FreezerDays,
        [nameof(Recipe.Thaw)] = parsed.Thaw, [nameof(Recipe.Reheat)] = parsed.Reheat
    };

    static bool Empty(object? value) => value == null || value is string text && string.IsNullOrWhiteSpace(text)
        || value is decimal number && number == 0 || value is IEnumerable<Ingredient> ingredients && !ingredients.Any();

    public static bool Apply(Recipe recipe, ParsedRecipe parsed, bool isNew)
    {
        var state = RecipeImportState.Read(recipe.ImportStateJson);
        state.ReviewNotes = [.. parsed.ReviewNotes];
        var changed = false;
        foreach (var (field, proposed) in Values(parsed))
        {
            var property = typeof(Recipe).GetProperty(field)!;
            var current = property.GetValue(recipe);
            // A source attribution must not be attached to a different locally entered value.
            if (field == nameof(Recipe.KcalSource) && state.ManualFields.Contains(nameof(Recipe.Kcal)))
            {
                if (state.AppliedValues.TryGetValue(field, out var autoSource) && Snapshot(current) == autoSource)
                {
                    property.SetValue(recipe, "");
                    current = "";
                    state.AppliedValues.Remove(field);
                    changed = true;
                }
                state.ManualFields.Add(field);
            }
            var currentSnapshot = Snapshot(current);
            var proposedSnapshot = Snapshot(proposed);
            if (state.AppliedValues.TryGetValue(field, out var applied) && currentSnapshot != applied)
            {
                state.ManualFields.Add(field);
                state.AppliedValues.Remove(field);
            }
            var canApply = !state.ManualFields.Contains(field)
                && (isNew || state.AppliedValues.ContainsKey(field) || Empty(current));
            if (canApply)
            {
                if (currentSnapshot != proposedSnapshot)
                {
                    property.SetValue(recipe, proposed);
                    changed = true;
                }
                state.AppliedValues[field] = proposedSnapshot;
            }
            else
            {
                // Legacy imports have no baseline: preserve every populated local value.
                state.ManualFields.Add(field);
                if (parsed.Recognized && currentSnapshot != proposedSnapshot && !Empty(proposed))
                {
                    var comparable = current is string a && proposed is string b && a.Length + b.Length <= 120
                        || current is decimal or int or bool || proposed is decimal or int or bool;
                    var comparison = comparable ? $" Valeur locale : {Display(current)} ; source : {Display(proposed)}." : " Comparez-la à la source BookStack avant validation.";
                    state.ReviewNotes.Add($"{Labels[field]} : valeur locale conservée." + comparison);
                }
            }
        }
        state.Version = RecipePageParser.Version;
        state.SourceKind = parsed.Recognized ? "recipe" : "unrecognized";
        state.ReviewNotes = state.ReviewNotes.Distinct().ToList();
        recipe.ImportStateJson = JsonSerializer.Serialize(state);
        return changed;
    }

    // Even an explicit local clearing of a field must survive future imports.
    public static string RememberLocalEdits(Recipe original, Recipe edited)
    {
        var state = RecipeImportState.Read(original.ImportStateJson);
        if (original.Kcal != edited.Kcal && original.KcalSource == edited.KcalSource
            && state.AppliedValues.ContainsKey(nameof(Recipe.KcalSource)))
        {
            edited.KcalSource = "";
            state.ReviewNotes.Add("Provenance des kcal à renseigner après la correction locale de leur valeur.");
        }
        foreach (var field in Labels.Keys)
        {
            var property = typeof(Recipe).GetProperty(field)!;
            if (Snapshot(property.GetValue(original)) == Snapshot(property.GetValue(edited))) continue;
            state.ManualFields.Add(field);
            state.AppliedValues.Remove(field);
        }
        return JsonSerializer.Serialize(state);
    }

    static string Display(object? value) => value switch
    {
        null => "vide", bool flag => flag ? "oui" : "non",
        IFormattable number => number.ToString(null, CultureInfo.GetCultureInfo("fr-FR")),
        _ => string.IsNullOrWhiteSpace(value.ToString()) ? "vide" : value.ToString()!
    };
}
