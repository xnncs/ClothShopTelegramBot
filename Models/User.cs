using static System.Guid;

namespace ShopTelegramBot.Models;

public class User
{
    public static User Create(long telegramId, string? username, int age, bool isAdmin) => new User
    {
        Id = NewGuid(),
        TelegramId = telegramId,
        Username = username,
        Age = age,
        IsAdmin = isAdmin
    };
    
    
    public Guid Id { get; set; }
    public long TelegramId { get; set; }
    public string? Username { get; set; }
    
    public int Age { get; set; }
    
    public bool IsAdmin { get; set; }
}