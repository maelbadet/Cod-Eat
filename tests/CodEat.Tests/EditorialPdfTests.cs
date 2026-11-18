using CodEat.Web;
using PdfSharp.Fonts;
using Xunit;

namespace CodEat.Tests;

public class EditorialPdfTests
{
    [Fact]
    public void ShoppingReportKeepsLongRowsQuantitiesAndChecksAcrossPages()
    {
        GlobalFontSettings.FontResolver = new LocalFontResolver();
        var data = new WeekData();
        for (var i = 0; i < 60; i++)
            data.Shopping.Add(new ShoppingLine { Name = $"Produit numéro {i:00} avec un nom particulièrement long pour vérifier les retours à la ligne et les colonnes", Calculated = 1.25m, Unit = "kg", Aisle = "Épicerie", Note = i == 0 ? "Quantité à vérifier" : "" });
        data.Shopping.Add(new ShoppingLine { Name = "Produit déjà disponible", InStock = true });
        using var document = UglyToad.PdfPig.PdfDocument.Open(new PdfExport().CreateShopping(data, [], new(2026, 11, 16)));
        Assert.True(document.NumberOfPages >= 3);
        var text = string.Join(" ", document.GetPages().Select(x => x.Text));
        Assert.Contains("Produit numéro 00", text);
        Assert.Contains("Produit numéro 59", text);
        Assert.Contains("1,25 kg", text.Replace("1.25", "1,25"));
        Assert.DoesNotContain("Produit déjà disponible", text);
        foreach (var page in document.GetPages())
            Assert.All(page.Letters, letter => Assert.InRange(letter.BoundingBox.Right, 40, 553));
    }

    [Fact]
    public void MealReportKeepsPlanningAndPreparationOnSeparatePages()
    {
        GlobalFontSettings.FontResolver = new LocalFontResolver();
        var preparation = new Preparation { Portions = 4, At = new(2026, 11, 15, 16, 0, 0), Recipe = new Recipe { Title = "Cabillaud citron et pommes de terre", FridgeHours = 24, ActiveMinutes = 15, CookMinutes = 30, Equipment = "four,économe", Reheat = "Réchauffer à cœur." } };
        var data = new WeekData { Preparations = [preparation], Meals = [new(0, "Midi", preparation.Id, 2), new(1, "Soir", preparation.Id, 2)] };
        using var document = UglyToad.PdfPig.PdfDocument.Open(new PdfExport().CreateMeals(data, new(2026, 11, 16)));
        Assert.Equal(2, document.NumberOfPages);
        Assert.Contains("DÉJEUNER", document.GetPage(1).Text);
        Assert.Contains("DÎNER", document.GetPage(1).Text);
        Assert.Contains("Votre session cuisine", document.GetPage(2).Text);
        Assert.Contains("Conservation dépassée", document.GetPage(2).Text);
        Assert.Contains("Réchauffer à cœur", document.GetPage(2).Text);
    }
}
