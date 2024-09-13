namespace ShopTelegramBot.HelpingModels;

public class PhotoDownloadHelperConfiguration
{
    public static PhotoDownloadHelperConfiguration Create(string baseDirectoryPath)
    {
        return new PhotoDownloadHelperConfiguration
        {
            BaseDirectoryPath = baseDirectoryPath
        };
    }
    public string BaseDirectoryPath { get; set; }
}