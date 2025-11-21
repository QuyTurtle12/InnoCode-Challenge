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
            // Serialize enums as strings
            options.JsonSerializerOptions.Converters.Add(new JsonStringEnumConverter());
        })
    .ConfigureApiBehaviorOptions(options =>
    {
        options.InvalidModelStateResponseFactory = context =>
        {
            var firstError = context.ModelState
                .Where(kvp => kvp.Value?.Errors.Count > 0)
                .Select(kvp => new { Field = kvp.Key, Message = kvp.Value!.Errors.First().ErrorMessage })
                .FirstOrDefault();

            var field = firstError?.Field ?? "Request";
            var message = firstError?.Message ?? "Validation failed.";

            var payload = new
            {
                errorCode = "VALIDATION_ERROR",
                errorMessage = $"{field}: {message}"
            };

            return new BadRequestObjectResult(payload);
        };
    });

builder.Services.AddEndpointsApiExplorer();

builder.Services.AddHttpContextAccessor();
builder.Services.AddHttpClient();
builder.Services.AddInfrastructure(builder.Configuration);

var app = builder.Build();

app.UseSwagger();
app.UseSwaggerUI();

app.UseHttpsRedirection();

app.UseCors("AllowAllOrigins");
app.UseAuthentication();
app.UseAuthorization();

app.UseMiddleware<CustomExceptionHandlerMiddleware>();

app.MapHub<LeaderboardHub>("/hubs/leaderboard");

app.MapControllers();
app.Run();
