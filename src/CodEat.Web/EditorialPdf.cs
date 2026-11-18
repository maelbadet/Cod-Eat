using PdfSharp.Drawing;
using PdfSharp.Pdf;

namespace CodEat.Web;

public partial class PdfExport
{
    public byte[] CreateShopping(WeekData data, IEnumerable<Reminder> reminders, DateOnly monday)
    {
        var pending = reminders.Where(x => x.Decision == "En attente").ToArray();
        var products = data.Shopping.Where(x => !x.InStock).ToArray();
        var checks = products.Where(x => x.Note != "" || x.Changed).ToArray();
        using var report = new EditorialReport("La liste de courses", Period(monday));
        report.Stats((products.Length.ToString(), products.Length == 1 ? "produit à prévoir" : "produits à prévoir"),
            (products.Select(x => x.Aisle).Distinct().Count().ToString(), products.Select(x => x.Aisle).Distinct().Count() == 1 ? "rayon" : "rayons"),
            ((pending.Length + checks.Length).ToString(), "points à vérifier"));
        var index = 0;
        foreach (var group in products.GroupBy(x => x.Aisle))
        {
            report.Section(++index, group.Key);
            report.TableHeading("PRODUIT", "QUANTITÉ");
            foreach (var product in group)
            {
                var quantity = product.Adjusted ?? product.Calculated;
                var label = quantity.HasValue ? $"{quantity:0.###} {product.Unit}".Trim() : "À vérifier";
                report.ShoppingRow(product.Name, label, product.Checked,
                    product.Changed ? "Quantité modifiée · confirmation nécessaire" : "");
            }
            report.Gap(12);
        }
        if (checks.Length > 0 || pending.Length > 0)
        {
            report.Section(++index, "À vérifier avant de partir");
            report.Paragraph("Ces éléments demandent une confirmation et ne sont pas marqués comme achetés.", muted: true);
            foreach (var product in checks)
                report.Paragraph(product.Name + " — " + (product.Note != "" ? product.Note : "Quantité calculée modifiée à confirmer."));
            foreach (var reminder in pending)
                report.ShoppingRow(reminder.Product, $"{reminder.Quantity:0.###} {reminder.Unit}", false, "Rappel du foyer · en attente");
        }
        if (products.Length == 0 && pending.Length == 0) report.Paragraph("Votre liste est vide. Ajoutez des repas à votre semaine pour la compléter.");
        return report.Finish();
    }

