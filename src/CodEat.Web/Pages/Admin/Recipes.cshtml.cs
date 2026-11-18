using CodEat.Web;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using System.Globalization;
namespace CodEat.Web.Pages.Admin;
public class RecipesModel(AppDb db,BookStackSyncQueue syncQueue) : PageModel {
 public List<Recipe> Recipes {get;set;}=[];
 [BindProperty(SupportsGet=true)] public string? StatusFilter { get; set; }
 public int DraftCount { get; private set; }
 [BindProperty(SupportsGet=true)] public int CatalogPage { get; set; } = 1;
 [BindProperty(SupportsGet=true)] public string? Search { get; set; }
 public int CatalogCount { get; private set; }
 public int CatalogPages => Math.Max(1,(CatalogCount+49)/50);
 [BindProperty] public Recipe Input {get;set;}=new();
 [BindProperty] public string IngredientsText {get;set;}="";
 public AppSettings Settings {get;set;}=new();
 public BookStackSyncStatus SyncStatus => syncQueue.Snapshot;
 public async Task OnGetAsync(int? id)
 {
     DraftCount = await db.Recipes.CountAsync(x => x.Status == "Brouillon");
     var catalog = db.Recipes.AsNoTracking();
     if (StatusFilter == "draft") catalog = catalog.Where(x => x.Status == "Brouillon");
     if (!string.IsNullOrWhiteSpace(Search)) catalog = catalog.Where(x => x.Title.ToLower().Contains(Search.ToLower()));
     CatalogCount = await catalog.CountAsync();
     CatalogPage = Math.Clamp(CatalogPage,1,CatalogPages);
     Recipes = await catalog.OrderBy(x => x.Title).ThenBy(x=>x.Id).Skip((CatalogPage-1)*50).Take(50)
         .Select(x => new Recipe { Id = x.Id, Title = x.Title, Status = x.Status, Missing = x.Missing, AvailablePrivately=x.AvailablePrivately }).ToListAsync();
     Settings = await db.Settings.SingleAsync();
     if (id.HasValue)
     {
         Input = await db.Recipes.SingleAsync(x => x.Id == id);
         IngredientsText = string.Join('\n', Input.Ingredients.Select(x => $"{x.Canonical}|{x.Quantity?.ToString(CultureInfo.InvariantCulture)}|{x.Unit}|{x.Aisle}|{x.Comment}"));
     }
 }
 public IActionResult OnPostSync()
 {
     TempData["Message"] = syncQueue.TryEnqueue()
         ? "Synchronisation lancée en arrière-plan. Vous pouvez suivre son avancement ci-dessous."
         : "Une synchronisation est déjà en cours. Son avancement est affiché ci-dessous.";
     return RedirectToPage();
 }
 public IActionResult OnGetSyncStatus()
 {
     Response.Headers.CacheControl = "no-store, private";
     return new JsonResult(syncQueue.Snapshot);
 }
 public async Task<IActionResult> OnPostSettingsAsync(bool catalog,bool registration){if(registration&&string.IsNullOrWhiteSpace(HttpContext.RequestServices.GetRequiredService<IConfiguration>()["Smtp:Host"])){TempData["Message"]="Configurez SMTP avant d’ouvrir les inscriptions.";return RedirectToPage();}var settings=await db.Settings.SingleAsync();settings.PublicCatalog=catalog;settings.PublicRegistration=registration;await db.SaveChangesAsync();return RedirectToPage();}
 public async Task<IActionResult> OnPostAliasAsync(string alias,string canonical){if(string.IsNullOrWhiteSpace(alias)||string.IsNullOrWhiteSpace(canonical))return BadRequest();alias=Kitchen.Normalize(alias);canonical=Kitchen.Normalize(canonical);var existing=await db.Aliases.SingleOrDefaultAsync(x=>x.Alias==alias);if(existing==null)db.Aliases.Add(new(){Alias=alias,Canonical=canonical});else existing.Canonical=canonical;await db.SaveChangesAsync();TempData["Message"]="Alias enregistré. Régénérez les courses en enregistrant votre semaine.";return RedirectToPage();}
 public async Task<IActionResult> OnPostAsync(string action){
 try{
 if(!ModelState.IsValid)throw new InvalidOperationException("Vérifiez les valeurs des champs du formulaire.");
 foreach(var property in typeof(Recipe).GetProperties().Where(p=>p.PropertyType==typeof(string)&&p.CanWrite)){if(property.GetValue(Input)==null)property.SetValue(Input,"");}
 var ingredients=new List<Ingredient>();foreach(var line in (IngredientsText ?? string.Empty).Split('\n',StringSplitOptions.TrimEntries|StringSplitOptions.RemoveEmptyEntries)){var parts=line.Split('|');if(parts.Length<4)throw new InvalidOperationException("Chaque ingrédient doit suivre le format indiqué.");decimal? quantity=string.IsNullOrWhiteSpace(parts[1])?null:decimal.Parse(parts[1].Replace(',','.'),CultureInfo.InvariantCulture);if(quantity<0||string.IsNullOrWhiteSpace(parts[0]))throw new InvalidOperationException("Ingrédient ou quantité invalide.");ingredients.Add(new(Kitchen.Normalize(parts[0]),quantity,parts[2].Trim(),parts[3].Trim(),parts.Length>4?parts[4].Trim():""));}
 if(Input.Servings<0||Input.ActiveMinutes<0||Input.CookMinutes<0||Input.TotalMinutes<0||Input.Kcal<0||Input.FridgeHours<0||Input.FreezerDays<0||Input.Category!="" && Input.Category is not("Terre" or "Mer" or "Terre & Mer"))throw new InvalidOperationException("Vérifiez les portions, durées et catégorie.");
 Input.Ingredients=ingredients;
 var target=Input.Id==0?new Recipe():await db.Recipes.SingleAsync(x=>x.Id==Input.Id);
 if(Input.Id!=0&&target.Revision!=Input.Revision)throw new DbUpdateConcurrencyException();
 if(target.BookStackId!=null){Input.Title=target.Title;Input.Steps=target.Steps;}
 if(action is "validate" or "publish"&&Input.MissingFields().Length>0)throw new InvalidOperationException("À renseigner : "+string.Join(", ",Input.MissingFields()));
 var importState = RecipeImportMerge.RememberLocalEdits(target, Input);
 var privateAvailability=target.AvailablePrivately;
 var oldSlug=target.Slug;var book=target.BookStackId;var hash=target.SourceHash;var missing=target.Missing;var oldSlugs=target.OldSlugs;
 db.Entry(target).CurrentValues.SetValues(Input);target.BookStackId=book;target.SourceHash=hash;target.ImportStateJson=importState;target.Missing=missing;target.OldSlugs=oldSlugs;target.Revision=Guid.NewGuid();target.Status=action=="publish"?"Publiée":action=="validate"?"Validée":"Brouillon";
 target.AvailablePrivately=privateAvailability && RecipeAvailability.PrivatePlanningProblems(target).Length==0;
 if(action=="publish"){var slug=Kitchen.Slug(target.Title);if(oldSlug!=""&&oldSlug!=slug)target.OldSlugs=oldSlugs+"|"+oldSlug;target.Slug=slug;}
 if(target.Id==0)db.Recipes.Add(target);await db.SaveChangesAsync();TempData["Message"]="Fiche enregistrée : "+target.Status;return RedirectToPage(new{id=target.Id});
 }catch(DbUpdateConcurrencyException){ModelState.AddModelError("","Fiche modifiée par une autre personne. Rechargez la page.");Response.StatusCode=409;}catch(Exception ex)when(ex is InvalidOperationException or FormatException){ModelState.AddModelError("",ex.Message);}
 Recipes=await db.Recipes.AsNoTracking().OrderBy(x=>x.Title).Take(50).Select(x=>new Recipe{Id=x.Id,Title=x.Title,Status=x.Status}).ToListAsync();Settings=await db.Settings.SingleAsync();
 if(Input.Id != 0)
 {
     var metadata = await db.Recipes.AsNoTracking().Where(x => x.Id == Input.Id)
         .Select(x => new { x.BookStackId, x.ImportStateJson }).SingleOrDefaultAsync();
     if(metadata != null) { Input.BookStackId = metadata.BookStackId; Input.ImportStateJson = metadata.ImportStateJson; }
 }
 return Page();
 }
}
