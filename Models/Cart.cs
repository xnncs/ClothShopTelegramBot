namespace ShopTelegramBot.Models;

public class Cart
{
    public Guid Id { get; set; }

    public User Owner { get; set; }
    public Guid OwnerId { get; set; }

    public List<ShoppingItem> ItemsAdded { get; set; }
}