using DataAccess.Entities;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Api.IntegrationTests.Infrastructure;

public class ApiFactory : WebApplicationFactory<Program>
{
    // Root này giúp InMemory DB dùng chung giữa các service provider
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
