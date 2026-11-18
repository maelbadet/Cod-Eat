using System.Security.Claims;
using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;

namespace CodEat.Web.Pages.Admin;

public class ReviewImportsModel(AppDb db, IDataProtectionProvider protection, BookStackSyncQueue queue) : PageModel
{
    public record Item(int Id, Guid Revision, string Title, string[] Problems, bool UnknownAllergens, bool Available);
    public record Selection(string User, List<Selected> Recipes, string Mode);
    public record Selected(int Id, Guid Revision);
    private readonly ITimeLimitedDataProtector protector = protection.CreateProtector("PrivateRecipeReview.v1").ToTimeLimitedDataProtector();
    public List<Item> Items { get; private set; } = [];
    public int EligibleCount { get; private set; }
    public int IncompleteCount { get; private set; }
    public int AvailableCount { get; private set; }
    public int Total { get; private set; }
    public int Pages => Math.Max(1,(Total+49)/50);
    [BindProperty(SupportsGet=true)] public int CatalogPage { get; set; } = 1;
    [BindProperty(SupportsGet=true)] public string Mode { get; set; } = "private";
    public bool Publishing => Mode == "public";
    public bool CatalogOpen { get; private set; }
    [BindProperty] public string Ticket { get; set; } = "";
    [BindProperty] public bool Confirm { get; set; }
    public int PreviewCount { get; private set; }

    private async Task<List<Recipe>> ReadImports() => await db.Recipes.AsNoTracking()
        .Where(x=>x.BookStackId!=null && (x.Status=="Brouillon" || Publishing && x.Status=="Validée"))
        .Select(x=>new Recipe { Id=x.Id, Revision=x.Revision, Title=x.Title, Missing=x.Missing,
            Steps=x.Steps.Trim()==""?"":"Étapes présentes", Category=x.Category, Servings=x.Servings,
            BookStackId=x.BookStackId, ImportStateJson=x.ImportStateJson,
            IngredientsJson=x.IngredientsJson, Kcal=x.Kcal, KcalSource=x.KcalSource,
            ActiveMinutes=x.ActiveMinutes, CookMinutes=x.CookMinutes, TotalMinutes=x.TotalMinutes,
            FridgeHours=x.FridgeHours, FreezerDays=x.FreezerDays, AllergensVerified=x.AllergensVerified,
            AvailablePrivately=x.AvailablePrivately, Status=x.Status, Slug=x.Slug, OldSlugs=x.OldSlugs }).OrderBy(x=>x.Title).ThenBy(x=>x.Id).ToListAsync();

    private string[] Problems(Recipe recipe) => RecipeAvailability.PrivatePlanningProblems(recipe)
        .Concat(Publishing && !recipe.AllergensVerified ? new[]{"allergènes vérifiés"} : []).ToArray();
    private bool AlreadyAvailable(Recipe recipe) => !Publishing && recipe.AvailablePrivately;

    private async Task Load()
    {
        CatalogOpen=await db.Settings.Select(x=>x.PublicCatalog).SingleAsync();
        var all=(await ReadImports()).Select(x=>new Item(x.Id,x.Revision,x.Title,
            Problems(x),!x.AllergensVerified,AlreadyAvailable(x))).ToList();
        Total=all.Count;
        EligibleCount=all.Count(x=>!x.Available && x.Problems.Length==0);
        IncompleteCount=all.Count(x=>x.Problems.Length>0);
        AvailableCount=all.Count(x=>x.Available);
        CatalogPage=Math.Clamp(CatalogPage,1,Pages);
        Items=all.Skip((CatalogPage-1)*50).Take(50).ToList();
    }
    public async Task OnGetAsync() => await Load();