    public byte[] CreateMeals(WeekData data, DateOnly monday)
    {
        using var report = new EditorialReport("Les repas de la semaine", Period(monday));
        report.Stats((data.Meals.Count.ToString(), "repas planifiés"),
            (data.Preparations.Count.ToString(), data.Preparations.Count == 1 ? "préparation" : "préparations"),
            (data.Preparations.Sum(x => x.Portions).ToString("0.##"), "portions à cuisiner"));
        report.Section(1, "Votre semaine, en un regard");
        report.PlanningHeading();
        for (var day = 0; day < 7; day++)
        {
            string Meal(string slot)
            {
                var meal = data.Meals.SingleOrDefault(x => x.Day == day && x.Slot == slot);
                return meal == null ? "Créneau libre" : $"{data.Preparations.Single(x => x.Id == meal.PreparationId).Recipe.Title}\n{meal.Portions:0.##} portions";
            }
            report.PlanningRow(monday.AddDays(day), Meal("Midi"), Meal("Soir"));
        }
        report.Gap(16);
        report.Paragraph("Un même lot peut alimenter plusieurs repas. Les courses comptent chaque préparation une seule fois.", muted: true);
        if (data.Preparations.Count == 0) return report.Finish();
        report.NewChapter("Votre session cuisine", "Les préparations, les ustensiles et les consignes de conservation.");
        report.Stats((data.Preparations.Sum(x => x.Recipe.ActiveMinutes ?? 0).ToString(), "min actives cumulées*"),
            (data.Preparations.Count.ToString(), data.Preparations.Count == 1 ? "lot à préparer" : "lots à préparer"),
            (data.Preparations.Select(x => x.At).Distinct().Count().ToString(), data.Preparations.Select(x => x.At).Distinct().Count() == 1 ? "session" : "sessions"));
        report.Paragraph("* Cumul des temps connus, distinct de la durée réelle de la session. Les temps manquants sont exclus.", muted: true);
        var index = 1;
        foreach (var session in data.Preparations.GroupBy(x => x.At).OrderBy(x => x.Key))
        {
            report.Section(++index, session.Key.ToString("dddd dd MMMM · HH:mm"));
            var tools = session.SelectMany(x => x.Recipe.Equipment.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)).Distinct();
            report.Paragraph("Ustensiles de la session — " + string.Join(", ", tools));
            report.Paragraph("Heure de Paris · Europe/Paris", muted: true);
            foreach (var preparation in session)
            {
                var recipe = preparation.Recipe;
                report.RecipeHeading(recipe.Title, $"{preparation.Portions:0.##} portions à cuisiner");
                report.Paragraph($"Préparation active : {Minutes(recipe.ActiveMinutes)}   /   Cuisson : {Minutes(recipe.CookMinutes)}   /   Total : {Minutes(recipe.TotalMinutes)}", muted: true);
                var meals = data.Meals.Where(x => x.PreparationId == preparation.Id)
                    .Select(x => $"{monday.AddDays(x.Day):ddd dd/MM} {x.Slot.ToLowerInvariant()} · {x.Portions:0.##} portions");
                report.Paragraph("Repas associés — " + string.Join(" ; ", meals));
                report.Label("CONSERVATION", preparation.Frozen
                    ? $"Congélation : {(recipe.FreezerDays.HasValue ? recipe.FreezerDays + " jours" : "durée à renseigner")}."
                    : $"Réfrigérateur : {(recipe.FridgeHours.HasValue ? recipe.FridgeHours + " heures" : "Conservation à renseigner")}.");
                if (preparation.Frozen) report.Label("DÉCONGÉLATION", string.IsNullOrWhiteSpace(recipe.Thaw) ? "Consignes à renseigner." : recipe.Thaw);
                report.Label("AVANT DE SERVIR", string.IsNullOrWhiteSpace(recipe.Reheat) ? "Réchauffage à renseigner." : recipe.Reheat);
                foreach (var warning in Kitchen.Warnings(preparation, data, monday)) report.Warning(warning);
                report.Gap(14);
            }
        }
        return report.Finish();
    }

    static string Period(DateOnly monday) => $"Semaine du {monday:dd MMMM} au {monday.AddDays(6):dd MMMM yyyy}";
    static string Minutes(int? value) => value.HasValue ? $"{value} min" : "non renseigné";
}

// Fixed A4 grid shared by the two reports; document data stays structured until drawing.
internal sealed class EditorialReport : IDisposable
{
    readonly PdfDocument document = new();
    readonly XFont body = new("DejaVu Sans", 9);
    readonly XFont bold = new("DejaVu Sans", 9, XFontStyleEx.Bold);
    readonly XFont small = new("DejaVu Sans", 7.5);
    readonly XFont heading = new("DejaVu Sans", 13, XFontStyleEx.Bold);
    readonly XFont titleFont = new("DejaVu Sans", 27, XFontStyleEx.Bold);
    readonly XSolidBrush ink = new(XColor.FromArgb(0x19, 0x19, 0x19));
    readonly XSolidBrush red = new(XColor.FromArgb(0xB9, 0x1C, 0x1C));
    readonly XSolidBrush grey = new(XColor.FromArgb(0x6B, 0x6B, 0x6B));
    readonly XSolidBrush paper = new(XColor.FromArgb(0xF5, 0xF4, 0xF1));
    readonly XPen rule = new(XColor.FromArgb(0xE4, 0xE2, 0xDF), .6);
    XGraphics graphics = null!;
    string title;
    string currentSection = "";
    readonly string subtitle;
    double y;
    const double Left = 44, Right = 551, Width = Right - Left;

    public EditorialReport(string title, string subtitle)
    {
        this.title = title;
        this.subtitle = subtitle;
        document.Info.Title = "Cod & Eat — " + title;
        document.Info.Author = "Cod & Eat";
        Page(true);
    }

