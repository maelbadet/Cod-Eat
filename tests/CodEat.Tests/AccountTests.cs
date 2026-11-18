using CodEat.Web;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using System.Net;
using Xunit;
namespace CodEat.Tests;
public partial class IntegrationTests {
 [Fact] public async Task PublicSignupAndSingleUseExpiringInvitation(){await using var app=new AppFactory();await app.Initialize();using var c=app.Client();Assert.Equal(HttpStatusCode.NotFound,(await c.GetAsync("/Account/Register")).StatusCode);string valid="test-invitation",expired="expired-invitation";Guid householdId;
 using(var scope=app.Services.CreateScope()){var db=scope.ServiceProvider.GetRequiredService<AppDb>();householdId=(await db.Households.SingleAsync(x=>x.Name=="B")).Id;db.Invitations.AddRange(new(){HouseholdId=householdId,TokenHash=CodEat.Web.Pages.Account.RegisterModel.Hash(valid),ExpiresUtc=DateTime.UtcNow.AddHours(1)},new(){HouseholdId=householdId,TokenHash=CodEat.Web.Pages.Account.RegisterModel.Hash(expired),ExpiresUtc=DateTime.UtcNow.AddHours(-1)});await db.SaveChangesAsync();}
 async Task<HttpResponseMessage> Register(string email,string? invite){var route="/Account/Register"+(invite==null?"":"?invite="+invite);var html=await c.GetStringAsync(route);return await c.PostAsync(route,new FormUrlEncodedContent(new Dictionary<string,string>{{"__RequestVerificationToken",AppFactory.Token(html)},{"Email",email},{"Password","Test!Password123"},{"Invite",invite??""}}));}
 Assert.Equal(HttpStatusCode.OK,(await Register("expired@example.test",expired)).StatusCode);
 Assert.Equal(HttpStatusCode.Redirect,(await Register("invited@example.test",valid)).StatusCode);
 Assert.Equal(HttpStatusCode.OK,(await Register("reused@example.test",valid)).StatusCode);
 using(var scope=app.Services.CreateScope()){var db=scope.ServiceProvider.GetRequiredService<AppDb>();Assert.Equal(2,await db.Users.CountAsync(x=>x.HouseholdId==householdId));Assert.False(await db.Users.AnyAsync(x=>x.Email=="reused@example.test"||x.Email=="expired@example.test"));var settings=await db.Settings.SingleAsync();settings.PublicRegistration=true;await db.SaveChangesAsync();}
 Assert.Equal(HttpStatusCode.Redirect,(await Register("new@example.test",null)).StatusCode);
 using(var scope=app.Services.CreateScope()){var users=scope.ServiceProvider.GetRequiredService<UserManager<AppUser>>();var user=(await users.FindByEmailAsync("new@example.test"))!;Assert.NotEqual(householdId,user.HouseholdId);Assert.Empty(await users.GetRolesAsync(user));var token=await users.GenerateEmailConfirmationTokenAsync(user);var response=await c.GetAsync($"/Account/Recovery?mode=confirm&user={user.Id}&token={Uri.EscapeDataString(token)}");Assert.Equal(HttpStatusCode.Redirect,response.StatusCode);}
 }
 [Fact] public async Task SimultaneousReminderReadsCreateOnlyOnePerRule(){await using var app=new AppFactory();await app.Initialize();Guid household;using(var scope=app.Services.CreateScope())household=(await scope.ServiceProvider.GetRequiredService<AppDb>().Households.SingleAsync(x=>x.Name=="A")).Id;
 await Task.WhenAll(Enumerable.Range(0,2).Select(async _=>{using var scope=app.Services.CreateScope();await scope.ServiceProvider.GetRequiredService<HouseholdService>().EnsureReminders(household,new(2026,12,10));}));using var check=app.Services.CreateScope();var db=check.ServiceProvider.GetRequiredService<AppDb>();Assert.Equal(2,await db.Reminders.CountAsync(x=>x.HouseholdId==household));}
 [Fact] public async Task OrdinaryMemberCannotPostAdminOperations(){await using var app=new AppFactory();await app.Initialize();using var c=app.Client();await AppFactory.Login(c,"member@example.test");var html=await c.GetStringAsync("/");foreach(var handler in new[]{"Sync","Settings","Alias"}){var response=await c.PostAsync("/Admin/Recipes?handler="+handler,new FormUrlEncodedContent(new Dictionary<string,string>{{"__RequestVerificationToken",AppFactory.Token(html)},{"catalog","true"},{"registration","true"}}));Assert.Equal(HttpStatusCode.Redirect,response.StatusCode);}using var scope=app.Services.CreateScope();Assert.False((await scope.ServiceProvider.GetRequiredService<AppDb>().Settings.SingleAsync()).PublicCatalog);}
}
