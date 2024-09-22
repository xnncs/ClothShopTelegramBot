using ShopTelegramBot.Abstract;

namespace ShopTelegramBot.Helpers;

public class PathHelper : IPathHelper
{
    private const string SpitWord = "bin";

    public string GetProjectDirectoryPath()
    {
        var path = GetType().Assembly.Location;
        var index = GetFirstIndex(path) ?? throw new Exception("Wrong path format");

        return path.Remove(index);
    }

    private int? GetFirstIndex(string path)
    {
        if (path.Length < 3) return null;

        for (var index = 0; index < path.Length; index++)
            if (path[index..(index + 3)] == SpitWord)
                return index;

        return null;
    }
}