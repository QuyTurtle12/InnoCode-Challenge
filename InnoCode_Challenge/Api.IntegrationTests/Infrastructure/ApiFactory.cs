using BusinessLogic.IServices.FileStorages;
using DataAccess.Entities;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.EntityFrameworkCore.Storage;

namespace Api.IntegrationTests.Infrastructure;

public class ApiFactory : WebApplicationFactory<Program>
{
    // Root nay giup InMemory DB dung chung giua cac service provider
    private readonly InMemoryDatabaseRoot _dbRoot = new();
    private const string DbName = "TestDb";

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");

        builder.ConfigureLogging(logging =>
        {
            logging.ClearProviders();
            logging.AddConsole();
            logging.SetMinimumLevel(LogLevel.Debug);
        });

        builder.ConfigureAppConfiguration((context, config) =>
        {
            config.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["JwtSettings:Key"] = "InnoCode_Challenge_InnoCode_Challenge_InnoCode_Challenge_InnoCode_Challenge_InnoCode_Challenge_InnoCode_Challenge_InnoCode_Challenge_InnoCode_Challenge_InnoCode_Challenge",
                ["JwtSettings:Issuer"] = "Capstone",
                ["JwtSettings:Audience"] = "Capstone",
                ["JwtSettings:ExpiryMinutes"] = "60",
                ["JwtSettings:RefreshExpiryMinutes"] = "1440"
            });
        });

        builder.ConfigureServices(services =>
        {
            // Remove existing DbContext registration
            var descriptor = services.SingleOrDefault(
                d => d.ServiceType == typeof(DbContextOptions<ContestDbContext>));
            if (descriptor != null) services.Remove(descriptor);

            // Replace Cloudinary with a fake to avoid network calls in tests
            var cloudDescriptor = services.SingleOrDefault(
                d => d.ServiceType == typeof(ICloudinaryService));
            if (cloudDescriptor != null) services.Remove(cloudDescriptor);
            services.AddSingleton<ICloudinaryService, FakeCloudinaryService>();

            // IMPORTANT: use same DbName + shared _dbRoot
            services.AddDbContext<ContestDbContext>(opt =>
                opt.UseInMemoryDatabase(DbName, _dbRoot));

            // Seed
            var sp = services.BuildServiceProvider();
            using var scope = sp.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<ContestDbContext>();

            db.Database.EnsureCreated();
            TestSeed.Seed(db);
        });
    }
}
