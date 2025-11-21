using BusinessLogic.Hubs;
using InnoCode_Challenge_API.DI;
using InnoCode_Challenge_API.Middleware;
using Microsoft.AspNetCore.Mvc;
using System.Text.Json.Serialization;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddHttpClient();

builder.Services.AddControllers()
    .AddJsonOptions(options =>
    {
        options.JsonSerializerOptions.Converters.Add(new JsonStringEnumConverter());
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
app.UseMiddleware<CustomExceptionHandlerMiddleware>();

app.UseHttpsRedirection();

app.UseCors("AllowAllOrigins");
app.UseAuthentication();
app.UseAuthorization();


app.MapHub<LeaderboardHub>("/hubs/leaderboard");

app.MapControllers();
app.Run();
      