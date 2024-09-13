namespace ShopTelegramBot.Abstract;

public interface IPhotoDownloadHelper
{
    Task DownloadFile(string fileId, string additionalDirectoryName, string destinationFileName);
    string GenerateFilePathString(string fileId, string additionalDirectoryName);
}