using AspNet.Security.OAuth.GitHub;

using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Mvc;

namespace DotNetEpicsWeb.Controllers
{
    public class AccountController : Controller
    {
        [HttpGet("signin")]
        public IActionResult SignIn(string returnUrl)
        {
            return Challenge(
                new AuthenticationProperties
                {
                    RedirectUri = "/" + returnUrl
                },
                GitHubAuthenticationDefaults.AuthenticationScheme
            );
        }

        [HttpGet("signout")]
        [HttpPost("signout")]
        public IActionResult SignOut()
        {
            return SignOut(
                new AuthenticationProperties
                {
                    RedirectUri = "/"
                },
                CookieAuthenticationDefaults.AuthenticationScheme
            );
        }
    }
}
