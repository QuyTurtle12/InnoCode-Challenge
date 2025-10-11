using System.Text.Json.Serialization;
using InnoCode_Challenge_API.DI;
using Microsoft.EntityFrameworkCore;
using Product_Sale_API.Middleware;
using Utility.Helpers;

var builder = WebApplication.CreateBuilder(args);
// Add config for Cloudinary settings
builder.Services.Configure<CloudinarySettings>(
    builder.Configuration.GetSection("CloudinarySettings"));

builder.Services.AddHttpClient();
// Add services to the container.

builder.Services.AddControllers()
        .AddJsonOptions(options =>
        {
            // Serialize enums as strings
            options.JsonSerializerOptions.Converters.Add(new JsonStringEnumConverter());
        });
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

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

app.MapControllers();

app.Run();


  