    public async Task<IActionResult> OnPostPreviewAsync()
    {
        await Load();
        if(queue.Snapshot.IsBusy) { ModelState.AddModelError("","Attendez la fin de la synchronisation avant de préparer ce lot."); return Page(); }
        var eligible=(await ReadImports()).Where(x=>!AlreadyAvailable(x) && Problems(x).Length==0)
            .Select(x=>new Selected(x.Id,x.Revision)).ToList();
        PreviewCount=eligible.Count;
        if(PreviewCount==0) { ModelState.AddModelError("",Publishing?"Aucune fiche publiable. Consultez les motifs ci-dessous ; la vérification des allergènes doit être faite explicitement sur chaque fiche.":"Aucune nouvelle fiche complète à activer. Resynchronisez les recettes si les ingrédients ne sont pas encore préremplis."); return Page(); }
        Ticket=protector.Protect(JsonSerializer.Serialize(new Selection(User.FindFirstValue(ClaimTypes.NameIdentifier)!,eligible,Mode)),TimeSpan.FromMinutes(30));
        ModelState.Clear();
        Confirm=false;
        return Page();
    }

    public async Task<IActionResult> OnPostApplyAsync()
    {
        try
        {
            if(!Confirm) throw new InvalidOperationException("Cochez la confirmation après avoir relu l’aperçu.");
            if(queue.Snapshot.IsBusy) throw new InvalidOperationException("Attendez la fin de la synchronisation puis préparez un nouvel aperçu.");
            var selection=JsonSerializer.Deserialize<Selection>(protector.Unprotect(Ticket,out _));
            if(selection==null || selection.User!=User.FindFirstValue(ClaimTypes.NameIdentifier) || selection.Recipes.Count==0 || selection.Mode!=Mode || Mode is not ("private" or "public"))
                throw new InvalidOperationException("Aperçu invalide. Préparez un nouveau lot.");
            var current=(await ReadImports()).ToDictionary(x=>x.Id);
            if(selection.Recipes.Any(x=>!current.TryGetValue(x.Id,out var r) || r.Revision!=x.Revision || AlreadyAvailable(r) || Problems(r).Length>0))
                throw new InvalidOperationException("Des fiches ont changé depuis l’aperçu. Aucune activation effectuée ; préparez un nouvel aperçu.");
            foreach(var item in selection.Recipes)
            {
                var recipe=new Recipe { Id=item.Id, Revision=item.Revision };
                db.Attach(recipe);
                if(Publishing)
                {
                    recipe.Status="Publiée";
                    recipe.Slug=Kitchen.Slug(current[item.Id].Title);
                    var previous=current[item.Id];
                    recipe.OldSlugs=previous.OldSlugs+(previous.Slug!="" && previous.Slug!=recipe.Slug?"|"+previous.Slug:"");
                    db.Entry(recipe).Property(x=>x.Status).IsModified=true;
                    db.Entry(recipe).Property(x=>x.Slug).IsModified=true;
                    db.Entry(recipe).Property(x=>x.OldSlugs).IsModified=true;
                }
                else { recipe.AvailablePrivately=true; db.Entry(recipe).Property(x=>x.AvailablePrivately).IsModified=true; }
                recipe.Revision=Guid.NewGuid();
                db.Entry(recipe).Property(x=>x.Revision).IsModified=true;
            }
            await db.SaveChangesAsync(); // EF transaction: the entire batch succeeds or rolls back.
            TempData["Message"]=Publishing?$"{selection.Recipes.Count} recettes publiées. Le catalogue doit être ouvert au public dans Administration pour les rendre visibles.":$"{selection.Recipes.Count} recettes disponibles dans le planning privé. Aucune publication ni vérification d’allergènes ajoutée.";
            return RedirectToPage(new { Mode });
        }
        catch(DbUpdateConcurrencyException) { db.ChangeTracker.Clear(); ModelState.AddModelError("","Une fiche a changé. Aucune activation effectuée ; préparez un nouvel aperçu."); Response.StatusCode=409; }
        catch(Exception ex) when(ex is CryptographicException or JsonException or ArgumentException or InvalidOperationException)
        { ModelState.AddModelError("",ex is InvalidOperationException?ex.Message:"L’aperçu a expiré ou est invalide. Préparez un nouveau lot."); }
        Ticket="";
        ModelState.Remove(nameof(Ticket));
        await Load();
        return Page();
    }
}
