namespace ShopTelegramBot.Models;

public class Feedback
{
    public static Feedback Create(string text, int raing, User author) => new Feedback()
    {
        Id = Guid.NewGuid(),
        Text = text,
        Rating = raing,
        Author = author,
        CreatedAt = DateTime.Now
    };
    
    
    public Guid Id { get; set; }
    
    public string Text { get; set; }
    
    public int Rating { get; set; }
    
    public User Author { get; set; }
    public Guid AuthorId { get; set; }
    
    public DateTime CreatedAt { get; set; }
}