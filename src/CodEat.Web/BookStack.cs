using System.Net;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;

namespace CodEat.Web;
public record ImportedPage(int Id, string Name, string Text);
public class BookStackClient(HttpClient http, IConfiguration config)
{
    public async Task<List<ImportedPage>> Read(CancellationToken ct)
    {
        if (config.GetValue<bool>("BookStack:Simulated")) return [new(9001, "Cabillaud citron et pommes de terre", "Cuire les pommes de terre. Ajouter le cabillaud et le citron, puis cuire au four. Source BookStack simulée.")];
        var baseUrl = config["BookStack:BaseUrl"] ?? throw new InvalidOperationException("BookStack non configuré.");
        if (!Uri.TryCreate(baseUrl.TrimEnd('/') + "/", UriKind.Absolute, out var uri) || uri.Scheme != "https") throw new InvalidOperationException("URL HTTPS requise.");
        http.BaseAddress ??= uri;
        http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Token", $"{config["BookStack:TokenId"]}:{config["BookStack:TokenSecret"]}");
        var book = config.GetValue<int>("BookStack:BookId");
        if (book <= 0) throw new InvalidOperationException("Identifiant de livre requis.");
        var result = new List<ImportedPage>();
        int offset = 0;
        while (true)
        {
            using var response = await http.GetAsync($"api/pages?count=100&offset={offset}&sort=%2Bid&filter[book_id]={book}", ct);
            response.EnsureSuccessStatusCode();
            using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync(ct));
            var entries = json.RootElement.GetProperty("data").EnumerateArray().ToArray();
            var total = json.RootElement.GetProperty("total").GetInt32();
            foreach (var item in entries)
            {
                var id = item.GetProperty("id").GetInt32();
                using var detail = await http.GetAsync($"api/pages/{id}", ct);
                detail.EnsureSuccessStatusCode();
                using var page = JsonDocument.Parse(await detail.Content.ReadAsStringAsync(ct));
                if (page.RootElement.GetProperty("book_id").GetInt32() != book) throw new InvalidOperationException("Livre modifié pendant la synchronisation.");
                var text = page.RootElement.TryGetProperty("markdown", out var md) && !string.IsNullOrWhiteSpace(md.GetString()) ? md.GetString()! : WebUtility.HtmlDecode(Regex.Replace(page.RootElement.GetProperty("html").GetString() ?? "", "<[^>]*>", " ", RegexOptions.None, TimeSpan.FromSeconds(1)));
                result.Add(new(id, page.RootElement.GetProperty("name").GetString()!, text));
            }
            offset += entries.Length;
            if (offset >= total) break;
            if (entries.Length == 0 || offset > 100000) throw new InvalidOperationException("Pagination incomplète.");
        }
        if (result.Select(x => x.Id).Distinct().Count() != result.Count) throw new InvalidOperationException("Catalogue modifié pendant la synchronisation.");
        return result;
    }
}
public class BookStackSync(AppDb db, BookStackClient client)
{
    public async Task<string> Run(CancellationToken ct)
    {
        // All external requests complete before any catalogue change is persisted.
        var pages = await client.Read(ct);
        var recipes = await db.Recipes.Where(x => x.BookStackId != null).ToListAsync(ct);
        int created = 0, updated = 0, same = 0;
        foreach (var p in pages)
        {
            var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(p.Name + "\n" + p.Text)));
            var r = recipes.SingleOrDefault(x => x.BookStackId == p.Id);
            if (r == null) { r = new Recipe { BookStackId = p.Id }; db.Recipes.Add(r); created++; }
            else if (r.SourceHash == hash) { r.Missing = false; same++; continue; }
            else updated++;
            // Editorial fields belong to BookStack; local structured enrichment is preserved.
            r.Title = p.Name; r.Steps = p.Text; r.SourceHash = hash; r.Missing = false; r.Status = "Brouillon"; r.Revision = Guid.NewGuid();
        }
        foreach (var r in recipes.Where(x => !pages.Any(p => p.Id == x.BookStackId))) r.Missing = true;
        await db.SaveChangesAsync(ct);
        return $"{created} créée(s), {updated} mise(s) à jour, {same} inchangée(s), 0 erreur. {recipes.Count(x => x.Missing)} absente(s) du livre.";
    }
}
