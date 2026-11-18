using CodEat.Web;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
namespace CodEat.Web.Pages;
public class IndexModel(AppDb db, UserManager<AppUser> users, HouseholdService households, PdfExport pdf) : PageModel
{
 [BindProperty(SupportsGet=true)] public string Tab {get;set;} = "week";
 [BindProperty(SupportsGet=true)] public DateOnly? Date {get;set;}
 [BindProperty(SupportsGet=true)] public string? Search {get;set;}
 [BindProperty(SupportsGet=true)] public string? Category {get;set;}
 [BindProperty(SupportsGet=true)] public int? MaxKcal {get;set;}
 [BindProperty(SupportsGet=true)] public int? MinKcal {get;set;}
 [BindProperty(SupportsGet=true)] public string? Protein {get;set;}
 [BindProperty(SupportsGet=true)] public int PageSize {get;set;}=12;
 [BindProperty(SupportsGet=true)] public string Sort {get;set;}="popular";
 public record PopularRecipe(int Id,string Title,int Count);
 public List<PopularRecipe> PopularRecipes {get;private set;}=[];
 public Dictionary<string,int> ProteinOptions {get;private set;}=[];
 public int CalorieCeiling {get;private set;}=1200;
 [BindProperty(SupportsGet=true)] public int? MaxActive {get;set;}
 [BindProperty(SupportsGet=true)] public int? MaxTotal {get;set;}
 [BindProperty(SupportsGet=true)] public string? Include {get;set;}
 [BindProperty(SupportsGet=true)] public string? Exclude {get;set;}
 [BindProperty(SupportsGet=true)] public string? Allergens {get;set;}
 [BindProperty(SupportsGet=true)] public bool EquipmentFilter {get;set;}
 public Week Week {get;set;} = new();
 public WeekData Data {get;set;} = new();
 public List<Recipe> Recipes {get;set;} = [];
 public int DraftRecipeCount { get; private set; }
 public int ReadyRecipeCount { get; private set; }
 [BindProperty(SupportsGet=true)] public int CatalogPage { get; set; } = 1;
 public int MatchingRecipeCount { get; private set; }
 public int CatalogPages => Math.Max(1,(MatchingRecipeCount+PageSize-1)/PageSize);
 public Dictionary<string,string> CatalogRoutes(int page) => new() {
 ["Tab"]="recipes", ["CatalogPage"]=page.ToString(), ["Date"]=Week.Monday.ToString("yyyy-MM-dd"),
 ["Search"]=Search??"", ["Category"]=Category??"", ["MaxKcal"]=MaxKcal?.ToString()??"",
 ["MinKcal"]=MinKcal?.ToString()??"", ["Protein"]=Protein??"", ["PageSize"]=PageSize.ToString(), ["Sort"]=Sort,
 ["MaxActive"]=MaxActive?.ToString()??"", ["MaxTotal"]=MaxTotal?.ToString()??"",
 ["Include"]=Include??"", ["Exclude"]=Exclude??"", ["Allergens"]=Allergens??"", ["EquipmentFilter"]=EquipmentFilter.ToString() };
 public string EmptyRecipeMessage => DraftRecipeCount > 0 && ReadyRecipeCount == 0
     ? $"Vos recettes sont importées : {DraftRecipeCount} fiche(s) en brouillon attendent leur validation."
     : DraftRecipeCount + ReadyRecipeCount == 0
         ? "Aucune recette n’a encore été importée ou créée."
         : "Aucune recette ne correspond à ces filtres. Essayez de les élargir ou de les effacer.";
 public List<Reminder> Reminders {get;set;} = [];
 public List<ReminderRule> Rules {get;set;} = [];
 public List<ExportVersion> Versions {get;set;} = [];
 public Household Household {get;set;} = new();
 public string CopyText {get;set;} = "";
 public async Task OnGetAsync() => await Load();
 public async Task<IActionResult> OnGetRecipeResultsAsync() { Tab="recipes"; await Load(); return Partial("_RecipeResults",this); }
 async Task<Guid> HouseholdId() => (await users.GetUserAsync(User))!.HouseholdId;
 async Task Load() {
 var id = await HouseholdId(); Household = await db.Households.SingleAsync(x=>x.Id==id);
 Week=await households.GetWeek(id, Date ?? Kitchen.Today); Data=Week.Data;
 PageSize=RecipeDiscovery.PageSize(PageSize);
 var availableRecipes = await db.Recipes.AsNoTracking().Where(RecipeAvailability.Available).OrderBy(x=>x.Category).ThenBy(x=>x.Title).ThenBy(x=>x.Id)
 .Select(x=>new Recipe {Id=x.Id,Title=x.Title,Category=x.Category,Servings=x.Servings,Kcal=x.Kcal,
 ActiveMinutes=x.ActiveMinutes,TotalMinutes=x.TotalMinutes,Allergens=x.Allergens,AllergensVerified=x.AllergensVerified,
 Equipment=x.Equipment,IngredientsJson=x.IngredientsJson}).ToListAsync();
 ReadyRecipeCount = availableRecipes.Count;
 CalorieCeiling=(int)Math.Min(int.MaxValue,((Math.Max(1200L,Math.Max(availableRecipes.Max(x=>(long?)x.Kcal)??0,Math.Max((long)MinKcal.GetValueOrDefault(),MaxKcal.GetValueOrDefault())))+99)/100)*100);
 DraftRecipeCount = await db.Recipes.CountAsync(x=>x.Status=="Brouillon" && !x.AvailablePrivately);
 var proteins=availableRecipes.ToDictionary(x=>x.Id,RecipeDiscovery.ProteinKinds);
 ProteinOptions=proteins.Values.SelectMany(x=>x).GroupBy(x=>x).OrderBy(x=>x.Key).ToDictionary(x=>x.Key,x=>x.Count());
 var popularity=Data.Meals.GroupBy(x=>Data.Preparations.Single(p=>p.Id==x.PreparationId).Recipe.Id).ToDictionary(x=>x.Key,x=>x.Count());
 PopularRecipes=availableRecipes.Where(x=>popularity.ContainsKey(x.Id)).OrderByDescending(x=>popularity[x.Id]).ThenBy(x=>x.Title)
 .Take(6).Select(x=>new PopularRecipe(x.Id,x.Title,popularity[x.Id])).ToList();
 var matching=availableRecipes.Where(new RecipeFilter(Search,Category,MaxKcal,MaxActive,MaxTotal,Include,Exclude,Allergens,EquipmentFilter?Household.Equipment:null).Matches)
 .Where(x=>(MinKcal==null || x.Kcal.HasValue && x.Kcal>=MinKcal) && (string.IsNullOrEmpty(Protein)||proteins[x.Id].Contains(Protein))).ToList();
 matching=(Sort switch {
 "title"=>matching.OrderBy(x=>x.Title).ThenBy(x=>x.Id),
 "kcal"=>matching.OrderBy(x=>x.Kcal==null).ThenBy(x=>x.Kcal).ThenBy(x=>x.Title),
 "quick"=>matching.OrderBy(x=>x.TotalMinutes==null).ThenBy(x=>x.TotalMinutes).ThenBy(x=>x.Title),
 _=>matching.OrderByDescending(x=>popularity.GetValueOrDefault(x.Id)).ThenBy(x=>x.Title).ThenBy(x=>x.Id)
 }).ToList();
 MatchingRecipeCount=matching.Count;
 CatalogPage=Math.Clamp(CatalogPage,1,CatalogPages);
 var selected=Tab=="recipes" ? matching.Skip((CatalogPage-1)*PageSize).Take(PageSize).ToList()
 : matching.GroupBy(x=>x.Category).SelectMany(g=>g.Select((r,i)=>new{Recipe=r,Rank=i})).OrderBy(x=>x.Rank).ThenBy(x=>x.Recipe.Category).Take(50).Select(x=>x.Recipe).ToList();
 var selectedIds=selected.Select(x=>x.Id).ToArray();
 var full=await db.Recipes.AsNoTracking().Where(x=>selectedIds.Contains(x.Id)).ToDictionaryAsync(x=>x.Id);
 Recipes=selected.Select(x=>full[x.Id]).ToList();
 await households.EnsureReminders(id,Week.Monday);
 Reminders=await db.Reminders.Where(x=>x.HouseholdId==id && x.Decision=="En attente").ToListAsync();
 Rules=await db.Rules.Where(x=>x.HouseholdId==id).ToListAsync(); Versions=await db.Exports.Where(x=>x.HouseholdId==id&&x.WeekId==Week.Id).OrderByDescending(x=>x.Version).ToListAsync(); CopyText=PdfExport.ShoppingText(Data,Reminders);
 }
 public async Task<IActionResult> OnGetPdfAsync(int id, string kind) { var h=await HouseholdId(); var export=await db.Exports.SingleOrDefaultAsync(x=>x.Id==id&&x.HouseholdId==h); if(export==null)return NotFound(); return File(kind=="shopping"?export.ShoppingPdf:export.MealsPdf,"application/pdf",$"codeat-{kind}-v{export.Version}.pdf"); }
 public async Task<IActionResult> OnPostAsync(string action, Guid revision, int recipeId, Guid preparationId, int day, string slot, decimal portions, decimal prepared, DateTime at, bool frozen, string? key, decimal? quantity, string? name, string? unit, string? aisle, string? decision, int reminderId, string? equipment, int cadence=1, decimal? winter=null, decimal? autumn=null, int? month=null, decimal? monthQuantity=null, bool confirm=false, bool bought=false) {
 await Load();
 if(!ModelState.IsValid) return Page();
 try {
 if(Week.Revision!=revision)throw new DbUpdateConcurrencyException();
 switch(action) {
 case "assign":
 if(preparationId==Guid.Empty) { var recipe=await db.Recipes.Where(RecipeAvailability.Available).SingleOrDefaultAsync(x=>x.Id==recipeId); if(recipe==null) return NotFound(); if(prepared<=0||portions<=0)throw new InvalidOperationException("Indiquez des portions positives."); var p=new Preparation{Recipe=recipe,Portions=prepared,At=at==default?Week.Monday.AddDays(-1).ToDateTime(new TimeOnly(16,0)):DateTime.SpecifyKind(at,DateTimeKind.Unspecified),Frozen=frozen}; Data.Preparations.Add(p); preparationId=p.Id; }
 Kitchen.Assign(Data,new(day,slot,preparationId,portions)); break;
 case "remove": Data.Meals.RemoveAll(x=>x.Day==day&&x.Slot==slot); break;
 case "move": var moved=Data.Meals.Single(x=>x.PreparationId==preparationId&&x.Day==int.Parse(key!)&&x.Slot==decision); Data.Meals.Remove(moved); Kitchen.Assign(Data,moved with{Day=day,Slot=slot}); break;
 case "prepare": var prep=Data.Preparations.Single(x=>x.Id==preparationId); if(prep.Carryover || prep.ReservedAfterWeek>0)throw new InvalidOperationException("Ce lot réserve des portions entre deux semaines. Ses quantités et sa date de préparation sont conservées pour garder les restes cohérents."); if(prepared<=0||prepared<Data.Meals.Where(x=>x.PreparationId==prep.Id).Sum(x=>x.Portions)+prep.ReservedBeforeWeek+prep.ReservedAfterWeek)throw new InvalidOperationException("Le lot ne peut pas être inférieur aux portions affectées."); prep.Portions=prepared; prep.At=DateTime.SpecifyKind(at,DateTimeKind.Unspecified); prep.Frozen=frozen; break;
 case "suggest":
 if(Recipes.Count==0)throw new InvalidOperationException(EmptyRecipeMessage);
 var index=0; var varied=Recipes.GroupBy(r=>r.Category).SelectMany(g=>g.Select((r,i)=>new{Recipe=r,Rank=i})).OrderBy(x=>x.Rank).ThenBy(x=>x.Recipe.Category).Select(x=>x.Recipe).ToList(); for(var d=0;d<7;d++)foreach(var s in new[]{"Midi","Soir"}) {if(Data.Meals.Any(x=>x.Day==d&&x.Slot==s))continue; var r=varied[index++%varied.Count]; var p=new Preparation{Recipe=r,Portions=2,At=Week.Monday.AddDays(-1).ToDateTime(new TimeOnly(16,0))}; Data.Preparations.Add(p); Kitchen.Assign(Data,new(d,s,p.Id,2));} break;
 case "line": var line=Data.Shopping.Single(x=>x.Key==key); if(quantity<0)throw new InvalidOperationException("Quantité positive requise."); line.Adjusted=quantity; line.Checked=decision==null?bought:decision=="acheté"; line.InStock=decision=="stock"; line.Changed=false; break;
 case "manual": if(string.IsNullOrWhiteSpace(name)||quantity<0)throw new InvalidOperationException("Produit et quantité valides requis."); Data.Shopping.Add(new(){Name=name,Calculated=quantity,Unit=unit??"",Aisle=aisle??"Divers",Manual=true}); break;
 case "reminder":
 if(decision is not ("Ajouter" or "Stock" or "Reporter"))return BadRequest();
 var reminder=await db.Reminders.SingleOrDefaultAsync(x=>x.Id==reminderId&&x.HouseholdId==Household.Id); if(reminder==null)return NotFound();
 if(reminder.Decision=="En attente") {if(decision=="Ajouter"&&!Data.Shopping.Any(x=>x.Key=="reminder:"+reminder.Id))Data.Shopping.Add(new(){Key="reminder:"+reminder.Id,Name=reminder.Product,Calculated=reminder.Quantity,Unit=reminder.Unit,Manual=true}); reminder.Decision=decision;} break;
 case "equipment": Household.Equipment=equipment??""; break;
 case "rule": if(string.IsNullOrWhiteSpace(name)||quantity<=0||cadence<1||cadence>12||month is <1 or >12)throw new InvalidOperationException("Règle invalide."); db.Rules.Add(new(){HouseholdId=Household.Id,Product=name,Unit=unit??"",Quantity=quantity??1,EveryMonths=cadence,WinterQuantity=winter,AutumnQuantity=autumn,ExceptionMonth=month,MonthQuantity=monthQuantity}); break;
 case "delete-rule": var rule=await db.Rules.SingleOrDefaultAsync(x=>x.Id==reminderId&&x.HouseholdId==Household.Id); if(rule==null)return NotFound(); db.Rules.Remove(rule); break;
 case "finalize":
 Kitchen.Regenerate(Data,await db.Aliases.ToDictionaryAsync(x=>x.Alias,x=>x.Canonical));
 if(!confirm && (Reminders.Any()||Data.Shopping.Any(x=>x.Changed||x.Note!="")||Data.Preparations.Any(x=>!x.Recipe.AllergensVerified)))throw new InvalidOperationException("Des éléments restent à vérifier, notamment les allergènes des recettes importées. Cochez la confirmation pour les inclure dans l’export.");
 var version=Versions.Count==0?1:Versions.Max(x=>x.Version)+1;
 db.Exports.Add(new(){HouseholdId=Household.Id,WeekId=Week.Id,Version=version,Snapshot=System.Text.Json.JsonSerializer.Serialize(Data),ShoppingPdf=pdf.CreateShopping(Data,Reminders,Week.Monday),MealsPdf=pdf.CreateMeals(Data,Week.Monday)}); break;
 case "invite":
 var token=Convert.ToHexString(System.Security.Cryptography.RandomNumberGenerator.GetBytes(32)); db.Invitations.Add(new(){HouseholdId=Household.Id,TokenHash=Account.RegisterModel.Hash(token),ExpiresUtc=System.DateTime.UtcNow.AddDays(2)}); TempData["Invitation"]=$"{Seo.Origin(HttpContext.RequestServices.GetRequiredService<IConfiguration>())}/Account/Register?invite={token}"; break;
 default: return BadRequest();
 }
 Data.Preparations.RemoveAll(x=>x.ReservedAfterWeek==0 && !Data.Meals.Any(m=>m.PreparationId==x.Id)); Kitchen.Regenerate(Data,await db.Aliases.ToDictionaryAsync(x=>x.Alias,x=>x.Canonical)); await households.Save(Week,revision,Data);
 TempData["Message"]="Modifications enregistrées."; return RedirectToPage(new{Tab,Date=Week.Monday.ToString("yyyy-MM-dd")});
 } catch(DbUpdateException) {db.ChangeTracker.Clear(); ModelState.AddModelError("","Une autre modification a été enregistrée. Rechargez la page avant de réessayer.");Response.StatusCode=409;} catch(InvalidOperationException ex){ModelState.AddModelError("",ex.Message);} catch(ArgumentException){ModelState.AddModelError("","Date ou heure invalide dans le fuseau Europe/Paris.");} 
 return Page();
 }
}





