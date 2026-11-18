using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;

namespace CodEat.Web.Pages.Recipes;

public class DetailsModel(AppDb db, UserManager<AppUser> users, HouseholdService households) : PageModel
{
    public Recipe Recipe {get;private set;}=new();
    [BindProperty(SupportsGet=true)] public int Id {get;set;}
    [BindProperty(SupportsGet=true)] public DateOnly? Date {get;set;}
    [BindProperty(SupportsGet=true)] public string? ReturnUrl {get;set;}
    [BindProperty] public string Slot {get;set;}="Soir";
    [BindProperty] public int People {get;set;}=2;
    [BindProperty] public bool Leftovers {get;set;}
    public string BackUrl=>Url.IsLocalUrl(ReturnUrl)?ReturnUrl!:"/?Tab=recipes";
    private async Task<bool> Load()
    {
        var recipe=await db.Recipes.AsNoTracking().Where(RecipeAvailability.Available).SingleOrDefaultAsync(x=>x.Id==Id);
        if(recipe==null)return false;
        Recipe=recipe;Date??=Kitchen.Today;
        ViewData["Title"]=Recipe.Title;
        return true;
    }
    public async Task<IActionResult> OnGetAsync()=>await Load()?Page():NotFound();
    public async Task<IActionResult> OnPostAsync()
    {
        if(!await Load())return NotFound();
        if(!ModelState.IsValid)return Page();
        if(People is <1 or >20 || Slot is not ("Midi" or "Soir") || Date is null || Date.Value.Year is <2000 or >2100)
        {ModelState.AddModelError("","Choisissez une date, un créneau et entre 1 et 20 personnes.");return Page();}
        try
        {
            var household=(await users.GetUserAsync(User))!.HouseholdId;
            var date=Date.Value;
            var week=await households.GetWeek(household,date);
            var data=week.Data;
            var day=date.DayNumber-week.Monday.DayNumber;
            var tomorrow=Leftovers ? await households.GetWeek(household,date.AddDays(1)) : week;
            var nextData=tomorrow.Id==week.Id?data:tomorrow.Data;
            var nextDay=date.AddDays(1).DayNumber-tomorrow.Monday.DayNumber;
            if(data.Meals.Any(x=>x.Day==day && x.Slot==Slot) || Leftovers && nextData.Meals.Any(x=>x.Day==nextDay && x.Slot==Slot))
                throw new InvalidOperationException("Un repas occupe déjà l’un de ces créneaux. Choisissez une autre date ou libérez ce créneau dans le planning.");
            var preparation=new Preparation {Recipe=Recipe,Portions=People*(Leftovers?2:1),ReservedAfterWeek=tomorrow.Id!=week.Id?People:0,At=date.ToDateTime(new TimeOnly(Slot=="Midi"?10:18,0))};
            data.Preparations.Add(preparation);
            Kitchen.Assign(data,new(day,Slot,preparation.Id,People));
            if(Leftovers)
            {
                if(tomorrow.Id!=week.Id) nextData.Preparations.Add(new Preparation{Id=preparation.Id,Recipe=Recipe,Portions=preparation.Portions,AtUtc=preparation.AtUtc,Carryover=true,ReservedBeforeWeek=People});
                Kitchen.Assign(nextData,new(nextDay,Slot,preparation.Id,People));
            }
            var aliases=await db.Aliases.ToDictionaryAsync(x=>x.Alias,x=>x.Canonical);
            Kitchen.Regenerate(data,aliases);week.Data=data;week.Revision=Guid.NewGuid();
            if(tomorrow.Id!=week.Id){Kitchen.Regenerate(nextData,aliases);tomorrow.Data=nextData;tomorrow.Revision=Guid.NewGuid();}
            await db.SaveChangesAsync();
            TempData["Message"]=Leftovers?$"{Recipe.Title} : {People*2} portions à cuisiner pour {People} personnes, dont {People} portions réservées au lendemain.":$"{Recipe.Title} ajouté pour {People} personnes.";
            return RedirectToPage("/Index",new{Tab="week",Date=date.ToString("yyyy-MM-dd")});
        }
        catch(DbUpdateConcurrencyException){db.ChangeTracker.Clear();Response.StatusCode=409;ModelState.AddModelError("","Le planning a changé. Réessayez après avoir vérifié les créneaux.");}
        catch(InvalidOperationException ex){ModelState.AddModelError("",ex.Message);}
        return Page();
    }
}
