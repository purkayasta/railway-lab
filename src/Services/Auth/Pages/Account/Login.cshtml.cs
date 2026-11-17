using System.ComponentModel.DataAnnotations;
using AuthService.Data;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace AuthService.Pages.Account;

public sealed class LoginModel(SignInManager<AppUser> signIn) : PageModel
{
    [BindProperty, Required, StringLength(256)] public string Username { get; set; } = "";
    [BindProperty, Required, StringLength(256)] public string Password { get; set; } = "";
    [BindProperty] public string? ReturnUrl { get; set; }
    public string? Error { get; private set; }

    public void OnGet(string? returnUrl) => ReturnUrl = returnUrl;

    public async Task<IActionResult> OnPostAsync()
    {
        if (!ModelState.IsValid)
        {
            Error = "Enter your username and password.";
            return Page();
        }

        // lockoutOnFailure counts failed attempts and locks the account after the configured number.
        var result = await signIn.PasswordSignInAsync(Username, Password, isPersistent: false, lockoutOnFailure: true);
        if (result.Succeeded)
            return Url.IsLocalUrl(ReturnUrl) ? LocalRedirect(ReturnUrl) : LocalRedirect("/account/signed-in");

        // The same message for unknown user and wrong password: no account enumeration.
        Error = result.IsLockedOut
            ? "Too many failed attempts. Try again in 15 minutes."
            : "Invalid username or password.";
        return Page();
    }
}
