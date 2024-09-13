using ShopTelegramBot.Abstract;

namespace ShopTelegramBot.Helpers;

public class PathHelper : IPathHelper
{
    private const string SpitWord = "bin";
    
    private int? GetFirstIndex(string path)
    {
        if (path.Length < 3)
        {
            return null;
        }

        for (int index = 0; index < path.Length; index++)
        {
            if (path[index..(index + 3)] == SpitWord)
            {
                return index;
            }
        }

        return null;
    }
    
    public string GetProjectDirectoryPath()
    {
        string path =  GetType().Assembly.Location;
        int index = GetFirstIndex(path) ?? throw new Exception("Wrong path format");

        return path.Remove(index);
    }
}

