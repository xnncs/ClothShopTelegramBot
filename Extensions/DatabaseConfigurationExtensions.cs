using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using ShopTelegramBot.Database;

namespace ShopTelegramBot.Extensions;

public static class DatabaseConfigurationExtensions
{
    public static DbContextOptionsBuilder ConfigureApplicationDbContext(this DbContextOptionsBuilder options,
        IConfiguration configuration)
    {
        var connectionString = configuration.GetConnectionString(nameof(ApplicationDbContext))
                               ?? throw new Exception("Service error: connection string is not configured");

        options.UseNpgsql(connectionString)
            .EnableSensitiveDataLogging();

        return options;
    }
}