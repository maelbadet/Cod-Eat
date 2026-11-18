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
    public async Task<List<ImportedPage>> Read(CancellationToken ct, Action<BookStackProgress>? progress = null)
    {
        progress?.Invoke(new(0, 0, "Vérification de la connexion et du livre"));
        if (config.GetValue<bool>("BookStack:Simulated"))
        {
            ct.ThrowIfCancellationRequested();
            progress?.Invoke(new(1, 1, "Lecture des fiches de démonstration"));
            return [new(9001, "Cabillaud citron et pommes de terre", "Cuire les pommes de terre. Ajouter le cabillaud et le citron, puis cuire au four. Source BookStack simulée.")];
        }
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
            progress?.Invoke(new(result.Count, total, "Lecture des fiches BookStack"));
            foreach (var item in entries)
            {
                var id = item.GetProperty("id").GetInt32();
                using var page = await GetJson($"api/pages/{id}", ct);
                if (page.RootElement.GetProperty("book_id").GetInt32() != book) throw new BookStackFailure("BS_CHANGED", "Le livre a changé pendant la synchronisation. Réessayez pour obtenir un catalogue cohérent.");
                var text = page.RootElement.TryGetProperty("markdown", out var md) && !string.IsNullOrWhiteSpace(md.GetString())
                    ? md.GetString()! : BookStackContent.FromHtml(page.RootElement.GetProperty("html").GetString() ?? "");
                result.Add(new(id, page.RootElement.GetProperty("name").GetString()!, text));
                progress?.Invoke(new(result.Count, total, "Lecture des fiches BookStack"));
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
    public async Task<string> Run(CancellationToken ct, Action<BookStackProgress>? progress = null)
    {
        // All external requests complete before any catalogue change is persisted.
        var pages = await client.Read(ct, progress);
        ct.ThrowIfCancellationRequested();
        progress?.Invoke(new(pages.Count, pages.Count, "Enregistrement du catalogue"));
        var recipes = await db.Recipes.Where(x => x.BookStackId != null).ToListAsync(ct);
        int created = 0, updated = 0, same = 0, enriched = 0;
        foreach (var p in pages)
        {
            var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(p.Name + "\n" + p.Text)));
            var r = recipes.SingleOrDefault(x => x.BookStackId == p.Id);
            var isNew = r == null;
            var sourceChanged = r?.SourceHash != hash;
            if (r != null && !sourceChanged && RecipeImportState.Read(r.ImportStateJson).Version >= RecipePageParser.Version)
            { r.Missing = false; same++; continue; }
            if (r == null) { r = new Recipe { BookStackId = p.Id }; db.Recipes.Add(r); created++; }
            else if (sourceChanged) updated++;
            else enriched++;
            var parsed = RecipePageParser.Parse(p.Text);
            var steps = parsed.Recognized ? parsed.Steps : p.Text;
            var editorialChanged = r.Title != p.Name || r.Steps != steps;
            var structuredChanged = RecipeImportMerge.Apply(r, parsed, isNew);
            // The parser pre-fills data, but never verifies allergens or publishes recipes.
            if (sourceChanged || structuredChanged || editorialChanged || !parsed.Recognized)
            {
                r.Status = "Brouillon";
                r.AllergensVerified = false;
                r.AvailablePrivately = false;
            }
            r.Title = p.Name; r.Steps = steps; r.SourceHash = hash; r.Missing = false; r.Revision = Guid.NewGuid();
        }
        foreach (var r in recipes.Where(x => !pages.Any(p => p.Id == x.BookStackId))) r.Missing = true;
        await db.SaveChangesAsync(ct);
        return $"{created} créée(s), {updated} mise(s) à jour, {enriched} préremplie(s), {same} inchangée(s), 0 erreur. {recipes.Count(x => x.Missing)} absente(s) du livre."
            + (created + updated + enriched > 0 ? " Les données reconnues ont été préremplies. Relisez les points à vérifier et validez les brouillons dans Administration pour les utiliser dans le planning." : "");
    }
}
