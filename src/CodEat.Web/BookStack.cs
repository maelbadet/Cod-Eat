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
        var baseUrl = config["BookStack:BaseUrl"]?.Trim();
        if (string.IsNullOrWhiteSpace(baseUrl) || !Uri.TryCreate(baseUrl.TrimEnd('/') + "/", UriKind.Absolute, out var uri)
            || uri.Scheme != "https" || uri.UserInfo != "" || uri.Query != "" || uri.Fragment != "")
            throw new BookStackFailure("BS_CONFIG_URL", "BOOKSTACK_URL doit être l’URL HTTPS de base de BookStack, sans identifiants, paramètres ni fragment.");
        if (Regex.IsMatch(uri.AbsolutePath, @"/(api(/docs)?|books/[^/]+|shelves/[^/]+)/$", RegexOptions.IgnoreCase))
            throw new BookStackFailure("BS_CONFIG_URL", "BOOKSTACK_URL doit pointer vers la racine de l’installation BookStack, pas vers une page de livre ou /api/docs.");
        var tokenId = config["BookStack:TokenId"]?.Trim();
        var tokenSecret = config["BookStack:TokenSecret"]?.Trim();
        if (string.IsNullOrWhiteSpace(tokenId) || string.IsNullOrWhiteSpace(tokenSecret)
            || tokenId.Any(char.IsWhiteSpace) || tokenSecret.Any(char.IsWhiteSpace)
            || tokenId.Contains(':') || tokenSecret.Contains(':'))
            throw new BookStackFailure("BS_CONFIG_TOKEN", "BOOKSTACK_TOKEN_ID et BOOKSTACK_TOKEN_SECRET doivent tous les deux être renseignés avec le jeton API du compte technique.");
        if (!int.TryParse(config["BookStack:BookId"], out var book) || book <= 0)
            throw new BookStackFailure("BS_CONFIG_BOOK", "BOOKSTACK_BOOK_ID doit être l’identifiant numérique positif du livre, pas son nom ni son URL.");
        http.BaseAddress ??= uri;
        http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Token", $"{tokenId}:{tokenSecret}");
        if (!http.DefaultRequestHeaders.Accept.Any(x => x.MediaType == "application/json"))
            http.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        // A wrong or inaccessible book must fail, not look like a successfully emptied catalogue.
        using var bookJson = await GetJson($"api/books/{book}", ct);
        if (!bookJson.RootElement.TryGetProperty("id", out var bookId) || bookId.GetInt32() != book)
            throw new BookStackFailure("BS_RESPONSE", "L’API ne renvoie pas le livre demandé. Vérifiez l’URL de base et les règles du reverse proxy.");
        var result = new List<ImportedPage>();
        int offset = 0;
        while (true)
        {
            using var json = await GetJson($"api/pages?count=100&offset={offset}&sort=%2Bid&filter[book_id]={book}", ct);
            var entries = json.RootElement.GetProperty("data").EnumerateArray().ToArray();
            var total = json.RootElement.GetProperty("total").GetInt32();
            foreach (var item in entries)
            {
                var id = item.GetProperty("id").GetInt32();
                using var page = await GetJson($"api/pages/{id}", ct);
                if (page.RootElement.GetProperty("book_id").GetInt32() != book) throw new BookStackFailure("BS_CHANGED", "Le livre a changé pendant la synchronisation. Réessayez pour obtenir un catalogue cohérent.");
                var text = page.RootElement.TryGetProperty("markdown", out var md) && !string.IsNullOrWhiteSpace(md.GetString()) ? md.GetString()! : WebUtility.HtmlDecode(Regex.Replace(page.RootElement.GetProperty("html").GetString() ?? "", "<[^>]*>", " ", RegexOptions.None, TimeSpan.FromSeconds(1)));
                result.Add(new(id, page.RootElement.GetProperty("name").GetString()!, text));
            }
            offset += entries.Length;
            if (offset >= total) break;
            if (entries.Length == 0 || offset > 100000) throw new BookStackFailure("BS_PAGINATION", "L’API a retourné une liste incomplète. Aucun import partiel n’a été enregistré ; vérifiez la pagination et réessayez.");
        }
        if (result.Select(x => x.Id).Distinct().Count() != result.Count) throw new BookStackFailure("BS_CHANGED", "Le catalogue a changé pendant la synchronisation. Réessayez pour obtenir une liste cohérente.");
        return result;
    }

    async Task<JsonDocument> GetJson(string path, CancellationToken ct)
    {
        using var response = await http.GetAsync(path, ct);
        if (!response.IsSuccessStatusCode)
        {
            var diagnostic = BookStackDiagnostic.Http((int)response.StatusCode);
            throw new BookStackFailure(diagnostic.Code, diagnostic.Guidance);
        }
        // Never include the response body, request URI, Authorization or redirect target in errors.
        return JsonDocument.Parse(await response.Content.ReadAsStringAsync(ct));
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
