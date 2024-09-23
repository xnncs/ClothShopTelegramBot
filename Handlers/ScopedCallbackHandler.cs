using System.Text;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
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

namespace ShopTelegramBot.Handlers;

public class ScopedCallbackHandler : CallbackQueryHandler
{
    private readonly ICallbackGenerateHelper _callbackGenerateHelper;

    private readonly ApplicationDbContext _dbContext;
    private readonly ILogger<ScopedCallbackHandler> _logger;

    private readonly int _paginationLimit = 3;
    private readonly IPhotoDownloadHelper _photoDownloadHelper;

    private readonly char _specialSymbol;

    public ScopedCallbackHandler(ApplicationDbContext dbContext, ILogger<ScopedCallbackHandler> logger,
        IOptions<CharReplacingSettings> replacingSettings, IPhotoDownloadHelper photoDownloadHelper,
        ICallbackGenerateHelper callbackGenerateHelper)
    {
        _dbContext = dbContext;
        _logger = logger;
        _photoDownloadHelper = photoDownloadHelper;
        _callbackGenerateHelper = callbackGenerateHelper;
        _specialSymbol = replacingSettings.Value.SpecialSymbol[0];
    }

    protected override async Task HandleAsync(IContainer<CallbackQuery> container)
    {
        if (container.Container.CallbackQuery?.Data == null) return;

        var callbackQuery = container.Container.CallbackQuery;

        var categoryNames = await _dbContext.ShoppingCategories.Select(x => x.Name).ToListAsync();
        var itemNames = await _dbContext.ShoppingItems.Select(x => x.Name).ToListAsync();


        var categoryNamesFormatForGetCategoryCallback = categoryNames
            .Select(_callbackGenerateHelper.GenerateCategoriesCallbackFormatStringOnGet).ToList();
        if (categoryNamesFormatForGetCategoryCallback.Contains(callbackQuery.Data))
            await HandleChooseCategoryAsync(callbackQuery);


        var itemNamesFormatForGetItemCallback =
            itemNames.Select(_callbackGenerateHelper.GenerateItemsCallbackFormatStringOnGet).ToList();
        if (itemNamesFormatForGetItemCallback.Contains(callbackQuery.Data)) await HandleChooseItemAsync(callbackQuery);

        var categoryNamesFormatForDeleteCategoryCallback = categoryNames
            .Select(_callbackGenerateHelper.GenerateCategoriesCallbackFormatStringOnDelete).ToList();
        if (categoryNamesFormatForDeleteCategoryCallback.Contains(callbackQuery.Data))
            await HandleDeleteCategoryAsync(callbackQuery);

        var itemNamesFormatForDeleteItemCallback =
            itemNames.Select(_callbackGenerateHelper.GenerateItemsCallbackFormatStringOnDelete).ToList();
        if (itemNamesFormatForDeleteItemCallback.Contains(callbackQuery.Data))
            await HandleDeleteItemAsync(callbackQuery);

        var itemNamesFormatForAddItemToCartCallback =
            itemNames.Select(_callbackGenerateHelper.GenerateCallbackOnAddToCart).ToList();
        if (itemNamesFormatForAddItemToCartCallback.Contains(callbackQuery.Data))
            await HandleAddItemToCartAsync(callbackQuery);

        var itemNamesFormatForRemoveItemFromCartCallback =
            itemNames.Select(_callbackGenerateHelper.GenerateCallbackOnRemoveFromCart).ToList();
        if (itemNamesFormatForRemoveItemFromCartCallback.Contains(callbackQuery.Data))
            await HandleRemoveItemFromCartAsync(callbackQuery);

        var getFeedbacksRegex = _callbackGenerateHelper.GetOnGetFeedbackByPageNumberRegex();
        if (getFeedbacksRegex.IsMatch(callbackQuery.Data)) await HandleGetFeedbacksAsync(callbackQuery);
    }
    

    #region FeedbackTools
    
