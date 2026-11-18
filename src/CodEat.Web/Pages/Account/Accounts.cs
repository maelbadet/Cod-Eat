using System.ComponentModel.DataAnnotations;
using System.Security.Cryptography;
using System.Text;
using CodEat.Web;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
namespace CodEat.Web.Pages.Account;
[EnableRateLimiting("public-forms")]
public class LoginModel(SignInManager<AppUser> signIn) : PageModel {
 [BindProperty,Required,EmailAddress] public string Email {get;set;}="";
 [BindProperty,Required,DataType(DataType.Password)] public string Password {get;set;}="";
 public async Task<IActionResult> OnPostAsync(){if(!ModelState.IsValid)return Page(); var result=await signIn.PasswordSignInAsync(Email,Password,false,true);if(result.Succeeded)return LocalRedirect("/");ModelState.AddModelError("","Connexion impossible. Vérifiez vos informations et la confirmation de votre adresse, ou réessayez plus tard.");return Page();}
}
[EnableRateLimiting("public-forms")]
public class RegisterModel(AppDb db, UserManager<AppUser> users, MailService mail, IConfiguration config) : PageModel {
 [BindProperty,Required,EmailAddress] public string Email {get;set;}="";
 [BindProperty,Required,MinLength(12),DataType(DataType.Password)] public string Password {get;set;}="";
 [BindProperty(SupportsGet=true)] public string? Invite {get;set;}
 public static string Hash(string token)=>Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(token)));
 async Task<bool> Allowed()=>!string.IsNullOrEmpty(Invite)|| (await db.Settings.SingleAsync()).PublicRegistration;
 public async Task<IActionResult> OnGetAsync()=>await Allowed()?Page():NotFound();
 public async Task<IActionResult> OnPostAsync(){
 if(!await Allowed())return NotFound(); if(!ModelState.IsValid)return Page();
 await using var transaction=await db.Database.BeginTransactionAsync();
 Invitation? invitation=null; Household household;
 if(!string.IsNullOrEmpty(Invite)){var hash=Hash(Invite); invitation=await db.Invitations.SingleOrDefaultAsync(x=>x.TokenHash==hash&&!x.Used&&x.ExpiresUtc>DateTime.UtcNow); if(invitation==null){ModelState.AddModelError("","Invitation expirée ou déjà utilisée.");return Page();} if(await users.Users.CountAsync(x=>x.HouseholdId==invitation.HouseholdId)>=2){ModelState.AddModelError("","Ce foyer possède déjà deux membres.");return Page();}household=await db.Households.SingleAsync(x=>x.Id==invitation.HouseholdId);invitation.Used=true;invitation.Revision=Guid.NewGuid();}
 else{household=new();db.Households.Add(household);}
 var user=new AppUser{UserName=Email,Email=Email,HouseholdId=household.Id,MemberSlot=invitation==null?1:2};
 try {var result=await users.CreateAsync(user,Password);if(!result.Succeeded){ModelState.AddModelError("","Impossible de créer le compte. Utilisez une adresse valide et un mot de passe de 12 caractères avec majuscule, minuscule, chiffre et symbole.");return Page();}await db.SaveChangesAsync();await transaction.CommitAsync();}
 catch(DbUpdateException){ModelState.AddModelError("","Inscription non disponible ou invitation déjà utilisée. Rechargez la page.");return Page();}
 var token=await users.GenerateEmailConfirmationTokenAsync(user);var url=$"{Seo.Origin(config)}/Account/Recovery?mode=confirm&user={Uri.EscapeDataString(user.Id)}&token={Uri.EscapeDataString(token)}";
 try{await mail.Send(Email,"Confirmez votre adresse Cod & Eat",url);TempData["Message"]="Compte créé. Consultez votre messagerie pour confirmer l’adresse.";}catch{TempData["Message"]="Compte créé, mais le courriel n’a pas pu être envoyé. Demandez un nouveau lien dans la page de récupération.";}return RedirectToPage("Login");
 }
}
[EnableRateLimiting("public-forms")]
public class RecoveryModel(UserManager<AppUser> users,MailService mail,IConfiguration config) : PageModel {
 [BindProperty] public string Email {get;set;}="";
 [BindProperty] public string Password {get;set;}="";
 [BindProperty(SupportsGet=true)] public string? Mode {get;set;}
 [BindProperty(SupportsGet=true)] public string? UserId {get;set;}
 [BindProperty(SupportsGet=true)] public string? Token {get;set;}
 public async Task<IActionResult> OnGetAsync(string? user){if(Mode=="confirm"&&user!=null&&Token!=null){var account=await users.FindByIdAsync(user);var result=account==null?IdentityResult.Failed():await users.ConfirmEmailAsync(account,Token);TempData["Message"]=result.Succeeded?"Adresse confirmée. Vous pouvez vous connecter.":"Lien invalide ou expiré.";return RedirectToPage("Login");}return Page();}
 public async Task<IActionResult> OnPostAsync(){
 if(Token!=null&&UserId!=null){var user=await users.FindByIdAsync(UserId);var result=user==null?IdentityResult.Failed():await users.ResetPasswordAsync(user,Token,Password);if(result.Succeeded){TempData["Message"]="Mot de passe modifié.";return RedirectToPage("Login");}ModelState.AddModelError("","Lien invalide ou mot de passe insuffisant.");return Page();}
 var account=await users.FindByEmailAsync(Email);if(account!=null){var confirm=Mode=="resend";var token=confirm?await users.GenerateEmailConfirmationTokenAsync(account):await users.GeneratePasswordResetTokenAsync(account);var url=confirm?$"{Seo.Origin(config)}/Account/Recovery?mode=confirm&user={Uri.EscapeDataString(account.Id)}&token={Uri.EscapeDataString(token)}":$"{Seo.Origin(config)}/Account/Recovery?UserId={Uri.EscapeDataString(account.Id)}&Token={Uri.EscapeDataString(token)}";try{await mail.Send(Email,"Votre lien Cod & Eat",url);}catch{}}
 TempData["Message"]="Si ce compte existe, un lien vous sera envoyé.";return RedirectToPage("Login");
 }
}
public class LogoutModel(SignInManager<AppUser> signIn):PageModel {public async Task<IActionResult> OnPostAsync(){await signIn.SignOutAsync();return RedirectToPage("Login");}}