    void Page(bool opening = false)
    {
        graphics?.Dispose();
        var page = document.AddPage();
        page.Size = PdfSharp.PageSize.A4;
        graphics = XGraphics.FromPdfPage(page);
        graphics.DrawRectangle(red, Left, 31, 4, 14);
        graphics.DrawString("Cod & Eat", bold, ink, new XPoint(Left + 12, 42));
        RightText("TERRE & MER / LE CARNET", small, grey, Right, 42);
        graphics.DrawLine(rule, Left, 58, Right, 58);
        if (opening)
        {
            graphics.DrawString("S’ORGANISER. CUISINER. SAVOURER.", small, red, new XPoint(Left, 83));
            y = 121;
            foreach (var line in Wrap(title, titleFont, Width))
            {
                graphics.DrawString(line, titleFont, ink, new XPoint(Left, y));
                y += 34;
            }
            graphics.DrawString(subtitle, body, grey, new XPoint(Left, y - 6));
            y += 15;
        }
        else
        {
            graphics.DrawString(title + " · suite", bold, ink, new XPoint(Left, 79));
            if (currentSection != "") graphics.DrawString(currentSection, small, red, new XPoint(Left, 96));
            y = 118;
        }
    }

    public bool Ensure(double height)
    {
        if (y + height <= 781) return false;
        Page();
        return true;
    }

    public void NewChapter(string name, string description)
    {
        title = name;
        Page(true);
        Paragraph(description, muted: true);
    }

    public void Gap(double amount) => y += amount;

    public void Stats(params (string Value, string Label)[] stats)
    {
        Ensure(62);
        graphics.DrawRoundedRectangle(paper, Left, y, Width, 57, 6, 6);
        var column = Width / stats.Length;
        for (var i = 0; i < stats.Length; i++)
        {
            var x = Left + 15 + column * i;
            graphics.DrawString(stats[i].Value, new XFont("DejaVu Sans", 18, XFontStyleEx.Bold), ink, new XPoint(x, y + 25));
            graphics.DrawString(stats[i].Label, small, grey, new XPoint(x, y + 43));
            if (i > 0) graphics.DrawLine(rule, x - 15, y + 12, x - 15, y + 45);
        }
        y += 80;
    }

    public void Section(int number, string name)
    {
        currentSection = name;
        var lines = Wrap(name, heading, Width - 34);
        Ensure(lines.Length * 18 + 62);
        graphics.DrawString(number.ToString("00"), bold, red, new XPoint(Left, y));
        foreach (var line in lines)
        {
            graphics.DrawString(line, heading, ink, new XPoint(Left + 31, y));
            y += 18;
        }
        graphics.DrawLine(rule, Left, y - 8, Right, y - 8);
        y += 8;
    }

    public void TableHeading(string first, string last)
    {
        Ensure(27);
        graphics.DrawString(first, small, grey, new XPoint(Left + 23, y));
        RightText(last, small, grey, Right - 8, y);
        y += 16;
    }

    public void ShoppingRow(string name, string quantity, bool bought, string note)
    {
        var names = Wrap(name, body, 340);
        var quantities = Wrap(quantity, bold, 115);
        var notes = string.IsNullOrWhiteSpace(note) ? [] : Wrap(note, small, 340);
        var height = Math.Max(names.Length * 14 + notes.Length * 11, quantities.Length * 14) + 17;
        if (Ensure(height)) TableHeading("PRODUIT · SUITE", "QUANTITÉ");
        graphics.DrawRoundedRectangle(new XPen(XColor.FromArgb(0x91, 0x91, 0x91), .8), Left + 1, y - 8, 10, 10, 2, 2);
        if (bought)
        {
            graphics.DrawLine(new XPen(XColor.FromArgb(0x19, 0x19, 0x19), 1), Left + 3, y - 4, Left + 5, y - 1);
            graphics.DrawLine(new XPen(XColor.FromArgb(0x19, 0x19, 0x19), 1), Left + 5, y - 1, Left + 9, y - 6);
        }
        for (var i = 0; i < names.Length; i++) graphics.DrawString(names[i], body, ink, new XPoint(Left + 23, y + i * 14));
        for (var i = 0; i < quantities.Length; i++) RightText(quantities[i], bold, ink, Right - 8, y + i * 14);
        for (var i = 0; i < notes.Length; i++) graphics.DrawString(notes[i], small, red, new XPoint(Left + 23, y + names.Length * 14 + i * 11));
        y += height;
        graphics.DrawLine(rule, Left, y - 11, Right, y - 11);
    }

    public void PlanningHeading()
    {
        Ensure(25);
        graphics.DrawString("JOUR", small, grey, new XPoint(Left + 9, y));
        graphics.DrawString("DÉJEUNER", small, grey, new XPoint(Left + 116, y));
        graphics.DrawString("DÎNER", small, grey, new XPoint(Left + 317, y));
        y += 15;
    }

