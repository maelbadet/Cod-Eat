using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using System.Text.Json;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;

namespace CodEat.Web;

public class AppUser : IdentityUser { public Guid HouseholdId { get; set; } public int MemberSlot { get; set; } = 1; }
public class Household { public Guid Id { get; set; } = Guid.NewGuid(); public string Name { get; set; } = "Mon foyer"; public string Equipment { get; set; } = "four,poêle"; }
public class Recipe
{
    public int Id { get; set; }
    public int? BookStackId { get; set; }
    public string SourceHash { get; set; } = "";
    public bool Missing { get; set; }
    public string Title { get; set; } = "";
    public string Description { get; set; } = "";
    public string Steps { get; set; } = "";
    public string Collection { get; set; } = "Terre & Mer";
    public string Category { get; set; } = "Terre";
    public string Tags { get; set; } = "";
    public decimal Servings { get; set; } = 4;
    public int? ActiveMinutes { get; set; }
    public int? CookMinutes { get; set; }
    public int? TotalMinutes { get; set; }
    public int? Kcal { get; set; }
    public string KcalSource { get; set; } = "";
    public string Allergens { get; set; } = "";
    public bool AllergensVerified { get; set; }
    public string Equipment { get; set; } = "";
    public int? FridgeHours { get; set; }
    public bool Freezable { get; set; }
    public int? FreezerDays { get; set; }
    public string Thaw { get; set; } = "";
    public string Reheat { get; set; } = "";
    public string Status { get; set; } = "Brouillon";
    public bool AvailablePrivately { get; set; }
    public string Slug { get; set; } = "";
    public string OldSlugs { get; set; } = "";
    public string IngredientsJson { get; set; } = "[]";
    public string ImportStateJson { get; set; } = "{}";
    [NotMapped, System.Text.Json.Serialization.JsonIgnore]
    public IReadOnlyList<string> ImportReviewNotes => RecipeImportState.Read(ImportStateJson).ReviewNotes;
    public Guid Revision { get; set; } = Guid.NewGuid();
    public List<Ingredient> Ingredients { get => JsonSerializer.Deserialize<List<Ingredient>>(IngredientsJson)!; set => IngredientsJson = JsonSerializer.Serialize(value); }
    public string[] MissingFields() => new[] { string.IsNullOrWhiteSpace(Title) ? "titre" : "", string.IsNullOrWhiteSpace(Steps) ? "étapes" : "", Category is not ("Terre" or "Mer" or "Terre & Mer") ? "catégorie" : "", Servings <= 0 ? "portions" : "", Ingredients.Count == 0 ? "ingrédients" : "", Kcal.HasValue && string.IsNullOrWhiteSpace(KcalSource) ? "provenance des kcal" : "", !AllergensVerified ? "allergènes vérifiés" : "" }.Where(x => x != "").ToArray();
}
public record Ingredient(string Canonical, decimal? Quantity, string Unit, string Aisle, string Comment = "");
public class Week
{
    public int Id { get; set; }
    public Guid HouseholdId { get; set; }
    public DateOnly Monday { get; set; }
    public Guid Revision { get; set; } = Guid.NewGuid();
    public string DataJson { get; set; } = "{}";
    public WeekData Data { get => JsonSerializer.Deserialize<WeekData>(DataJson)!; set => DataJson = JsonSerializer.Serialize(value); }
}
public class WeekData { public List<Preparation> Preparations { get; set; } = []; public List<Meal> Meals { get; set; } = []; public List<ShoppingLine> Shopping { get; set; } = []; }
public class Preparation { public Guid Id { get; set; } = Guid.NewGuid(); public Recipe Recipe { get; set; } = new(); public decimal Portions { get; set; } = 4; public DateTime AtUtc { get; set; } = DateTime.UtcNow; [System.Text.Json.Serialization.JsonIgnore] public DateTime At { get => TimeZoneInfo.ConvertTimeFromUtc(DateTime.SpecifyKind(AtUtc, DateTimeKind.Utc), Kitchen.Paris); set => AtUtc = TimeZoneInfo.ConvertTimeToUtc(DateTime.SpecifyKind(value, DateTimeKind.Unspecified), Kitchen.Paris); } public bool Frozen { get; set; } }
public record Meal(int Day, string Slot, Guid PreparationId, decimal Portions);
public class ShoppingLine { public string Key { get; set; } = Guid.NewGuid().ToString(); public string Name { get; set; } = ""; public string Aisle { get; set; } = "Divers"; public string Unit { get; set; } = ""; public decimal? Calculated { get; set; } public decimal? Adjusted { get; set; } public bool Checked { get; set; } public bool InStock { get; set; } public bool Manual { get; set; } public bool Changed { get; set; } public string Note { get; set; } = ""; }
public class ExportVersion { public int Id { get; set; } public Guid HouseholdId { get; set; } public int WeekId { get; set; } public int Version { get; set; } public DateTime CreatedUtc { get; set; } = DateTime.UtcNow; public string Snapshot { get; set; } = ""; public byte[] ShoppingPdf { get; set; } = []; public byte[] MealsPdf { get; set; } = []; }
public class ReminderRule { public int Id { get; set; } public Guid HouseholdId { get; set; } public string Product { get; set; } = ""; public string Unit { get; set; } = "boîte"; public decimal Quantity { get; set; } = 1; public int EveryMonths { get; set; } = 1; public decimal? WinterQuantity { get; set; } public decimal? AutumnQuantity { get; set; } public int? ExceptionMonth { get; set; } public decimal? MonthQuantity { get; set; } }
public class Reminder { public int Id { get; set; } public Guid HouseholdId { get; set; } public int RuleId { get; set; } public string Period { get; set; } = ""; public string Product { get; set; } = ""; public string Unit { get; set; } = ""; public decimal Quantity { get; set; } public string Decision { get; set; } = "En attente"; }
public class Invitation { public int Id { get; set; } public Guid HouseholdId { get; set; } public string TokenHash { get; set; } = ""; public DateTime ExpiresUtc { get; set; } public bool Used { get; set; } public Guid Revision { get; set; } = Guid.NewGuid(); }
public class AppSettings { public int Id { get; set; } = 1; public bool PublicCatalog { get; set; } public bool PublicRegistration { get; set; } }
public class IngredientAlias { public int Id { get; set; } public string Alias { get; set; } = ""; public string Canonical { get; set; } = ""; }
public class AppDb(DbContextOptions<AppDb> options) : IdentityDbContext<AppUser>(options)
{
    public DbSet<Household> Households => Set<Household>();
    public DbSet<Recipe> Recipes => Set<Recipe>();
    public DbSet<Week> Weeks => Set<Week>();
    public DbSet<ExportVersion> Exports => Set<ExportVersion>();
    public DbSet<ReminderRule> Rules => Set<ReminderRule>();
    public DbSet<Reminder> Reminders => Set<Reminder>();
    public DbSet<Invitation> Invitations => Set<Invitation>();
    public DbSet<AppSettings> Settings => Set<AppSettings>();
    public DbSet<IngredientAlias> Aliases => Set<IngredientAlias>();
    protected override void OnModelCreating(ModelBuilder b)
    {
        base.OnModelCreating(b);
        b.Entity<Recipe>().Ignore(x => x.Ingredients).HasIndex(x => x.BookStackId).IsUnique();
        b.Entity<Recipe>().Property(x => x.Revision).IsConcurrencyToken();
        b.Entity<Week>().Ignore(x => x.Data).HasIndex(x => new { x.HouseholdId, x.Monday }).IsUnique();
        b.Entity<Week>().Property(x => x.Revision).IsConcurrencyToken();
        b.Entity<Reminder>().HasIndex(x => new { x.HouseholdId, x.RuleId, x.Period }).IsUnique();
        b.Entity<ExportVersion>().HasIndex(x => new { x.WeekId, x.Version }).IsUnique();
        b.Entity<Invitation>().HasIndex(x => x.TokenHash).IsUnique();
        b.Entity<Invitation>().Property(x => x.Revision).IsConcurrencyToken();
        b.Entity<IngredientAlias>().HasIndex(x => x.Alias).IsUnique();
        b.Entity<AppUser>().HasIndex(x => new { x.HouseholdId, x.MemberSlot }).IsUnique();
        b.Entity<AppUser>().ToTable(t => t.HasCheckConstraint("CK_MemberSlot", "\"MemberSlot\" IN (1, 2)"));
        b.Entity<AppUser>().HasOne<Household>().WithMany().HasForeignKey(x => x.HouseholdId).OnDelete(DeleteBehavior.Restrict);
    }
}
