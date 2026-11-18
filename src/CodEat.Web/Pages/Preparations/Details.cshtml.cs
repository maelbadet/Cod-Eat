using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;

namespace CodEat.Web.Pages.Preparations;

public class DetailsModel(AppDb db,UserManager<AppUser> users):PageModel
{
    [BindProperty(SupportsGet=true)] public int WeekId {get;set;}
    [BindProperty(SupportsGet=true)] public Guid PreparationId {get;set;}
    [BindProperty(SupportsGet=true)] public string From {get;set;}="week";
    public Week Week {get;private set;}=new();
    public WeekData Data {get;private set;}=new();
    public Preparation Preparation {get;private set;}=new();
    public List<Ingredient> Ingredients=>RecipeQuantities.For(Preparation.Recipe,Preparation.Portions);
    public async Task<IActionResult> OnGetAsync()
    {
        var household=(await users.GetUserAsync(User))!.HouseholdId;
        var week=await db.Weeks.AsNoTracking().SingleOrDefaultAsync(x=>x.Id==WeekId && x.HouseholdId==household);
        if(week==null)return NotFound();
        var data=week.Data;var preparation=data.Preparations.SingleOrDefault(x=>x.Id==PreparationId);
        if(preparation==null)return NotFound();
        Week=week;Data=data;Preparation=preparation;
        ViewData["Title"]=preparation.Recipe.Title;
        return Page();
    }
}
