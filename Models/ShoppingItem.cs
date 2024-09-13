namespace ShopTelegramBot.Models;

public class ShoppingItem
{
    public static ShoppingItem Create(string name, string description, double price, int unitsInStock, ShoppingCategory category, List<string> photoFileNames) => new ShoppingItem()
    {
        Id = Guid.NewGuid(),
        Name = name,
        Description = description,
        Price = price,
        UnitsInStock = unitsInStock,
        ShoppingCategory = category,
        PhotoFileNames = photoFileNames,
        
        DateOfIssue = DateTime.UtcNow
    };
    
    public Guid Id { get; set; }
    public string Name { get; set; }
    public string Description { get; set; }
    public double Price { get; set; }
    public int UnitsInStock { get; set; }
    
    public List<string> PhotoFileNames { get; set; }
    
    public ShoppingCategory ShoppingCategory { get; set; } 
    
    public DateTime DateOfIssue { get; set; }
}