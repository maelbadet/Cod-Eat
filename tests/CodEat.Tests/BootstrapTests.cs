using CodEat.Web;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Xunit;
namespace CodEat.Tests;
public class BootstrapTests {
 [Fact] public async Task BothInitialAccountsAreCommittedAndShareHousehold(){await using var app=new AppFactory();using(var scope=app.Services.CreateScope()){await scope.ServiceProvider.GetRequiredService<AppDb>().Database.EnsureCreatedAsync();var config=new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string,string?>{{"Bootstrap:Email","first@example.test"},{"Bootstrap:Password","Test!Password123"},{"Bootstrap:SecondEmail","second@example.test"},{"Bootstrap:SecondPassword","Test!Password123"}}).Build();await Setup.Bootstrap(scope.ServiceProvider,config);}using var verify=app.Services.CreateScope();var users=await verify.ServiceProvider.GetRequiredService<AppDb>().Users.ToListAsync();Assert.Equal(2,users.Count);Assert.Single(users.Select(x=>x.HouseholdId).Distinct());Assert.Equal(new[]{1,2},users.Select(x=>x.MemberSlot).Order().ToArray());}
}
