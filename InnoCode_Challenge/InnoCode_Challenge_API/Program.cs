using System.Text.Json.Serialization;
using BusinessLogic.Hubs;
using InnoCode_Challenge_API.DI;
using InnoCode_Challenge_API.Middleware;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddHttpClient();

builder.Services.AddControllers()
        .AddJsonOptions(options =>
        {
            // Serialize enums as strings
            options.JsonSerializerOptions.Converters.Add(new JsonStringEnumConverter());
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
      