    private async Task HandleGetFeedbacksAsync(CallbackQuery callbackQuery)
    {
        var userId = callbackQuery.From.Id;

        var pageNumber = _callbackGenerateHelper.GetPageNumberByGetFeedbackCallbackString(callbackQuery.Data!);

        var feedbacks = await _dbContext.Feedbacks
            .OrderByDescending(x => x.Rating)
            .Skip(pageNumber * _paginationLimit)
            .Take(_paginationLimit)
            .ToListAsync();
        var response = GenerateFeedbacksString(feedbacks, pageNumber);

        var count = await _dbContext.Feedbacks.CountAsync();
        if (count - pageNumber * _paginationLimit > _paginationLimit)
        {
            await BotClient.SendTextMessageAsync(userId, response, replyMarkup: new InlineKeyboardMarkup([
                new InlineKeyboardButton("Смотреть дальше")
                {
                    CallbackData = _callbackGenerateHelper.GenerateCallbackOnGetFeedbackByPageNumber(pageNumber + 1)
                }
            ]));
            return;
        }

        await BotClient.SendTextMessageAsync(userId, response);
    }

    private string GenerateFeedbacksString(List<Feedback> feedbacks, int pageNumber)
    {
        var messageBuilder = new StringBuilder();

        messageBuilder.Append($"Отзывы ({pageNumber + 1}-я страница):\n");

        foreach (var feedback in feedbacks) messageBuilder.AppendLine($"\n{GenerateFeedbackString(feedback)}");
        return messageBuilder.ToString();
    }

    private string GenerateFeedbackString(Feedback feedback)
    {
        return $"""
                Отзыв {feedback.Id.ToString()}:
                {feedback.Title} - {feedback.Rating} {GenerateWordFromByNumber(feedback.Rating)}
                {feedback.Text}
                """;
    }

    /// <summary>
    ///     Works only with 1-10 numbers
    /// </summary>
    private string GenerateWordFromByNumber(int number)
    {
        return number switch
        {
            1 => "звезда",
            2 or 3 or 4 => "звезды",
            _ => "звезд"
        };
    }

    #endregion


    #region CartTools

    private async Task HandleRemoveItemFromCartAsync(CallbackQuery callbackQuery)
    {
        var userId = callbackQuery.From.Id;

        var responseMessage = "Ok";
        await using (var transaction = await _dbContext.Database.BeginTransactionAsync())
        {
            try
            {
                var items = await _dbContext.ShoppingItems
                    .ToListAsync();

                var item = items.FirstOrDefault(x =>
                               _callbackGenerateHelper.GenerateCallbackOnRemoveFromCart(x.Name) ==
                               callbackQuery.Data)
                           ?? throw new Exception();

                var user = await _dbContext.Users
                               .Include(x => x.Cart)
                               .ThenInclude(x => x.ItemsAdded)
                               .FirstOrDefaultAsync(x => x.TelegramId == userId)
                           ?? throw new Exception();

                user.Cart.ItemsAdded.Remove(item);

                await _dbContext.SaveChangesAsync();
                await transaction.CommitAsync();
            }
            catch (Exception exception)
            {
                _logger.LogError(exception.Message);

                responseMessage = "Что-то пошло не так, попробуйте позже";
            }
        }

        await BotClient.SendTextMessageAsync(userId, responseMessage);
    }

    private async Task HandleAddItemToCartAsync(CallbackQuery callbackQuery)
    {
        var userId = callbackQuery.From.Id;

        var responseMessage = "Ok";
        await using (var transaction = await _dbContext.Database.BeginTransactionAsync())
        {
            try
            {
                var items = await _dbContext.ShoppingItems
                    .ToListAsync();

                var item = items.FirstOrDefault(x =>
                               _callbackGenerateHelper.GenerateCallbackOnAddToCart(x.Name) ==
                               callbackQuery.Data)
                           ?? throw new Exception();

                var user = await _dbContext.Users
                               .Include(x => x.Cart)
                               .ThenInclude(x => x.ItemsAdded)
                               .FirstOrDefaultAsync(x => x.TelegramId == userId)
                           ?? throw new Exception();

                user.Cart.ItemsAdded.Add(item);

                await _dbContext.SaveChangesAsync();
                await transaction.CommitAsync();
            }
            catch (Exception exception)
            {
                _logger.LogError(exception.Message);

                responseMessage = "Что-то пошло не так, попробуйте позже";
            }
        }

        await BotClient.SendTextMessageAsync(userId, responseMessage);
    }

