using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;

namespace CodEat.Web;

public static class Kitchen
{
    public static readonly TimeZoneInfo Paris = TimeZoneInfo.FindSystemTimeZoneById("Europe/Paris");
    public static DateOnly Today => DateOnly.FromDateTime(TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, Paris));
    public static DateOnly Monday(DateOnly date) => date.AddDays(-(((int)date.DayOfWeek + 6) % 7));
    public static string Normalize(string text) => Regex.Replace(text.Trim().ToLowerInvariant(), @"\s+", " ");
    public static string Slug(string text) => Regex.Replace(string.Concat(text.Normalize(NormalizationForm.FormD).Where(c => CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.NonSpacingMark)).ToLowerInvariant(), "[^a-z0-9]+", "-").Trim('-');
    public static (decimal Factor, string Unit) Unit(string unit) => Normalize(unit) switch { "kg" => (1000m, "g"), "g" => (1m, "g"), "l" => (1000m, "ml"), "cl" => (10m, "ml"), "ml" => (1m, "ml"), var u => (1m, u) };
    public static void Assign(WeekData data, Meal meal)
    {
        if (meal.Day is < 0 or > 6 || meal.Slot is not ("Midi" or "Soir") || meal.Portions <= 0) throw new InvalidOperationException("Créneau ou portions invalides.");
        var prep = data.Preparations.Single(x => x.Id == meal.PreparationId);
        var used = data.Meals.Where(x => x.PreparationId == prep.Id && !(x.Day == meal.Day && x.Slot == meal.Slot)).Sum(x => x.Portions);
        if (used + meal.Portions > prep.Portions) throw new InvalidOperationException("Le lot ne contient pas assez de portions. Augmentez les portions à cuisiner.");
        data.Meals.RemoveAll(x => x.Day == meal.Day && x.Slot == meal.Slot);
        data.Meals.Add(meal);
    }
    public static void Regenerate(WeekData data, IReadOnlyDictionary<string, string>? aliases = null)
    {
        var generated = new Dictionary<string, ShoppingLine>();
        foreach (var p in data.Preparations.Where(p => data.Meals.Any(m => m.PreparationId == p.Id)))
        foreach (var i in p.Recipe.Ingredients)
        {
            var canonical = Normalize(i.Canonical);
            if (aliases?.TryGetValue(canonical, out var mapped) == true) canonical = Normalize(mapped);
            var (factor, unit) = Unit(i.Unit);
            var key = $"{canonical}|{Normalize(i.Aisle)}|{unit}";
            if (!generated.TryGetValue(key, out var line)) generated[key] = line = new() { Key = key, Name = canonical, Aisle = i.Aisle, Unit = unit };
            if (i.Quantity is null) { line.Note = "Quantité à vérifier : " + i.Comment; }
            else line.Calculated = (line.Calculated ?? 0) + i.Quantity.Value * factor * p.Portions / p.Recipe.Servings;
        }
        foreach (var line in generated.Values)
        {
            var old = data.Shopping.FirstOrDefault(x => !x.Manual && x.Key == line.Key);
            if (old == null) continue;
            line.Adjusted = old.Adjusted; line.Checked = old.Checked; line.InStock = old.InStock;
            line.Changed = old.Changed || (old.Calculated != line.Calculated && (old.Adjusted.HasValue || old.Checked || old.InStock));
        }
        data.Shopping = generated.Values.Concat(data.Shopping.Where(x => x.Manual)).OrderBy(x => x.Aisle).ThenBy(x => x.Name).ToList();
    }
    public static string[] Warnings(Preparation p, WeekData data, DateOnly monday)
    {
        var warnings = new List<string>();
        if (p.Frozen)
        {
            if (!p.Recipe.Freezable) warnings.Add("Congélation non validée pour cette recette.");
            if (p.Recipe.FreezerDays == null || string.IsNullOrWhiteSpace(p.Recipe.Thaw) || string.IsNullOrWhiteSpace(p.Recipe.Reheat)) warnings.Add("Congélation : durée ou consignes à renseigner.");
        }
        else if (p.Recipe.FridgeHours == null) warnings.Add("Conservation à renseigner.");
        foreach (var meal in data.Meals.Where(x => x.PreparationId == p.Id))
        {
            var consumption = monday.AddDays(meal.Day).ToDateTime(new TimeOnly(meal.Slot == "Midi" ? 12 : 19, 30));
            var elapsed = (TimeZoneInfo.ConvertTimeToUtc(consumption, Paris) - TimeZoneInfo.ConvertTimeToUtc(DateTime.SpecifyKind(p.At, DateTimeKind.Unspecified), Paris)).TotalHours;
            if (elapsed < 0) warnings.Add("Préparation prévue après le repas.");
            if (!p.Frozen && p.Recipe.FridgeHours is int h && elapsed > h) warnings.Add($"Conservation dépassée pour {monday.AddDays(meal.Day):dd/MM} {meal.Slot} : cuisiner plus tard" + (p.Recipe.Freezable ? " ou prévoir une congélation." : "."));
            if (p.Frozen && p.Recipe.FreezerDays is int days && elapsed > days * 24) warnings.Add("Durée de congélation dépassée.");
        }
        return warnings.Distinct().ToArray();
    }
    public static (string Period, decimal Quantity) Due(ReminderRule rule, DateOnly date)
    {
        var months = date.Year * 12 + date.Month - 1;
        var bucket = months / Math.Max(1, rule.EveryMonths);
        var qty = date.Month is 12 or 1 or 2 ? rule.WinterQuantity ?? rule.Quantity : date.Month is 9 or 10 or 11 ? rule.AutumnQuantity ?? rule.Quantity : rule.Quantity;
        if (date.Month == rule.ExceptionMonth) qty = rule.MonthQuantity ?? qty;
        return ($"{Math.Max(1, rule.EveryMonths)}:{bucket}", qty);
    }
}
public record RecipeFilter(string? Search = null, string? Category = null, int? MaxKcal = null, int? MaxActive = null, int? MaxTotal = null, string? Include = null, string? Exclude = null, string? Allergens = null, string? Equipment = null)
{
    static string[] Parts(string? value) => (value ?? "").Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries).Select(Kitchen.Normalize).ToArray();
    public bool Matches(Recipe r)
    {
        var ingredients = r.Ingredients.Select(x => Kitchen.Normalize(x.Canonical)).ToArray();
        return (string.IsNullOrWhiteSpace(Search) || r.Title.Contains(Search, StringComparison.OrdinalIgnoreCase))
            && (string.IsNullOrWhiteSpace(Category) || r.Category == Category)
            && (MaxKcal == null || r.Kcal.HasValue && r.Kcal <= MaxKcal)
            && (MaxActive == null || r.ActiveMinutes.HasValue && r.ActiveMinutes <= MaxActive)
            && (MaxTotal == null || r.TotalMinutes.HasValue && r.TotalMinutes <= MaxTotal)
            && Parts(Include).All(ingredients.Contains) && !Parts(Exclude).Any(ingredients.Contains)
            && (Parts(Allergens).Length == 0 || r.AllergensVerified && !Parts(Allergens).Intersect(Parts(r.Allergens)).Any())
            && (Equipment == null || Parts(r.Equipment).All(Parts(Equipment).Contains));
    }
}
public class HouseholdService(AppDb db)
{
    public async Task<Week> GetWeek(Guid household, DateOnly monday)
    {
        monday = Kitchen.Monday(monday);
        var week = await db.Weeks.SingleOrDefaultAsync(x => x.HouseholdId == household && x.Monday == monday);
        if (week != null) return week;
        week = new Week { HouseholdId = household, Monday = monday };
        db.Weeks.Add(week);
        try { await db.SaveChangesAsync(); }
        catch (DbUpdateException) { db.Entry(week).State = EntityState.Detached; week = await db.Weeks.SingleAsync(x => x.HouseholdId == household && x.Monday == monday); }
        return week;
    }
    public async Task Save(Week week, Guid expected, WeekData data)
    {
        if (week.Revision != expected) throw new DbUpdateConcurrencyException();
        week.Data = data; week.Revision = Guid.NewGuid();
        await db.SaveChangesAsync();
    }
    public async Task EnsureReminders(Guid household, DateOnly date)
    {
        foreach (var rule in await db.Rules.Where(x => x.HouseholdId == household).ToListAsync())
        {
            var (period, qty) = Kitchen.Due(rule, date);
            if (await db.Reminders.AnyAsync(x => x.HouseholdId == household && x.RuleId == rule.Id && x.Period == period)) continue;
            var reminder = new Reminder { HouseholdId = household, RuleId = rule.Id, Period = period, Product = rule.Product, Unit = rule.Unit, Quantity = qty };
            db.Reminders.Add(reminder);
            try { await db.SaveChangesAsync(); } catch (DbUpdateException) { db.Entry(reminder).State = EntityState.Detached; if (!await db.Reminders.AnyAsync(x => x.HouseholdId == household && x.RuleId == rule.Id && x.Period == period)) throw; }
        }
    }
}
