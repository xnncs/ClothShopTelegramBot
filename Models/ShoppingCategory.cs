namespace ShopTelegramBot.Models;

public class ShoppingCategory
{
    public static ShoppingCategory Create(string name, string? description) => new ShoppingCategory()
    {
        Id = Guid.NewGuid(),
        Name = name,
        Description = description
    };
    
    public Guid Id { get; set; }
    public string Name { get; set; }
    public string Description { get; set; }

    public List<ShoppingItem> ShoppingItems { get; set; } = [];
}