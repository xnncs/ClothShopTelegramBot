using System.Text;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Primitives;
using ShopTelegramBot.Abstract;
using ShopTelegramBot.Database;
using ShopTelegramBot.HelpingModels;
using ShopTelegramBot.Models;
using Telegram.Bot;
using Telegram.Bot.Types;
using Telegram.Bot.Types.InputFiles;
using Telegram.Bot.Types.ReplyMarkups;
using TelegramUpdater.UpdateContainer;
using TelegramUpdater.UpdateHandlers.Scoped.ReadyToUse;
using IO_File = System.IO.File;
using Telegram_File = Telegram.Bot.Types.File;
using System.IO;

namespace ShopTelegramBot.Handlers;

public class ScopedCallbackHandler : CallbackQueryHandler
{
    public ScopedCallbackHandler(ApplicationDbContext dbContext, ILogger<ScopedCallbackHandler> logger, IOptions<CharReplacingSettings> replacingSettings, IPhotoDownloadHelper photoDownloadHelper)
    {
        _dbContext = dbContext;
        _logger = logger;
        _photoDownloadHelper = photoDownloadHelper;
        _specialSymbol = replacingSettings.Value.SpecialSymbol[0];
    }
    
    private readonly ApplicationDbContext _dbContext;
    private readonly ILogger<ScopedCallbackHandler> _logger;
    private readonly IPhotoDownloadHelper _photoDownloadHelper;
    
    private readonly char _specialSymbol;
    
    protected override async Task HandleAsync(IContainer<CallbackQuery> container)
    {
        if (container.Container.CallbackQuery?.Data == null)
        {
            return;
        }

        CallbackQuery callbackQuery = container.Container.CallbackQuery;
        
        
        List<string> categoryNames = await _dbContext.ShoppingCategories.Select(x => x.Name).ToListAsync();
        categoryNames = categoryNames.Select(GenerateCategoriesCallbackFormatString).ToList();

        if (categoryNames.Contains(callbackQuery.Data))
        {
            await HandleChoosingCategoryAsync(callbackQuery);
        }

        List<string> itemNames = await _dbContext.ShoppingItems.Select(x => x.Name).ToListAsync();
        itemNames = itemNames.Select(GenerateItemsCallbackFormatString).ToList();

        if (itemNames.Contains(callbackQuery.Data))
        {
            await HandleChoosingItemAsync(callbackQuery);
        }
    }

    private async Task HandleChoosingItemAsync(CallbackQuery callbackQuery)
    {
        long userId = callbackQuery.From.Id;
        
        List<ShoppingItem> items = await _dbContext.ShoppingItems
            .Include(x => x.ShoppingCategory)
            .ToListAsync();

        ShoppingItem chosenItem =
            items.FirstOrDefault(x => GenerateItemsCallbackFormatString(x.Name) == callbackQuery.Data)
            ?? throw new Exception();
        
        string responseMessage = GenerateLongShoppingItemMessage(chosenItem);

        if (chosenItem.PhotoFileNames.Count == 0)
        {
            await BotClient.SendTextMessageAsync(userId, "Something went wrong with pictures.");
            await BotClient.SendTextMessageAsync(userId, responseMessage);
            throw new Exception("Something wrong with photo files");
        }
        if (chosenItem.PhotoFileNames.Count == 1)
        {
            await SendShoppingItemWithSinglePhotoAsync(chosenItem, userId);
            return;
        }
        if (chosenItem.PhotoFileNames.Count > 9)
        {
            throw new Exception("Item has more then 9 pictures");
        }

        (List<InputMediaPhoto>, List<Stream>) getPhotosResult = await GetResponsePhotosForShoppingItemAsync(chosenItem, userId);

        await BotClient.SendMediaGroupAsync(userId, getPhotosResult.Item1);

        foreach (Stream stream in getPhotosResult.Item2)
        {
            await stream.DisposeAsync();
        }
    }

