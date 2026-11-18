using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace CodEat.Web;

public sealed class ParsedRecipe
{
    public bool Recognized { get; set; }
    public List<Ingredient> Ingredients { get; set; } = [];
    public decimal? Servings { get; set; }
    public string Description { get; set; } = "";
    public string Steps { get; set; } = "";
    public string Category { get; set; } = "";
    public string Tags { get; set; } = "";
    public int? ActiveMinutes { get; set; }
    public int? CookMinutes { get; set; }
    public int? TotalMinutes { get; set; }
    public int? Kcal { get; set; }
    public string KcalSource { get; set; } = "";
    public string Allergens { get; set; } = "";
    public int? FridgeHours { get; set; }
    public bool? Freezable { get; set; }
    public int? FreezerDays { get; set; }
    public string Thaw { get; set; } = "";
    public string Reheat { get; set; } = "";
    public string Equipment { get; set; } = "";
    public List<string> ReviewNotes { get; set; } = [];
}

public static class RecipePageParser
{
    public const int Version = 2;
    static readonly TimeSpan Limit = TimeSpan.FromSeconds(1);
    static Match Match(string value, string pattern) => Regex.Match(value, pattern, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, Limit);
    public static string Normalize(string value) => string.Concat(BookStackContent.Inline(value).Normalize(NormalizationForm.FormD)
        .Where(c => CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.NonSpacingMark))
        .Normalize(NormalizationForm.FormC).ToLowerInvariant().Replace('’', '\'');

