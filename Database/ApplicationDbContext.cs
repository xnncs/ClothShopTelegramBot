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
    }
    
    public DbSet<User> Users { get; set; }
    
    public DbSet<ShoppingItem> ShoppingItems { get; set; }
    public DbSet<ShoppingCategory> ShoppingCategories { get; set; }

    public DbSet<Cart> Carts { get; set; }
    
    public DbSet<Feedback> Feedbacks { get; set; }
        

    private readonly IPathHelper _pathHelper;
    
    protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder)
    {
        string connectionString = GetConnectionString();

        optionsBuilder.UseNpgsql(connectionString)
            .EnableSensitiveDataLogging();
    }

    private string GetConnectionString()
    {
        return
            "Server=localhost;Database=ShopTelegramBot;Username=postgres;Password=1425;Port=5432;Include Error Detail=true";
    }
    
    
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        List<User> admins = [];

        modelBuilder.Entity<Feedback>(options =>
        {
            options.HasKey(x => x.Id);
            options.Property(x => x.Id).ValueGeneratedNever();

            options.Property(x => x.Text).HasMaxLength(1250);
        });
        
        modelBuilder.Entity<User>(options =>
        {
            options.HasKey(x => x.Id);
            options.Property(x => x.Id).ValueGeneratedNever();

            options.Property(x => x.Username).HasMaxLength(35);

            options.HasMany(x => x.Feedbacks)
                .WithOne(x => x.Author)
                .HasForeignKey(x => x.AuthorId);
                
            
            options.HasData(admins);
        });

        modelBuilder.Entity<ShoppingItem>(options =>
        {
            options.HasKey(x => x.Id);
            options.Property(x => x.Id).ValueGeneratedNever();

            options.Property(x => x.Name).HasMaxLength(35);

            options.Property(x => x.Description).HasMaxLength(1000);

            options.HasOne(x => x.ShoppingCategory)
                .WithMany(x => x.ShoppingItems)
                .OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<ShoppingCategory>(options =>
        {
            options.HasKey(x => x.Id);
            options.Property(x => x.Id).ValueGeneratedNever();

            options.Property(x => x.Name).HasMaxLength(35);

            options.Property(x => x.Description).HasMaxLength(1000);
        });

        modelBuilder.Entity<Cart>(options => 
        {
            options.HasOne(x => x.Owner)
                .WithOne(x => x.Cart)
                .HasForeignKey<Cart>(x => x.OwnerId);

            options.HasMany(x => x.ItemsAdded)
                .WithMany();
        });
    }
}