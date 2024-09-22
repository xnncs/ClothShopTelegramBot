using static System.Guid;

namespace ShopTelegramBot.Models;

public class User
{
    public Guid Id { get; set; }
    public long TelegramId { get; set; }
    public string? Username { get; set; }

    public int Age { get; set; }

    public bool IsAdmin { get; set; }

    public Cart Cart { get; set; }

    public List<Feedback> Feedbacks { get; set; }

    public static User Create(long telegramId, string? username, int age, bool isAdmin)
    {
        var cart = new Cart
        {
            Id = NewGuid(),
            ItemsAdded = Enumerable.Empty<ShoppingItem>().ToList()
        };
        var user = new User
        {
            Id = NewGuid(),
            TelegramId = telegramId,
            Username = username,
            Age = age,
            IsAdmin = isAdmin
        };
        cart.Owner = user;
        cart.OwnerId = user.Id;

        user.Cart = cart;

        return user;
    }
}