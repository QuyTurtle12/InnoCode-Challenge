using Hangfire.Dashboard;
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

            // In production, require Admin role
            return httpContext.User.Identity?.IsAuthenticated == true
                && httpContext.User.IsInRole(RoleConstants.Admin);
        }
    }
}
