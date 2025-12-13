using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Repository.ResponseModel;
using System.Security.Claims;
using Utility.Constant;

namespace InnoCode_Challenge_API.Controllers.Users
{
    public class HangfireAuthController : ControllerBase
    {
        [HttpPost("hangfire-login")]
        [Authorize(Roles = RoleConstants.Admin)]
        public async Task<IActionResult> HangfireLogin()
        {
            // Get claims from JWT token
            var claims = User.Claims.Select(c => new Claim(c.Type, c.Value)).ToList();

            // Create claims identity for cookie authentication
            var claimsIdentity = new ClaimsIdentity(claims, "HangfireCookie");
            var claimsPrincipal = new ClaimsPrincipal(claimsIdentity);

            // Sign in with cookie authentication
            await HttpContext.SignInAsync("HangfireCookie", claimsPrincipal, new AuthenticationProperties
            {
                IsPersistent = true,
                ExpiresUtc = DateTimeOffset.UtcNow.AddHours(8)
            });

            var response = BaseResponseModel<object>.OkResponseModel(
                data: new
                {
                    dashboardUrl = "/hangfire",
                    expiresAt = DateTimeOffset.UtcNow.AddHours(8)
                },
                additionalData: null
            );
            response.Message = "Cookie authentication successful. You can now access the Hangfire dashboard.";

            return Ok(response);
        }

        [HttpPost("hangfire-logout")]
        public async Task<IActionResult> HangfireLogout()
        {
            await HttpContext.SignOutAsync("HangfireCookie");

            var response = BaseResponseModel<object>.OkResponseModel(
                data: null,
                additionalData: null
            );
            response.Message = "Logged out from Hangfire dashboard.";

            return Ok(response);
        }
    }
}
