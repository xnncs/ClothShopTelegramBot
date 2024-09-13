using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using ShopTelegramBot.Abstract;
using ShopTelegramBot.Helpers;
using ShopTelegramBot.HelpingModels;
using ShopTelegramBot.Models;

namespace ShopTelegramBot.Database;

public sealed class ApplicationDbContext : DbContext
{
    public ApplicationDbContext()
    {
        _pathHelper = new PathHelper();
        
        Database.EnsureCreated();
    }
    
    public DbSet<User> Users { get; set; }
    
    public DbSet<ShoppingItem> ShoppingItems { get; set; }
    public DbSet<ShoppingCategory> ShoppingCategories { get; set; }


    private readonly IPathHelper _pathHelper;
    
    protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder)
    {
        string connectionString = GetConntectionString();

        optionsBuilder.UseNpgsql(connectionString)
            .EnableSensitiveDataLogging();
    }

    private string GetConntectionString()
    {
        return
            "Server=localhost;Database=ShopTelegramBot;Username=postgres;Password=1425;Port=5432;Include Error Detail=true";
    }
    
    
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        List<User> admins = [User.Create(1367636999, "zitiret", 18, true)];
        
        modelBuilder.Entity<User>(options =>
        {
            options.HasKey(x => x.Id);
            options.Property(x => x.Id).ValueGeneratedNever();

            options.Property(x => x.Username).HasMaxLength(35);

            
            options.HasData(admins);
        });

        modelBuilder.Entity<ShoppingItem>(options =>
        {
            options.HasKey(x => x.Id);
            options.Property(x => x.Id).ValueGeneratedNever();

            options.Property(x => x.Name).HasMaxLength(35);

            options.Property(x => x.Description).HasMaxLength(250);

            options.HasOne(x => x.ShoppingCategory)
                .WithMany(x => x.ShoppingItems)
                .OnDelete(DeleteBehavior.NoAction);
        });

        modelBuilder.Entity<ShoppingCategory>(options =>
        {
            options.HasKey(x => x.Id);
            options.Property(x => x.Id).ValueGeneratedNever();

            options.Property(x => x.Name).HasMaxLength(35);

            options.Property(x => x.Description).HasMaxLength(250);
        });
    }
}