    #endregion


    #region ItemTools

    private async Task HandleDeleteItemAsync(CallbackQuery callbackQuery)
    {
        var userId = callbackQuery.From.Id;

        var responseMessage = "Ok";
        await using (var transaction = await _dbContext.Database.BeginTransactionAsync())
        {
            try
            {
                var items = await _dbContext.ShoppingItems
                    .ToListAsync();

                var item = items.FirstOrDefault(x =>
                               _callbackGenerateHelper.GenerateItemsCallbackFormatStringOnDelete(x.Name) ==
                               callbackQuery.Data)
                           ?? throw new Exception();

                await _dbContext.ShoppingItems.Where(x => x.Id == item.Id).ExecuteDeleteAsync();

                await _dbContext.SaveChangesAsync();
                await transaction.CommitAsync();
            }
            catch (Exception exception)
            {
                _logger.LogError(exception.Message);

                responseMessage = "Что-то пошло не так, попробуйте позже";
            }
        }

        await BotClient.SendTextMessageAsync(userId, responseMessage);
    }

    private async Task HandleChooseItemAsync(CallbackQuery callbackQuery)
    {
        var userId = callbackQuery.From.Id;

        var items = await _dbContext.ShoppingItems
            .Include(x => x.ShoppingCategory)
            .ToListAsync();

        var chosenItem =
            items.FirstOrDefault(x =>
                _callbackGenerateHelper.GenerateItemsCallbackFormatStringOnGet(x.Name) == callbackQuery.Data)
            ?? throw new Exception();

        var responseMessage = GenerateLongShoppingItemMessage(chosenItem);

        if (chosenItem.PhotoFileNames.Count > 9) throw new Exception("Item has more then 9 pictures");

        if (chosenItem.PhotoFileNames.Count == 0)
        {
            await BotClient.SendTextMessageAsync(userId, "Что-то пошло не так с загрузкой фотографий.");
            await BotClient.SendTextMessageAsync(userId, responseMessage);
            throw new Exception("Something wrong with photo files");
        }

        if (chosenItem.PhotoFileNames.Count == 1)
        {
            await SendShoppingItemWithSinglePhotoAsync(chosenItem, userId);
        }
        else
        {
            var getPhotosResult = await GetResponsePhotosForShoppingItemAsync(chosenItem, userId);

            await BotClient.SendMediaGroupAsync(userId, getPhotosResult.Item1);

            foreach (var stream in getPhotosResult.Item2) await stream.DisposeAsync();
        }

        await BotClient.SendTextMessageAsync(userId, "Добавить в корзину", replyMarkup: new InlineKeyboardMarkup([
            new InlineKeyboardButton("Добавить")
            {
                CallbackData = _callbackGenerateHelper.GenerateCallbackOnAddToCart(chosenItem.Name)
            }
        ]));
    }

    private async Task<(List<InputMediaPhoto>, List<Stream>)> GetResponsePhotosForShoppingItemAsync(
        ShoppingItem chosenItem, long userId)
    {
        var responseMessage = GenerateLongShoppingItemMessage(chosenItem);

        var photos = new List<InputMediaPhoto>();

        var photoStreams = new List<Stream>();
        foreach (var fileName in chosenItem.PhotoFileNames)
        {
            var photoPathUrl =
                _photoDownloadHelper.GenerateFilePathString(fileName, chosenItem.Name);


            if (!IO_File.Exists(photoPathUrl))
            {
                await BotClient.SendTextMessageAsync(userId, "Что-то пошло не так с загрузкой фотографий.");
                await BotClient.SendTextMessageAsync(userId, responseMessage);
                throw new Exception("Something wrong with photo files");
            }

            Stream fileStream = IO_File.Open(photoPathUrl, FileMode.Open, FileAccess.Read, FileShare.Read);
            photos.Add(
                new InputMediaPhoto(
                    new InputMedia(fileStream, fileName)));

            photoStreams.Add(fileStream);
        }

        photos[0].Caption = responseMessage;

        return (photos, photoStreams);
    }

