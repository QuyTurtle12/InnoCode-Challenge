using BusinessLogic.Hubs;
using BusinessLogic.Services.Contests;
using CloudinaryDotNet.Actions;
using Hangfire;
using InnoCode_Challenge_API.DI;
using InnoCode_Challenge_API.Middleware;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Text.Json.Serialization;
using Utility.Helpers;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddHttpClient();

builder.Services.AddControllers()
    .AddJsonOptions(options =>
    {
        // Serialize enums as strings
        options.JsonSerializerOptions.Converters.Add(new JsonStringEnumConverter());

        // Add custom datetime format globally
        options.JsonSerializerOptions.Converters.Add(new CustomDateTimeConverter());
        options.JsonSerializerOptions.Converters.Add(new CustomNullableDateTimeConverter());
    })
    .ConfigureApiBehaviorOptions(options =>
    {
        options.InvalidModelStateResponseFactory = context =>
        {
            var allErrors = context.ModelState
                .Where(kvp => kvp.Value?.Errors.Count > 0)
                .SelectMany(kvp => kvp.Value!.Errors.Select(e => new { Field = kvp.Key, e.ErrorMessage }))
                .ToList();

            bool isOnlyBodyMissing =
                allErrors.Count == 1 &&
                (allErrors[0].Field?.Equals("dto", StringComparison.OrdinalIgnoreCase) ?? false);

            if (isOnlyBodyMissing)
            {
                return new BadRequestObjectResult(new
                {
                    errorCode = "VALIDATION_ERROR",
                    errorMessage = "Request body is required."
                });
            }

            var actionParamNames = context.ActionDescriptor.Parameters
                .Select(p => p.Name)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);

            string CleanField(string raw)
            {
                if (string.IsNullOrWhiteSpace(raw)) return "Request";

                var parts = raw.Split('.', StringSplitOptions.RemoveEmptyEntries).ToList();
                if (parts.Count > 0 && actionParamNames.Contains(parts[0]))
                {
                    parts.RemoveAt(0);
                }

                if (parts.Count == 0) return "Request";

                return string.Join('.', parts);
            }

            var messages = allErrors
                .Select(e =>
                {
                    var field = CleanField(e.Field);
                    var msg = string.IsNullOrWhiteSpace(e.ErrorMessage) ? "Invalid value." : e.ErrorMessage.Trim();
                    return $"{field}: {msg}";
                })
                .ToList();

            if (messages.Count == 0)
            {
                messages.Add("Request: Validation failed.");
            }

            const int maxShown = 5;
            string finalMessage = messages.Count <= maxShown
                ? (messages.Count == 1 ? messages[0]
                                       : $"Validation errors: {string.Join(" | ", messages)}")
                : $"Validation errors: {string.Join(" | ", messages.Take(maxShown))} | ... and {messages.Count - maxShown} more";

            return new BadRequestObjectResult(new
            {
                errorCode = "VALIDATION_ERROR",
                errorMessage = finalMessage
            });
        };
    });


builder.Services.AddEndpointsApiExplorer();

builder.Services.AddHttpContextAccessor();
builder.Services.AddHttpClient();
builder.Services.AddInfrastructure(builder.Configuration);

var app = builder.Build();

app.UseSwagger();
app.UseSwaggerUI();
if (app.Environment.IsEnvironment("Testing"))
{
    app.UseMiddleware<CustomExceptionHandlerMiddleware>();
}
else
{
    app.UseMiddleware<CustomExceptionHandlerMiddleware>();
}

if (!app.Environment.IsEnvironment("Testing"))
{
    app.UseHttpsRedirection();
}

app.UseCors("AllowAllOrigins");
app.UseAuthentication();
app.UseAuthorization();

app.UseHangfireDashboard("/hangfire", new DashboardOptions
{
    Authorization = new[] { new HangfireAuthorizationFilter() },
    DashboardTitle = "InnoCode Challenge - Job Dashboard",
    StatsPollingInterval = 2000, // Update dashboard every 2 seconds
    DisplayStorageConnectionString = false,

    // Enable detailed job information
    DisplayNameFunc = (context, job) =>
    {
        // Custom display names for better readability
        if (job.Type.Name == "ContestStateJob")
        {
            return $"Contest State: {job.Method.Name}";
        }
        if (job.Type.Name == "RoundStateJob")
        {
            return $"Round State: {job.Method.Name}";
        }
        return $"{job.Type.Name}.{job.Method.Name}";
    }
});

app.MapHub<LeaderboardHub>("/hubs/leaderboard");
app.MapHub<NotificationsHub>("/hubs/notifications");
app.MapHub<ActivityLogsHub>("/hubs/activity-logs");
app.MapHub<DashboardHub>("/hubs/dashboard");

app.MapControllers();
app.Run();

public partial class Program { } // For integration testing purposes