    public void PlanningRow(DateOnly date, string lunch, string dinner)
    {
        var a = lunch.Split('\n').SelectMany(x => Wrap(x, body, 177)).ToArray();
        var b = dinner.Split('\n').SelectMany(x => Wrap(x, body, 177)).ToArray();
        var height = Math.Max(55, Math.Max(a.Length, b.Length) * 13 + 20);
        if (Ensure(height + 8)) PlanningHeading();
        graphics.DrawRoundedRectangle(paper, Left, y - 9, 98, height - 5, 4, 4);
        graphics.DrawString(date.ToString("dddd"), bold, ink, new XPoint(Left + 10, y + 6));
        graphics.DrawString(date.ToString("dd MMM"), small, grey, new XPoint(Left + 10, y + 22));
        for (var i = 0; i < a.Length; i++) graphics.DrawString(a[i], body, lunch == "Créneau libre" ? grey : ink, new XPoint(Left + 116, y + 5 + i * 13));
        for (var i = 0; i < b.Length; i++) graphics.DrawString(b[i], body, dinner == "Créneau libre" ? grey : ink, new XPoint(Left + 317, y + 5 + i * 13));
        y += height;
        graphics.DrawLine(rule, Left + 109, y - 11, Right, y - 11);
    }

    public void RecipeHeading(string name, string portions)
    {
        var lines = Wrap(name, heading, Width);
        Ensure(lines.Length * 18 + 110);
        Gap(12);
        foreach (var line in lines) { graphics.DrawString(line, heading, ink, new XPoint(Left, y)); y += 18; }
        graphics.DrawString(portions, bold, red, new XPoint(Left, y));
        y += 20;
    }

    public void Label(string label, string text)
    {
        Ensure(40);
        graphics.DrawString(label, small, grey, new XPoint(Left, y));
        y += 14;
        Paragraph(text);
    }

    public void Paragraph(string text, bool muted = false)
    {
        foreach (var line in Wrap(text, body, Width))
        {
            Ensure(16);
            graphics.DrawString(line, body, muted ? grey : ink, new XPoint(Left, y));
            y += 14;
        }
        y += 7;
    }

    public void Warning(string text)
    {
        var lines = Wrap("Attention — " + text, body, Width - 24);
        var height = lines.Length * 14 + 18;
        Ensure(height + 8);
        graphics.DrawRoundedRectangle(new XSolidBrush(XColor.FromArgb(0xFC, 0xF3, 0xF2)), Left, y - 11, Width, height, 5, 5);
        graphics.DrawRectangle(red, Left, y - 6, 2, height - 10);
        foreach (var line in lines) { graphics.DrawString(line, body, red, new XPoint(Left + 12, y + 2)); y += 14; }
        y += 16;
    }

    string[] Wrap(string text, XFont font, double width)
    {
        var result = new List<string>();
        var line = "";
        foreach (var word in text.Split(' ', StringSplitOptions.RemoveEmptyEntries))
        {
            if (line != "" && graphics.MeasureString(line + " " + word, font).Width > width) { result.Add(line); line = ""; }
            foreach (var c in (line != "" ? " " : "") + word)
            {
                if (line != "" && graphics.MeasureString(line + c, font).Width > width) { result.Add(line); line = ""; }
                line += c;
            }
        }
        if (line != "") result.Add(line);
        return result.ToArray();
    }

    void RightText(string text, XFont font, XBrush brush, double x, double baseline)
        => graphics.DrawString(text, font, brush, new XPoint(x - graphics.MeasureString(text, font).Width, baseline));

    public byte[] Finish()
    {
        graphics.Dispose();
        for (var i = 0; i < document.PageCount; i++)
        {
            graphics = XGraphics.FromPdfPage(document.Pages[i], XGraphicsPdfPageOptions.Append);
            graphics.DrawLine(rule, Left, 800, Right, 800);
            graphics.DrawString("Cod & Eat  /  Votre semaine a du goût.", small, grey, new XPoint(Left, 817));
            RightText($"{i + 1:00} / {document.PageCount:00}", small, grey, Right, 817);
            graphics.Dispose();
        }
        using var stream = new MemoryStream();
        document.Save(stream);
        return stream.ToArray();
    }

    public void Dispose() { graphics?.Dispose(); document.Dispose(); }
}