    private async Task SendShoppingItemWithSinglePhotoAsync(ShoppingItem chosenItem, long userId)
    {
        var responseMessage = GenerateLongShoppingItemMessage(chosenItem);

        var photoPathUrl =
            _photoDownloadHelper.GenerateFilePathString(chosenItem.PhotoFileNames[0], chosenItem.Name);

        if (!IO_File.Exists(photoPathUrl))
        {
            await BotClient.SendTextMessageAsync(userId, "Что-то пошло не так с загрузкой фотографий.");
            await BotClient.SendTextMessageAsync(userId, responseMessage);
            throw new Exception("No such files with this path");
        }

        using (Stream fileStream = IO_File.Open(photoPathUrl, FileMode.Open, FileAccess.Read, FileShare.Read))
        {
            InputOnlineFile file = new InputMedia(fileStream, chosenItem.PhotoFileNames[0]);
            await BotClient.SendPhotoAsync(userId, file, responseMessage);
        }
    }

    private string GenerateMessageOnGetShoppingItem(ShoppingCategory category)
    {
        var messageBuilder = new StringBuilder();

        messageBuilder.Append($"Наши {category.Name}:\n");

        for (var index = 0; index < category.ShoppingItems.Count; index++)
        {
            var item = category.ShoppingItems[index];

            messageBuilder.Append($"\n{index + 1}. ");
            messageBuilder.Append(GenerateShortShoppingItemMessage(item));
        }

        return messageBuilder.ToString();
    }

    private string GenerateShortShoppingItemMessage(ShoppingItem item)
    {
        var messageBuilder = new StringBuilder();

        messageBuilder.Append($"{item.Name} - {item.Price}р\n");

        return messageBuilder.ToString();
    }

    private string GenerateLongShoppingItemMessage(ShoppingItem item)
    {
        var messageBuilder = new StringBuilder();

        messageBuilder.Append($"{item.Name} - {item.Price}р\n\n");
        messageBuilder.Append(item.Description);
        messageBuilder.Append($"\n\n\nВ наличии: {item.UnitsInStock}");

        return messageBuilder.ToString();
    }

    #endregion


    #region CategoryTools

    private async Task HandleDeleteCategoryAsync(CallbackQuery callbackQuery)
    {
        var userId = callbackQuery.From.Id;

        var responseMessage = "Ok";
        await using (var transaction = await _dbContext.Database.BeginTransactionAsync())
        {
            try
            {
                var categories = await _dbContext.ShoppingCategories
                    .ToListAsync();

                var category = categories.FirstOrDefault(x =>
                                   _callbackGenerateHelper.GenerateCategoriesCallbackFormatStringOnDelete(x.Name) ==
                                   callbackQuery.Data)
                               ?? throw new Exception();

                await _dbContext.ShoppingCategories.Where(x => x.Id == category.Id).ExecuteDeleteAsync();

                await _dbContext.SaveChangesAsync();
                await transaction.CommitAsync();
            }
            catch (Exception exception)
            {
                _logger.LogError(exception.Message);

                responseMessage = "Что-то пошло не так, попробуйте позже";
            }
        }

        await BotClient.SendTextMessageAsync(userId, responseMessage);
    }


