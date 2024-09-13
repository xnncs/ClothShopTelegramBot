using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using ShopTelegramBot.Abstract;
using ShopTelegramBot.HelpingModels;
using Telegram.Bot;
using Telegram_File = Telegram.Bot.Types.File;

namespace ShopTelegramBot.Helpers;

public class PhotoDownloadHelper : IPhotoDownloadHelper
{
    public PhotoDownloadHelper(ITelegramBotClient botClient, ILogger<PhotoDownloadHelper> logger, IPathHelper pathHelper)
    {
        _botClient = botClient;
        _logger = logger;
        
        string fullDirectoryPath = Path.Combine(pathHelper.GetProjectDirectoryPath(), "photos");
        _configuration = PhotoDownloadHelperConfiguration.Create(fullDirectoryPath);
    }

    private readonly ITelegramBotClient _botClient;
    private readonly ILogger<PhotoDownloadHelper> _logger;
    private readonly PhotoDownloadHelperConfiguration _configuration;

    public async Task DownloadFile(string fileId, string additionalDirectoryName, string destinationFileName)
    {
        try
        {
            Telegram_File file = await _botClient.GetFileAsync(fileId);

            string additionalDirectoryFullPath = GenerateFullAdditionalDirectoryPath(additionalDirectoryName);
            if (!Directory.Exists(additionalDirectoryFullPath))
            {
                Directory.CreateDirectory(additionalDirectoryFullPath);
            }
            
            string fullFilePath = GenerateFullFilePath(additionalDirectoryFullPath, destinationFileName);
            await using (Stream saveImageStream = new FileStream(fullFilePath, FileMode.Create))
            {
                await _botClient.DownloadFileAsync(file.FilePath ?? throw new Exception("File path is null"),
                    saveImageStream);
            }
        }
        catch (Exception exception)
        {
            _logger.LogError(exception.Message);
        }
    }

    public string GenerateFilePathString(string fileId, string additionalDirectoryName)
    {
        return GenerateFullFilePath(GenerateFullAdditionalDirectoryPath(additionalDirectoryName), fileId);
    }

    private string GenerateFullAdditionalDirectoryPath(string additionalDirectoryName)
    {
        return Path.Combine(_configuration.BaseDirectoryPath, additionalDirectoryName);
    }
    private string GenerateFullFilePath(string additionalDirectoryFullPath, string fileId)
    {
        return Path.Combine(additionalDirectoryFullPath, fileId);
    }
}