    public static ParsedRecipe Parse(string source)
    {
        var result = new ParsedRecipe();
        var lines = source.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n');
        var tables = Tables(lines);
        var ingredientTable = tables.FirstOrDefault(table => table.Count > 0
            && Normalize(table[0].ElementAtOrDefault(0) ?? "").StartsWith("ingredient")
            && Normalize(table[0].ElementAtOrDefault(1) ?? "").StartsWith("quantite"));
        var preparation = Section(lines, "preparation", "etapes", "instructions");
        // Navigation pages can contain headings and numbered links too. Require
        // an actual preparation instruction in addition to the ingredient table.
        var hasInstruction = preparation.Any(line =>
            Match(line.Trim(), @"^\d+[.)]\s+").Success
            && !Match(line, @"\[[^\]]+\]\([^)]*\)").Success
            && Match(Normalize(line), @"\b(couper|cuire|faire|melanger|laver|peser|prechauffer|ajouter|verser|chauffer|repartir|assembler|servir|egoutter|eplucher|emincer|fouetter|mixer|rincer|saisir|etaler|deposer|reunir)\b").Success);
        if (ingredientTable == null || !hasInstruction)
        {
            result.ReviewNotes.Add("Format non reconnu : renseignez les ingrédients structurés et les étapes avant validation.");
            return result;
        }

        foreach (var row in ingredientTable.Skip(1))
        {
            if (row.Length < 2 || string.IsNullOrWhiteSpace(row[0])) continue;
            var name = Kitchen.Normalize(BookStackContent.Inline(row[0]));
            var originalQuantity = BookStackContent.Inline(row[1]);
            var (quantity, unit) = Quantity(originalQuantity);
            var comments = string.Join(" ; ", row.Skip(2).Select(BookStackContent.Inline).Where(x => x != ""));
            if (quantity == null)
            {
                comments = string.Join(" ; ", new[] { comments, "Quantité source : " + originalQuantity }.Where(x => x != ""));
                result.ReviewNotes.Add($"Quantité à vérifier pour {name} : {originalQuantity}.");
            }
            result.Ingredients.Add(new(name, quantity, unit, Aisle(name), comments));
        }
        result.Recognized = result.Ingredients.Count > 0;
        if (!result.Recognized)
        {
            result.ReviewNotes.Add("Le tableau d’ingrédients est vide : complétez la fiche avant validation.");
            return result;
        }
        result.Steps = string.Join('\n', preparation.Select(BookStackContent.Inline)).Trim();
        var portionCandidates = new List<decimal>();
        var introduction = string.Join('\n', lines.TakeWhile(line => !line.TrimStart().StartsWith("##") && !line.TrimStart().StartsWith('|')));
        foreach (Match match in Regex.Matches(BookStackContent.Inline(introduction), @"(?<![\d.,])(?<n>\d+(?:[.,]\d+)?)\s+portions?\b", RegexOptions.IgnoreCase, Limit))
            if (Number(match.Groups["n"].Value) is decimal count && count > 0) portionCandidates.Add(count);
        var headerPortions = Match(ingredientTable[0][1], @"pour\s+(?<n>\d+(?:[.,]\d+)?)");
        if (headerPortions.Success && Number(headerPortions.Groups["n"].Value) is decimal headerCount && headerCount > 0) portionCandidates.Add(headerCount);
        var distinctPortions = portionCandidates.Distinct().ToArray();
        if (distinctPortions.Length == 1) result.Servings = distinctPortions[0];
        else result.ReviewNotes.Add(distinctPortions.Length == 0 ? "Portions de référence à renseigner." : "Portions contradictoires dans la source : vérifiez la fiche.");

        result.Description = string.Join(" ", lines.TakeWhile(line => !line.TrimStart().StartsWith("##") && !line.TrimStart().StartsWith('|'))
            .Where(line => !line.TrimStart().StartsWith('#') && !Normalize(line).StartsWith("inspiration"))
            .Select(BookStackContent.Inline).Where(x => x != ""));
        var inspiration = lines.Select(BookStackContent.Inline).FirstOrDefault(line => Normalize(line).StartsWith("inspiration"));
        if (inspiration != null)
        {
            var tokens = inspiration[(inspiration.IndexOf(':') + 1)..].Split('·', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
            result.Category = tokens.FirstOrDefault(x => Normalize(x) == "terre & mer") ?? tokens.FirstOrDefault(x => Normalize(x) == "mer")
                ?? tokens.FirstOrDefault(x => Normalize(x) == "terre") ?? "";
            if (result.Category != "") result.Category = Normalize(result.Category) switch { "mer" => "Mer", "terre" => "Terre", _ => "Terre & Mer" };
            result.Tags = string.Join(", ", tokens.Where(x => Normalize(x) is not ("terre" or "mer" or "terre & mer")));
        }
        if (result.Category == "") result.ReviewNotes.Add("Catégorie Terre / Mer à renseigner : aucune catégorie explicite reconnue dans la source.");
        foreach (var row in tables.SelectMany(x => x).Where(row => row.Length >= 2))
        {
            var key = Normalize(row[0]);
            var value = BookStackContent.Inline(row[1]);
            if (key.StartsWith("temps actif") || key.StartsWith("temps de preparation")) result.ActiveMinutes = Minutes(value);
            else if (key.StartsWith("temps total"))
            {
                result.TotalMinutes = Minutes(value);
                result.ReviewNotes.Add("Temps total indiqué par la source : " + value + ".");
            }
            else if (key.StartsWith("temps de cuisson") || key == "cuisson") result.CookMinutes = Minutes(value);
            else if (key.StartsWith("energie") || key.StartsWith("kcal") || key.StartsWith("calories"))
            {
                var kcal = Match(value, @"(?<![\d.,])(?<n>\d+(?:[.,]\d+)?)\s*kcal\s*(?:par|/)\s*portion\b");
                var amount = kcal.Success ? Number(kcal.Groups["n"].Value) : null;
                if (amount is >= 0 and <= int.MaxValue && decimal.Truncate(amount.Value) == amount)
                {
                    result.Kcal = (int)amount.Value;
                    result.KcalSource = Normalize(row[0] + " " + value).Contains("estim") || value.Contains('~') ? "estimée — BookStack" : "saisie dans BookStack";
                }
            }
        }
        if (result.CookMinutes == null) result.ReviewNotes.Add("Temps de cuisson global à renseigner.");

        var storage = Section(lines, "cuisiner a deux et conserver", "conservation").Select(BookStackContent.Inline).Where(x => x != "").ToArray();
        foreach (var line in storage)
        {
            var normalized = Normalize(line);
            var fridge = Match(normalized, @"organisation prevue sous\s+(?<n>\d+)\s*h\b|(?:refrigerateur|refrigeration)\s*:\s*(?<n>\d+)\s*h\b");
            if (fridge.Success && int.TryParse(fridge.Groups["n"].Value, out var hours)) result.FridgeHours = hours;
            if (normalized.Contains("refriger") && !normalized.StartsWith("- congelation")) result.ReviewNotes.Add("Conservation indiquée dans la source, à vérifier : " + line.TrimStart('-', ' '));
            if (normalized.TrimStart('-', ' ').StartsWith("congelation :"))
            {
                var content = line[(line.IndexOf(':') + 1)..].Trim();
                var normalizedContent = Normalize(content);
                if (Match(normalizedContent, @"\b(impossible|interdite?|deconseillee?|non)\b|ne pas congeler").Success) result.Freezable = false;
                else if (Match(normalizedContent, @"\b(congeler|possible|oui|autorisee?)\b").Success) result.Freezable = true;
                var days = Match(normalizedContent, @"(?<n>\d+)\s*jours?\b");
                if (days.Success && int.TryParse(days.Groups["n"].Value, out var count)) result.FreezerDays = count;
                result.ReviewNotes.Add("Congélation indiquée dans la source : " + content);
            }
            if (normalized.TrimStart('-', ' ').StartsWith("decongelation :")) result.Thaw = line[(line.IndexOf(':') + 1)..].Trim();
            if (normalized.TrimStart('-', ' ').StartsWith("service :") || normalized.TrimStart('-', ' ').StartsWith("rechauffage :")) result.Reheat = line[(line.IndexOf(':') + 1)..].Trim();
        }
        if (result.Freezable == true && result.FreezerDays == null) result.ReviewNotes.Add("Durée de conservation au congélateur absente de la source : à renseigner.");
        if (result.Freezable == true && result.Thaw == "") result.ReviewNotes.Add("Consignes de décongélation absentes de la source : à renseigner.");

        var allergens = Section(lines, "allergenes et energie", "allergenes").Select(BookStackContent.Inline).Where(x => x != "").ToArray();
        foreach (var line in allergens)
        {
            var normalized = Normalize(line).TrimStart('-', ' ');
            if (normalized.StartsWith("allergenes identifies") || normalized.StartsWith("allergenes declares"))
            {
                var declaration = line[(line.IndexOf(':') + 1)..].Trim().TrimEnd('.');
                if (!Normalize(declaration).StartsWith("aucun")) result.Allergens = declaration;
                result.ReviewNotes.Add("Déclaration d’allergènes de la source : " + declaration + ".");
            }
            else if (normalized.StartsWith("etiquettes a verifier")) result.ReviewNotes.Add(line.TrimStart('-', ' '));
        }
        result.ReviewNotes.Add("Vérifiez les ingrédients et les étiquettes avant de confirmer les allergènes ; une mention « aucun identifié » ne garantit pas leur absence.");
        var equipment = Section(lines, "ustensiles", "equipements", "materiel").Select(BookStackContent.Inline)
            .Select(x => x.TrimStart('-', ' ')).Where(x => x != "").ToArray();
        result.Equipment = string.Join(", ", equipment);
        if (equipment.Length == 0) result.ReviewNotes.Add("Ustensiles et appareils requis à compléter.");
        result.ReviewNotes.Add("Rayons proposés automatiquement : vérifiez le classement des produits.");
        if (Normalize(source).Contains("pas ete testee en cuisine")) result.ReviewNotes.Add("La source indique que cette recette n’a pas été testée en cuisine : revue nécessaire.");
        result.ReviewNotes = result.ReviewNotes.Distinct().ToList();
        return result;
    }

    public static (decimal? Quantity, string Unit) Quantity(string text)
    {
        var match = Match(BookStackContent.Inline(text), @"^(?<n>\d+\s*/\s*\d+|\d+(?:[.,]\d+)?|[½¼¾])\s*(?<u>[\p{L}][\p{L}. ’'()-]*)$|^(?<n>\d+(?:[.,]\d+)?)$");
        if (!match.Success) return (null, "");
        var number = match.Groups["n"].Value.Replace(" ", "");
        decimal? amount = number switch { "½" => .5m, "¼" => .25m, "¾" => .75m, _ => null };
        if (number.Contains('/'))
        {
            var parts = number.Split('/');
            if (Number(parts[0]) is decimal numerator && Number(parts[1]) is decimal denominator && denominator > 0) amount = numerator / denominator;
        }
        else amount ??= Number(number);
        if (amount == null || amount < 0) return (null, "");
        var rawUnit = Kitchen.Normalize(match.Groups["u"].Value);
        var unit = Normalize(rawUnit).Trim();
        unit = unit switch
        {
            "gramme" or "grammes" => "g", "kilogramme" or "kilogrammes" => "kg",
            "litre" or "litres" => "l", "millilitre" or "millilitres" => "ml",
            "centilitre" or "centilitres" => "cl", "pieces" or "piece(s)" => "pièce",
            "piece" => "pièce", "gousses" or "gousse(s)" => "gousse", "boites" or "boite(s)" or "boite" => "boîte", _ => rawUnit
        };
        return (amount, unit);
    }

    static decimal? Number(string value) => decimal.TryParse(value.Replace(',', '.'), NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out var number) ? number : null;

    static int? Minutes(string value)
    {
        var match = Match(value, @"^\s*(?:~|environ\s+)?(?:(?<h>\d+)\s*h(?:eures?)?\s*)?(?:(?<m>\d+)\s*min(?:utes?)?)?\s*(?:[,;(].*|hors\b.*)?$");
        if (!match.Success || !match.Groups["h"].Success && !match.Groups["m"].Success) return null;
        if (!int.TryParse(match.Groups["h"].Success ? match.Groups["h"].Value : "0", out var hours)
            || !int.TryParse(match.Groups["m"].Success ? match.Groups["m"].Value : "0", out var minutes)) return null;
        var total = (long)hours * 60 + minutes;
        return total <= int.MaxValue ? (int)total : null;
    }

    static string[] Section(string[] lines, params string[] names)
    {
        var start = -1;
        var level = 0;
        for (var i = 0; i < lines.Length; i++)
        {
            var heading = Match(lines[i].Trim(), @"^(?<marks>#{1,6})\s+(?<name>.+?)\s*#*\s*$");
            if (!heading.Success) continue;
            var currentLevel = heading.Groups["marks"].Length;
            if (start >= 0 && currentLevel <= level) return lines[start..i];
            if (start < 0 && names.Contains(Normalize(heading.Groups["name"].Value))) { start = i + 1; level = currentLevel; }
        }
        return start < 0 ? [] : lines[start..];
    }

    static List<List<string[]>> Tables(string[] lines)
    {
        var result = new List<List<string[]>>();
        List<string[]>? table = null;
        foreach (var line in lines)
        {
            if (!line.TrimStart().StartsWith('|')) { table = null; continue; }
            var cells = Regex.Split(line.Trim().Trim('|'), @"(?<!\\)\|", RegexOptions.None, Limit)
                .Select(x => BookStackContent.Inline(x.Replace("\\|", "|")).Trim()).ToArray();
            if (cells.All(x => Match(x, @"^:?-{2,}:?$").Success)) continue;
            if (table == null) { table = []; result.Add(table); }
            table.Add(cells);
        }
        return result;
    }

    static string Aisle(string name) => Normalize(name) switch
    {
        "filet de poulet" or "filets de poulet" or "poulet" or "boeuf" or "porc" => "Boucherie",
        "cabillaud" or "saumon" or "thon frais" => "Poissonnerie",
        "courgette" or "courgettes" or "poivron" or "poivrons" or "oignon" or "oignons" or "ail" or "carotte" or "carottes" or "citron" or "pommes de terre" => "Fruits et légumes",
        "riz long grain sec" or "riz" or "huile de colza ou d'olive" or "huile d'olive" or "huile de colza" or "coulis de tomate nature" or "herbes de provence sechees" or "olives denoyautees egouttees" or "lentilles" => "Épicerie",
        _ => "À classer"
    };
}
