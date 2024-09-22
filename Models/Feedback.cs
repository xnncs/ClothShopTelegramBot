namespace ShopTelegramBot.Models;

public class Feedback
{
    public Guid Id { get; set; }

    public string Title { get; set; }
    public string Text { get; set; }

    public int Rating { get; set; }

    public User Author { get; set; }
    public Guid AuthorId { get; set; }

    public DateTime CreatedAt { get; set; }

    public static Feedback Create(string title, string text, int rate, User author)
    {
        return new Feedback()
        {
            Id = Guid.NewGuid(),
            Title = title,
            Text = text,
            Rating = rate,
            Author = author,
            AuthorId = author.Id,
            CreatedAt = DateTime.UtcNow
        };
    }
}