    private async Task<(List<InputMediaPhoto>, List<Stream>)> GetResponsePhotosForShoppingItemAsync(ShoppingItem chosenItem, long userId)
    {
        string responseMessage = GenerateLongShoppingItemMessage(chosenItem);
        
        List<InputMediaPhoto> photos = new List<InputMediaPhoto>();

        List<Stream> photoStreams = new List<Stream>();
        foreach (string fileName in chosenItem.PhotoFileNames)
        {
            string photoPathUrl =
                _photoDownloadHelper.GenerateFilePathString(fileName, chosenItem.Name);


            if (!IO_File.Exists(photoPathUrl))
            {
                await BotClient.SendTextMessageAsync(userId, "Something went wrong with pictures.");
                await BotClient.SendTextMessageAsync(userId, responseMessage);
                throw new Exception("Something wrong with photo files");
            }

            Stream fileStream = IO_File.Open(photoPathUrl, FileMode.Open, FileAccess.Read, FileShare.Read);
            photos.Add(
                new InputMediaPhoto(
                    new InputMedia(fileStream, fileName: fileName)));
            
            photoStreams.Add(fileStream);
        }

        photos[0].Caption = responseMessage;
        return (photos, photoStreams);
    }

    private async Task SendShoppingItemWithSinglePhotoAsync(ShoppingItem chosenItem, long userId)
    {
        string responseMessage = GenerateLongShoppingItemMessage(chosenItem);
        
        string photoPathUrl =
            _photoDownloadHelper.GenerateFilePathString(chosenItem.PhotoFileNames[0], chosenItem.Name);
            
        if (!IO_File.Exists(photoPathUrl))
        {
            await BotClient.SendTextMessageAsync(userId, "Something went wrong with pictures.");
            await BotClient.SendTextMessageAsync(userId, responseMessage);
            throw new Exception("No such files with this path");
        }
            
        using (Stream fileStream = IO_File.Open(photoPathUrl, FileMode.Open, FileAccess.Read, FileShare.Read))
        {
            InputOnlineFile file = new InputMedia(content: fileStream, fileName: chosenItem.PhotoFileNames[0]);
            await BotClient.SendPhotoAsync(userId, file, responseMessage);
        }
    }

    private async Task HandleChoosingCategoryAsync(CallbackQuery callbackQuery)
    {
        long userId = callbackQuery.From.Id;
        
        List<ShoppingCategory> categories = await _dbContext.ShoppingCategories
            .Include(x => x.ShoppingItems)
            .ToListAsync();
        
        ShoppingCategory chosenCategory = categories.FirstOrDefault(x => GenerateCategoriesCallbackFormatString(x.Name) == callbackQuery.Data)
                                           ?? throw new Exception();
        
        if (!chosenCategory.ShoppingItems.Any())
        {
            await BotClient.SendTextMessageAsync(userId, "Sorry, no items in this category right now");
            return;
        }
        
        string responseMessage = GenerateMessageOnGetShoppingItem(chosenCategory);
        
        if (chosenCategory.ShoppingItems.Count == 0)
        {
            await BotClient.SendTextMessageAsync(userId, "Something went wrong with pictures.");
            await BotClient.SendTextMessageAsync(userId, responseMessage);
            throw new Exception("Something wrong with photo files");
        }
        if (chosenCategory.ShoppingItems.Count == 1)
        {
            await SendShoppingCategoryWithSingleItemAsync(chosenCategory, userId);
            return;
        }
        if (chosenCategory.ShoppingItems.Count > 9)
        {
            throw new Exception("Category cant has more then 9 items");
        }

        InlineKeyboardMarkup keyboard = GenerateCategoriesInlineKeyboardMarkup(chosenCategory);
        
        (List<InputMediaPhoto>, List<Stream>) getPhotosResult =
            await GetResponsePhotosForCategoryAsync(chosenCategory, userId);

        await BotClient.SendMediaGroupAsync(userId, getPhotosResult.Item1);

        foreach (Stream stream in getPhotosResult.Item2)
        {
            await stream.DisposeAsync();
        }
    }
    
