using CodEat.Web;
using PdfSharp.Fonts;
using Xunit;
namespace CodEat.Tests;
public class PdfTests {
 [Fact] public void AccentsDecimalQuantitiesAndPageBreaksRemainReadable(){GlobalFontSettings.FontResolver=new LocalFontResolver();var text="Cod & Eat — À vérifier\nÉpicerie : crème fraîche 1,25 kg\n"+string.Join('\n',Enumerable.Range(0,180).Select(i=>$"[ ] Produit numéro {i} : légumes et céréales 0,125 kg"));using var pdf=UglyToad.PdfPig.PdfDocument.Open(new PdfExport().Create(text));Assert.True(pdf.NumberOfPages>=3);var content=string.Concat(pdf.GetPages().Select(p=>p.Text));Assert.Contains("Épicerie",content);Assert.Contains("1,25 kg",content);Assert.Contains("179",content);}
}
