using PdfSharp.Drawing;
using PdfSharp.Fonts;
using PdfSharp.Pdf;

namespace CodEat.Web;
public class LocalFontResolver : IFontResolver
{
    public FontResolverInfo ResolveTypeface(string familyName, bool bold, bool italic) => new(bold ? "DejaVuSans-Bold" : "DejaVuSans");
    public byte[] GetFont(string faceName) => File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "Fonts", faceName + ".ttf"));
}
public partial class PdfExport
{
    public static string ShoppingText(WeekData data, IEnumerable<Reminder> reminders)
    {
        var lines = new List<string> { "Cod & Eat — Liste de courses" };
        foreach (var group in data.Shopping.Where(x => !x.InStock).GroupBy(x => x.Aisle))
        {
            lines.Add(group.Key);
            lines.AddRange(group.Select(x =>  $"{(x.Checked ? "[x]" : "[ ]")} {x.Name} : {(x.Adjusted ?? x.Calculated)?.ToString("0.###")} {x.Unit}" + (x.Changed ? " — Quantité modifiée à confirmer" : "")));
        }
        lines.Add("À vérifier");
        lines.AddRange(data.Preparations.Where(x=>!x.Recipe.AllergensVerified).Select(x=>x.Recipe.Title).Distinct().Select(x=>x+" : allergènes non vérifiés, vérifiez les ingrédients et les étiquettes."));
        lines.AddRange(data.Shopping.Where(x => x.Note != "").Select(x => $"{x.Name} : {x.Note}"));
        lines.AddRange(reminders.Where(x => x.Decision == "En attente").Select(x => $"{x.Product} : {x.Quantity:0.###} {x.Unit}"));
        return string.Join('\n', lines);
    }
    public static string MealsText(WeekData data, DateOnly monday)
    {
        var lines = new List<string> { $"Cod & Eat — Repas du {monday:dd/MM/yyyy}" };
        for (var day = 0; day < 7; day++)
        {
            lines.Add(monday.AddDays(day).ToString("dddd dd MMMM"));
            foreach (var slot in new[] { "Midi", "Soir" })
            {
                var m = data.Meals.SingleOrDefault(x => x.Day == day && x.Slot == slot);
                lines.Add(m == null ? $"{slot} : libre" : $"{slot} : {data.Preparations.Single(x => x.Id == m.PreparationId).Recipe.Title} — {m.Portions:0.##} portions");
            }
        }
        foreach (var session in data.Preparations.GroupBy(x => x.At))
        {
            lines.Add($"Session du {session.Key:dd/MM/yyyy HH:mm} (Europe/Paris)");
            lines.Add($"Temps actifs cumulés : {session.Sum(x => x.Recipe.ActiveMinutes ?? 0)} min (durées inconnues exclues ; durée réelle non estimée)");
            lines.Add("Ustensiles : " + string.Join(", ", session.SelectMany(x => x.Recipe.Equipment.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)).Distinct()));
            foreach (var p in session)
            {
                lines.Add($"{p.Recipe.Title} : {p.Portions:0.##} portions ; actif {p.Recipe.ActiveMinutes?.ToString() ?? "?"} min ; cuisson {p.Recipe.CookMinutes?.ToString() ?? "?"} min");
                lines.Add("Ingrédients pour le lot :");
                lines.AddRange(RecipeQuantities.For(p.Recipe,p.Portions).Select(x=>$"{x.Canonical} : {RecipeQuantities.Quantity(x)} · {x.Comment}"));
                lines.Add("Préparation complète :");lines.Add(RecipeQuantities.ScalingNote(p.Recipe,p.Portions));lines.Add(p.Recipe.Steps);
                lines.Add($"Conservation : {(p.Frozen ? "congelé " + p.Recipe.FreezerDays + " jours" : "réfrigérateur " + p.Recipe.FridgeHours + " heures")}");
                lines.Add($"Décongélation : {p.Recipe.Thaw} ; Réchauffage : {p.Recipe.Reheat}");
                lines.AddRange(Kitchen.Warnings(p, data, monday).Select(x => "ATTENTION : " + x));
            }
        }
        return string.Join('\n', lines);
    }
    public byte[] Create(string text)
    {
        var lines = text.Split('\n');
        using var report = new EditorialReport(lines[0].Replace("Cod & Eat — ", ""), "Votre carnet de cuisine");
        foreach (var line in lines.Skip(1)) report.Paragraph(line);
        return report.Finish();
    }
}
