using Hangfire.Dashboard;
using Microsoft.AspNetCore.Authentication;
using Utility.Constant;

namespace InnoCode_Challenge_API.DI
{
    public class HangfireAuthorizationFilter : IDashboardAuthorizationFilter
    {
        public bool Authorize(DashboardContext context)
        {
            var httpContext = context.GetHttpContext();

            // Allow in development
            if (httpContext.RequestServices.GetRequiredService<IWebHostEnvironment>().IsDevelopment())
            {
                return true;
            }

            // Try to authenticate with the cookie scheme first
            var result = httpContext.AuthenticateAsync("HangfireCookie").GetAwaiter().GetResult();

            if (result?.Succeeded == true)
            {
                // Set the authenticated user
                httpContext.User = result.Principal;

                // Check if user has Admin role
                if (httpContext.User.IsInRole(RoleConstants.Admin))
                {
                    return true;
                }
            }

            // Fallback: Check if already authenticated (JWT or Cookie)
            if (httpContext.User.Identity?.IsAuthenticated == true
                && httpContext.User.IsInRole(RoleConstants.Admin))
            {
                return true;
            }

            return false;
        }
    }
}