    private async Task HandleChooseCategoryAsync(CallbackQuery callbackQuery)
    {
        var userId = callbackQuery.From.Id;

        var categories = await _dbContext.ShoppingCategories
            .Include(x => x.ShoppingItems)
            .ToListAsync();

        var chosenCategory = categories.FirstOrDefault(x =>
                                 _callbackGenerateHelper.GenerateCategoriesCallbackFormatStringOnGet(x.Name) ==
                                 callbackQuery.Data)
                             ?? throw new Exception();

        if (!chosenCategory.ShoppingItems.Any())
        {
            await BotClient.SendTextMessageAsync(userId, "Пока что в этой категории нет товаров");
            return;
        }

        var responseMessage = GenerateMessageOnGetShoppingItem(chosenCategory);

        if (chosenCategory.ShoppingItems.Count == 0)
        {
            await BotClient.SendTextMessageAsync(userId, "Что-то пошло не так с загрузкой фотографий.");
            await BotClient.SendTextMessageAsync(userId, responseMessage);
            throw new Exception("Something wrong with photo files");
        }

        if (chosenCategory.ShoppingItems.Count == 1)
        {
            await SendShoppingCategoryWithSingleItemAsync(chosenCategory, userId);
            return;
        }

        if (chosenCategory.ShoppingItems.Count > 9) throw new Exception("Category cant has more then 9 items");

        var getPhotosResult =
            await GetResponsePhotosForCategoryAsync(chosenCategory, userId);

        await BotClient.SendMediaGroupAsync(userId, getPhotosResult.Item1);

        foreach (var stream in getPhotosResult.Item2) await stream.DisposeAsync();

        var keyboard = GenerateCategoriesInlineKeyboardMarkup(chosenCategory);
        await BotClient.SendTextMessageAsync(userId, "Выберите вещь по её номеру", replyMarkup: keyboard);
    }

    private async Task<(List<InputMediaPhoto>, List<Stream>)> GetResponsePhotosForCategoryAsync(
        ShoppingCategory category, long userId)
    {
        var responseMessage = GenerateMessageOnGetShoppingItem(category);

        var photos = new List<InputMediaPhoto>();

        var photoStreams = new List<Stream>();
        foreach (var item in category.ShoppingItems)
        {
            var photoFileName = item.PhotoFileNames[0];


            var photoPathUrl =
                _photoDownloadHelper.GenerateFilePathString(photoFileName, item.Name);


            if (!IO_File.Exists(photoPathUrl))
            {
                await BotClient.SendTextMessageAsync(userId, "Что-то пошло не так с загрузкой фотографий.");
                await BotClient.SendTextMessageAsync(userId, responseMessage);
                throw new Exception("Something wrong with photo files");
            }

            Stream fileStream = IO_File.Open(photoPathUrl, FileMode.Open, FileAccess.Read, FileShare.Read);
            photos.Add(
                new InputMediaPhoto(
                    new InputMedia(fileStream, item.Name)));

            photoStreams.Add(fileStream);
        }

        photos[0].Caption = responseMessage;

        return (photos, photoStreams);
    }

    private async Task SendShoppingCategoryWithSingleItemAsync(ShoppingCategory category, long userId)
    {
        var responseMessage = GenerateMessageOnGetShoppingItem(category);

        var singleItem = category.ShoppingItems[0];

        var photoPathUrl =
            _photoDownloadHelper.GenerateFilePathString(singleItem.PhotoFileNames[0], singleItem.Name);

        if (!IO_File.Exists(photoPathUrl))
        {
            await BotClient.SendTextMessageAsync(userId, responseMessage);
            throw new Exception("No such files with this path");
        }

        using (Stream fileStream = IO_File.Open(photoPathUrl, FileMode.Open, FileAccess.Read, FileShare.Read))
        {
            InputOnlineFile file = new InputMedia(fileStream, singleItem.PhotoFileNames[0]);
            await BotClient.SendPhotoAsync(userId, file, responseMessage);
        }

        var keyboard = GenerateCategoriesInlineKeyboardMarkup(category);
        await BotClient.SendTextMessageAsync(userId, "Выберите вещь по её номеру", replyMarkup: keyboard);
    }

    private InlineKeyboardMarkup GenerateCategoriesInlineKeyboardMarkup(ShoppingCategory category)
    {
        var buttons = new List<InlineKeyboardButton>();
        for (var index = 0; index < category.ShoppingItems.Count; index++)
        {
            var item = category.ShoppingItems[index];

            var button = new InlineKeyboardButton((index + 1).ToString())
            {
                CallbackData = _callbackGenerateHelper.GenerateItemsCallbackFormatStringOnGet(item.Name)
            };

            buttons.Add(button);
        }

        return new InlineKeyboardMarkup(buttons);
    }

    #endregion
}