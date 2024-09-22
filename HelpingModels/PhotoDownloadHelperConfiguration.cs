namespace ShopTelegramBot.HelpingModels;

public class PhotoDownloadHelperConfiguration
{
    public string BaseDirectoryPath { get; set; }

    public static PhotoDownloadHelperConfiguration Create(string baseDirectoryPath)
    {
        return new PhotoDownloadHelperConfiguration
        {
            BaseDirectoryPath = baseDirectoryPath
        };
    }
}