    private async Task<(List<InputMediaPhoto>, List<Stream>)> GetResponsePhotosForCategoryAsync(ShoppingCategory category, long userId)
    {
        string responseMessage = GenerateMessageOnGetShoppingItem(category);
        
        List<InputMediaPhoto> photos = new List<InputMediaPhoto>();

        List<Stream> photoStreams = new List<Stream>();
        foreach (ShoppingItem item in category.ShoppingItems)
        {
            string photoFileName = item.PhotoFileNames[0];
            
            
            string photoPathUrl =
                _photoDownloadHelper.GenerateFilePathString(photoFileName, item.Name);


            if (!IO_File.Exists(photoPathUrl))
            {
                await BotClient.SendTextMessageAsync(userId, "Something went wrong with pictures.");
                await BotClient.SendTextMessageAsync(userId, responseMessage);
                throw new Exception("Something wrong with photo files");
            }

            Stream fileStream = IO_File.Open(photoPathUrl, FileMode.Open, FileAccess.Read, FileShare.Read);
            photos.Add(
                new InputMediaPhoto(
                    new InputMedia(fileStream, fileName: item.Name)));
            
            photoStreams.Add(fileStream);
        }
        photos[0].Caption = responseMessage;
        
        return (photos, photoStreams);
    }

    private async Task SendShoppingCategoryWithSingleItemAsync(ShoppingCategory category, long userId)
    {
        string responseMessage = GenerateMessageOnGetShoppingItem(category);

        ShoppingItem singleItem = category.ShoppingItems[0];
        
        string photoPathUrl =
            _photoDownloadHelper.GenerateFilePathString(singleItem.PhotoFileNames[0], singleItem.Name);
            
        if (!IO_File.Exists(photoPathUrl))
        {
            await BotClient.SendTextMessageAsync(userId, responseMessage);
            throw new Exception("No such files with this path");
        }
            
        using (Stream fileStream = IO_File.Open(photoPathUrl, FileMode.Open, FileAccess.Read, FileShare.Read))
        {
            InputOnlineFile file = new InputMedia(content: fileStream, fileName: singleItem.PhotoFileNames[0]);
            await BotClient.SendPhotoAsync(userId, file, responseMessage);
        }
    }

    private string GenerateMessageOnGetShoppingItem(ShoppingCategory category)
    {
        StringBuilder messageBuilder = new StringBuilder();

        messageBuilder.Append($"Our {category.Name}:\n");
        
        for (int index = 0; index < category.ShoppingItems.Count; index++)
        {
            ShoppingItem item = category.ShoppingItems[index];
            
            messageBuilder.Append($"\n{index + 1}. ");
            messageBuilder.Append(GenerateShortShoppingItemMessage(item));
        }

        return messageBuilder.ToString();
    }

    private string GenerateShortShoppingItemMessage(ShoppingItem item)
    {
        StringBuilder messageBuilder = new StringBuilder();
        
        messageBuilder.Append($"{item.Name} - {item.Price} rubles\n");
        
        return messageBuilder.ToString();
    }
    
    private string GenerateLongShoppingItemMessage(ShoppingItem item)
    {
        StringBuilder messageBuilder = new StringBuilder();
        
        messageBuilder.Append($"{item.Name} - {item.Price} rubles\n\n");
        messageBuilder.Append(item.Description);
        messageBuilder.Append($"\n\n\nIn stock: {item.UnitsInStock}");

        return messageBuilder.ToString();
    }

    private InlineKeyboardMarkup GenerateCategoriesInlineKeyboardMarkup(ShoppingCategory category)
    {
        List<InlineKeyboardButton> buttons = new List<InlineKeyboardButton>();
        for (int index = 0; index < category.ShoppingItems.Count; index++)
        {
            ShoppingItem item = category.ShoppingItems[index];

            InlineKeyboardButton button = new InlineKeyboardButton((index + 1).ToString())
            {
                CallbackData = GenerateItemsCallbackFormatString(item.Name)
            };
            
            buttons.Add(button);
        }

        return new InlineKeyboardMarkup(buttons);
    }
    
    private static string GenerateCategoriesCallbackFormatString(string x)
    {
        return $"categories/{x.ToLower()}";
    }
    
    private static string GenerateItemsCallbackFormatString(string x)
    {
        return $"items/{x.ToLower()}";
    }
    
}