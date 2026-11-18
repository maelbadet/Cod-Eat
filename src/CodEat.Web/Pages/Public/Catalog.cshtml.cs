using CodEat.Web;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using System.Text.Json;
namespace CodEat.Web.Pages.Public;
public class CatalogModel(AppDb db,IConfiguration config):PageModel {
 public List<Recipe> Recipes {get;set;}=[];
 public Recipe? Recipe {get;set;}
 public string? JsonLd {get;set;}
 [BindProperty(SupportsGet=true)] public int CatalogPage {get;set;}=1;
 [BindProperty(SupportsGet=true)] public string? Search {get;set;}
 public int Total {get;private set;}
 public int Pages=>Math.Max(1,(Total+23)/24);
 public async Task<IActionResult> OnGetAsync(int? id,string? slug){
 Response.Headers.CacheControl="no-cache";
 if(!(await db.Settings.SingleAsync()).PublicCatalog){Response.Headers["X-Robots-Tag"]="noindex";return NotFound();}
 if(id.HasValue){Recipe=await db.Recipes.SingleOrDefaultAsync(x=>x.Id==id&&x.Status=="Publiée");if(Recipe==null){Response.Headers["X-Robots-Tag"]="noindex";return NotFound();}
 var canonical=$"{Seo.Origin(config)}/catalogue/{Recipe.Id}/{Recipe.Slug}";
 if(slug!=Recipe.Slug){if(Recipe.OldSlugs.Split('|').Contains(slug))return RedirectPermanent(canonical);return NotFound();}
 ViewData["Title"]=Recipe.Title;ViewData["Description"]=string.IsNullOrWhiteSpace(Recipe.Description)?$"Ingrédients et étapes de {Recipe.Title}, pour {Recipe.Servings} portions.":Recipe.Description;ViewData["Canonical"]=canonical;ViewData["Recipe"]=true;
 if(Recipe.MissingFields().Length==0){var data=new Dictionary<string,object>{{"@context","https://schema.org"},{"@type","Recipe"},{"name",Recipe.Title},{"description",ViewData["Description"]!},{"recipeYield",$"{Recipe.Servings} portions"},{"recipeIngredient",Recipe.Ingredients.Select(x=>$"{x.Quantity} {x.Unit} {x.Canonical} {x.Comment}".Trim()).ToArray()},{"recipeInstructions",Recipe.Steps.Split('\n',StringSplitOptions.RemoveEmptyEntries).Select(x=>new Dictionary<string,string>{{"@type","HowToStep"},{"text",x}}).ToArray()}};
 if(Recipe.ActiveMinutes is int active)data["prepTime"]=$"PT{active}M";if(Recipe.CookMinutes is int cook)data["cookTime"]=$"PT{cook}M";if(Recipe.TotalMinutes is int total)data["totalTime"]=$"PT{total}M";if(Recipe.Kcal is int kcal)data["nutrition"]=new Dictionary<string,object>{{"@type","NutritionInformation"},{"calories",$"{kcal} kcal"}};JsonLd=JsonSerializer.Serialize(data);}
 }else{
 var query=db.Recipes.AsNoTracking().Where(x=>x.Status=="Publiée");
 if(!string.IsNullOrWhiteSpace(Search))query=query.Where(x=>x.Title.ToLower().Contains(Search.ToLower()));
 Total=await query.CountAsync();CatalogPage=Math.Clamp(CatalogPage,1,Pages);
 Recipes=await query.OrderBy(x=>x.Title).ThenBy(x=>x.Id).Skip((CatalogPage-1)*24).Take(24)
 .Select(x=>new Recipe{Id=x.Id,Title=x.Title,Slug=x.Slug,Description=x.Description,Category=x.Category,Servings=x.Servings,TotalMinutes=x.TotalMinutes}).ToListAsync();
 ViewData["Title"]="Terre & Mer : les recettes";ViewData["Description"]="Découvrez les recettes Terre & Mer de Cod & Eat : ingrédients, portions et étapes pour préparer vos repas.";ViewData["Canonical"]=Seo.Origin(config)+"/catalogue";}
 if(Request.QueryString.HasValue)Response.Headers["X-Robots-Tag"]="noindex, follow";
 return Page();
 }
}

