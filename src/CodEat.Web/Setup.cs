using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using System.Net;
using System.Net.Mail;
namespace CodEat.Web;
public static class Seo { public static string Origin(IConfiguration config) { var value = config["Public:Origin"] ?? "https://localhost:7100"; if (!Uri.TryCreate(value, UriKind.Absolute, out var uri) || uri.Scheme != "https" || uri.AbsolutePath != "/" || !string.IsNullOrEmpty(uri.Query)) throw new InvalidOperationException("Public:Origin doit être une origine HTTPS."); return uri.GetLeftPart(UriPartial.Authority); } }
public class MailService(IConfiguration config)
{
    public async Task Send(string email, string subject, string text)
    {
        using var client = new SmtpClient(config["Smtp:Host"] ?? "localhost", config.GetValue<int?>("Smtp:Port") ?? 1025) { EnableSsl = config.GetValue<bool>("Smtp:Tls") };
        if (!string.IsNullOrEmpty(config["Smtp:User"])) client.Credentials = new NetworkCredential(config["Smtp:User"], config["Smtp:Password"]);
        using var message = new MailMessage(config["Smtp:From"] ?? "codeat@localhost.test", email, subject, text); await client.SendMailAsync(message);
    }
}
public static class Setup
{
    public static async Task Bootstrap(IServiceProvider services, IConfiguration config)
    {
        var db = services.GetRequiredService<AppDb>(); var users = services.GetRequiredService<UserManager<AppUser>>(); var roles = services.GetRequiredService<RoleManager<IdentityRole>>();
        if (await users.Users.AnyAsync()) throw new InvalidOperationException("Initialisation déjà effectuée.");
        await using var transaction = await db.Database.BeginTransactionAsync();
        var email = config["Bootstrap:Email"] ?? throw new InvalidOperationException("Bootstrap__Email requis"); var password = config["Bootstrap:Password"] ?? throw new InvalidOperationException("Bootstrap__Password requis");
        var household = new Household(); db.Households.Add(household); await db.SaveChangesAsync();
        await roles.CreateAsync(new IdentityRole("Admin"));
        var user = new AppUser { UserName = email, Email = email, HouseholdId = household.Id, EmailConfirmed = true };
        var result = await users.CreateAsync(user, password); if (!result.Succeeded) throw new InvalidOperationException(string.Join("; ", result.Errors.Select(x => x.Description)));
        await users.AddToRoleAsync(user, "Admin");
        if (config["Bootstrap:SecondEmail"] is string second && config["Bootstrap:SecondPassword"] is string secondPassword) { var res = await users.CreateAsync(new AppUser { UserName = second, Email = second, HouseholdId = household.Id, MemberSlot = 2, EmailConfirmed = true }, secondPassword); if (!res.Succeeded) throw new InvalidOperationException(string.Join("; ", res.Errors.Select(x => x.Description))); }
        await transaction.CommitAsync();
        Console.WriteLine("Foyer et comptes initialisés.");
    }
    public static async Task Demo(IServiceProvider services)
    {
        var db = services.GetRequiredService<AppDb>();
        if (await db.Recipes.AnyAsync()) return;
        var fish = new Recipe { Title = "Cabillaud citron & pommes de terre", Description = "Un plat simple de la collection Terre & Mer, à préparer pour deux repas.", Steps = "1. Laver et couper les pommes de terre.\n2. Les cuire au four avec l’huile.\n3. Ajouter le cabillaud et le citron, poursuivre jusqu’à cuisson complète.", Category = "Mer", Servings = 4, ActiveMinutes = 15, CookMinutes = 30, TotalMinutes = 45, Kcal = 420, KcalSource = "estimée", Allergens = "poisson", AllergensVerified = true, Equipment = "four,économe", FridgeHours = 48, Freezable = true, FreezerDays = 30, Thaw = "Décongeler au réfrigérateur avant utilisation.", Reheat = "Réchauffer à cœur et servir immédiatement.", Status = "Publiée", Slug = "cabillaud-citron-pommes-de-terre", Ingredients = [new("cabillaud", 600, "g", "Poissonnerie"), new("pommes de terre", 1, "kg", "Fruits et légumes"), new("citron", 1, "pièce", "Fruits et légumes"), new("sel", null, "", "Épicerie", "selon le goût")] };
        db.Recipes.AddRange(fish, new Recipe { Title = "Lentilles aux carottes", Description = "Une préparation végétale du quotidien.", Steps = "Rincer les lentilles. Couper les carottes. Cuire ensemble et assaisonner.", Servings = 4, ActiveMinutes = 15, CookMinutes = 35, TotalMinutes = 50, AllergensVerified = true, Equipment = "casserole", FridgeHours = 72, Status = "Validée", Ingredients = [new("lentilles", 300, "g", "Épicerie"), new("carottes", 400, "g", "Fruits et légumes")] });
        await db.SaveChangesAsync();
        foreach (var household in await db.Households.ToListAsync())
        {
            var monday = Kitchen.Monday(Kitchen.Today); var data = new WeekData(); var p = new Preparation { Recipe = fish, Portions = 4, At = monday.AddDays(-1).ToDateTime(new TimeOnly(16, 0)) }; data.Preparations.Add(p); Kitchen.Assign(data, new(0, "Midi", p.Id, 2)); Kitchen.Assign(data, new(1, "Soir", p.Id, 2)); Kitchen.Regenerate(data);
            db.Weeks.Add(new Week { HouseholdId = household.Id, Monday = monday, Data = data });
            db.Rules.AddRange(new ReminderRule { HouseholdId = household.Id, Product = "Mouchoirs", Quantity = 1, WinterQuantity = 2 }, new ReminderRule { HouseholdId = household.Id, Product = "Papier toilette", Unit = "paquet", EveryMonths = 1, Quantity = 1, AutumnQuantity = 2 });
        }
        await db.SaveChangesAsync();
    }
}
