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
using Telegram.Bot.Types.Enums;
using Telegram.Bot.Types.InputFiles;
using Telegram.Bot.Types.ReplyMarkups;
using TelegramUpdater;
using TelegramUpdater.UpdateContainer;
using TelegramUpdater.UpdateHandlers.Scoped.ReadyToUse;
using Internal_User = ShopTelegramBot.Models.User;
using IO_File = System.IO.File;

namespace ShopTelegramBot.Handlers;

public class ScopedMessageHandler : MessageHandler
{
    private static readonly string[] UserCommands =
        ["/start", "/items", "/cart", "/info", "/feedbacks", "/add_feedback"];

    private static readonly string[] AdminCommands = ["/add_category", "/add_item", "/delete_category", "/delete_item"];
    private readonly ICallbackGenerateHelper _callbackGenerateHelper;

    private readonly ApplicationDbContext _dbContext;
    private readonly ILogger<ScopedMessageHandler> _logger;

    private readonly int _paginationLimit = 3;
    private readonly IPhotoDownloadHelper _photoDownloadHelper;

    private readonly char _specialSymbol;

    public ScopedMessageHandler(ApplicationDbContext dbContext, ILogger<ScopedMessageHandler> logger,
        IPhotoDownloadHelper photoDownloadHelper, IOptions<CharReplacingSettings> replacingSettings,
        ICallbackGenerateHelper callbackGenerateHelper)
    {
        _dbContext = dbContext;
        _logger = logger;
        _photoDownloadHelper = photoDownloadHelper;
        _callbackGenerateHelper = callbackGenerateHelper;
        _specialSymbol = replacingSettings.Value.SpecialSymbol[0];
    }

    protected override async Task HandleAsync(IContainer<Message> container)
    { 
        if (container.Update.Type != MessageType.Text) return;

        var message = container.Update;
        _logger.LogInformation($"Received message: {message.Text} from user with username: {message.From!.Username} with id: {message.From.Id}");

        if (message.Text!.StartsWith('/'))
        {
            var result = GetCommandArgumentsObject(message.Text);
            var command = result.Item1;
            var args = result.Item2;

            await OnCommand(command, args, message);
        }
        else
        {
            await OnTextMessage(message);
        }
    }

    private static (string, string) GetCommandArgumentsObject(string messageText)
    {
        var spaceIndex = messageText.IndexOf(' ');
        if (spaceIndex < 0) spaceIndex = messageText.Length;

        var command = messageText[..spaceIndex].ToLower();
        var args = messageText[spaceIndex..].TrimStart();

        return (command, args);
    }

    private async Task OnCommand(string command, string args, Message message)
    {
        var adminPermissionsErrorMessage = "У вас нет админ-прав для выполнения этой комманды.";
        switch (command)
        {
            case "/start":
                await OnStartCommandAsync(message.Chat.Id, message.Chat.Username);
                break;

            case "/items":
                await OnGetCategoriesCommandAsync();
                break;

            case "/cart":
                await OnGetCartCommandAsync(message.Chat.Id);
                break;

            case "/info":
                await OnGetInfoCommandAsync();
                break;

            case "/add_feedback":
                await OnAddFeedbackCommandAsync(message.Chat.Id);
                break;

            case "/feedbacks":
                await OnGetFeedbackFirstTimeAsync();
                break;


            case "/add_category":
                if (!await CheckAdminPermissionAsync(message.Chat.Id))
                {
                    await ResponseAsync(adminPermissionsErrorMessage);
                    break;
                }

                await OnAddCategoryCommandAsync();
                break;

            case "/add_item":
                if (!await CheckAdminPermissionAsync(message.Chat.Id))
                {
                    await ResponseAsync(adminPermissionsErrorMessage);
                    break;
                }

                await OnAddItemCommandAsync();
                break;

            case "/delete_category":
                if (!await CheckAdminPermissionAsync(message.Chat.Id))
                {
                    await ResponseAsync(adminPermissionsErrorMessage);
                    break;
                }

                await OnDeleteCategoryCommandAsync();
                break;

            case "/delete_item":
                if (!await CheckAdminPermissionAsync(message.Chat.Id))
                {
                    await ResponseAsync(adminPermissionsErrorMessage);
                    break;
                }

                await OnDeleteItemCommandAsync();
                break;
            
            case "/delete_feedback":
                if (!await CheckAdminPermissionAsync(message.Chat.Id))
                {
                    await ResponseAsync(adminPermissionsErrorMessage);
                    break;
                }

                await OnDeleteFeedbackCommandAsync();
                break;
        }
    }
    
    private async Task OnTextMessage(Message message)
    {
        var response = "Неправильный формат комманды";
        await ResponseAsync(response);
    }

    #region PermissonHelpingTools

    private async Task<bool> CheckAdminPermissionAsync(long telegramUserId)
    {
        var user = await _dbContext.Users.FirstOrDefaultAsync(x => x.TelegramId == telegramUserId);
        if (user == null) return false;

        return user.IsAdmin;
    }

    #endregion

    #region Commands

    #region AdminCommands

    private async Task OnDeleteFeedbackCommandAsync()
    {
        var feedbacks = await _dbContext.Feedbacks.ToListAsync();

        var feedbackId = await AwaitTextInputAsync(TimeSpan.FromSeconds(180), "Введите id отзыва");
        if (feedbackId == null)
        {
            await ResponseAsync("Что то пошло не так, попробуйте позже");
            return;
        }
        
        if (feedbacks.All(x => x.Id.ToString() != feedbackId))
        {
            await ResponseAsync("Нет отзыва с таким id, попробуйте еще раз");
            return;
        }
        
        await using (var transaction = await _dbContext.Database.BeginTransactionAsync())
        {
            try
            {
                await _dbContext.Feedbacks.Where(x => x.Id == Guid.Parse(feedbackId)).ExecuteDeleteAsync();
            
                await _dbContext.SaveChangesAsync();
                await transaction.CommitAsync();
            }
            catch (Exception exception)
            {
                _logger.LogError(exception.Message);
                await ResponseAsync("Что то пошло не так, попробуйте позже");
            }
        }

        await ResponseAsync("Correct");
    }
    
    private async Task OnDeleteCategoryCommandAsync()
    {
        var categories = await _dbContext.ShoppingCategories.ToListAsync();

        var buttons = new List<InlineKeyboardButton>();
        foreach (var category in categories)
        {
            var button = new InlineKeyboardButton(category.Name)
            {
                CallbackData = _callbackGenerateHelper.GenerateCategoriesCallbackFormatStringOnDelete(category.Name)
            };
            buttons.Add(button);
        }

        var keyboard = new InlineKeyboardMarkup(buttons);

        await ResponseAsync(
            "Какую категорию вы хотите удалить?\n(При удалении категории, также удалятся и все предметы, которые в ней были)",
            replyMarkup: keyboard);
    }

    private async Task OnDeleteItemCommandAsync()
    {
        var items = await _dbContext.ShoppingItems.ToListAsync();

        var buttons = new List<InlineKeyboardButton>();
        foreach (var item in items)
        {
            var button = new InlineKeyboardButton(item.Name)
            {
                CallbackData = _callbackGenerateHelper.GenerateItemsCallbackFormatStringOnDelete(item.Name)
            };
            buttons.Add(button);
        }

        var keyboard = new InlineKeyboardMarkup(buttons);

        await ResponseAsync("Какой предмет вы хотите удалить?", replyMarkup: keyboard);
    }

    private async Task OnAddItemCommandAsync()
    {
        var responseMessage = "Ok";
        await using (var transaction = await _dbContext.Database.BeginTransactionAsync())
        {
            try
            {
                var item = await GetShoppingItem();
                await _dbContext.ShoppingItems.AddAsync(item);

                await _dbContext.SaveChangesAsync();
                await transaction.CommitAsync();
            }
            catch (Exception exception)
            {
                _logger.LogError(exception.Message);

                responseMessage = "Что-то пошло не так, попробуйте позже";
            }
        }

        await ResponseAsync(responseMessage);
    }


    private async Task OnAddCategoryCommandAsync()
    {
        var name = await AwaitTextInputAsync(TimeSpan.FromSeconds(180), "Введите название категории");
        var description = await AwaitTextInputAsync(TimeSpan.FromSeconds(180), "Введите описание категории");

        if (name is null || description is null) return;

        var category = ShoppingCategory.Create(name, description);

        await using (var transaction = await _dbContext.Database.BeginTransactionAsync())
        {
            try
            {
                _dbContext.ShoppingCategories.Add(category);

                await _dbContext.SaveChangesAsync();
                await transaction.CommitAsync();
            }
            catch (Exception exception)
            {
                _logger.LogError(exception.Message);

                var responseOnServerError = "Что-то пошло не так, попробуйте позже";
                await ResponseAsync(responseOnServerError);
            }
        }
    }

    #endregion

    #region UserCommands

    private async Task OnGetCartCommandAsync(long telegramUserId)
    {
        var user = await _dbContext.Users
            .Include(x => x.Cart)
            .ThenInclude(x => x.ItemsAdded)
            .FirstOrDefaultAsync(x => x.TelegramId == telegramUserId);
        if (user is null)
        {
            await ResponseAsync("Вы не зарегестрированны");
            return;
        }

        if (user.Cart.ItemsAdded.Count == 0)
        {
            await ResponseAsync("У вас пока нет товаров в карзине");
            return;
        }
        // string baseMessageResponse = GenerateCartAsStringResponse(user.Cart);
        // await ResponseAsync(baseMessageResponse);

        await ResponseCartAsync(user.Cart, telegramUserId);
    }

    private async Task<Feedback> GenerateNewFeedbackAsync(Internal_User user)
    {
        var title = await AwaitTextInputAsync(TimeSpan.FromSeconds(180), "Введите название отзыва") ??
                    throw new Exception();
        var rate =
            await GetIntValueAsync(TimeSpan.FromSeconds(180), "Введите вашу оценку нашего товара (от 1 до 10)") ??
            throw new Exception();
        if (rate is < 1 or > 10) throw new Exception("Rate must be between 1 and 10");
        var text = await AwaitTextInputAsync(TimeSpan.FromSeconds(180), "Введите текст вашего отзыва") ??
                   throw new Exception();

        return Feedback.Create(title, text, rate, user);
    }

    private async Task OnAddFeedbackCommandAsync(long userId)
    {
        var user = await _dbContext.Users.FirstOrDefaultAsync(x => x.TelegramId == userId);
        if (user == null)
        {
            await ResponseAsync("Вы не авторизированы");
            return;
        }

        var feedback = await GenerateNewFeedbackAsync(user);

        await using (var transaction = await _dbContext.Database.BeginTransactionAsync())
        {
            try
            {
                await _dbContext.Feedbacks.AddAsync(feedback);

                await _dbContext.SaveChangesAsync();
                await transaction.CommitAsync();
            }
            catch (Exception exception)
            {
                _logger.LogError(exception.Message);
                await ResponseAsync("Что-то пошло не так, попробуйте позже");
                return;
            }
        }

        await ResponseAsync("Ok");
    }

    private async Task OnGetFeedbackFirstTimeAsync()
    {
        var feedbacks = await _dbContext.Feedbacks
            .OrderByDescending(x => x.Rating)
            .Take(_paginationLimit)
            .ToListAsync();
        if (!feedbacks.Any())
        {
            await ResponseAsync("Пока что никаких отзывов не добавлено");
            return;
        }
        
        var response = GenerateFeedbacksString(feedbacks, 0);

        var count = await _dbContext.Feedbacks.CountAsync();
        if (count > _paginationLimit)
            await ResponseAsync(response, replyMarkup: new InlineKeyboardMarkup([
                new InlineKeyboardButton("Смотреть дальше")
                {
                    CallbackData = _callbackGenerateHelper.GenerateCallbackOnGetFeedbackByPageNumber(1)
                }
            ]));
        else
        {
            await ResponseAsync(response);
        }
    }

    private async Task OnGetCategoriesCommandAsync()
    {
        var categories = await _dbContext.ShoppingCategories.ToListAsync();
        if (categories.Count == 0)
        {
            await ResponseAsync("Пока что никаких категорий не добавлено.");
            return;
        }

        var messageBuilder = new StringBuilder();
        messageBuilder.Append("Наши категории:");

        var buttons = new List<InlineKeyboardButton>();
        foreach (var category in categories)
        {
            var categoryText = GenerateCategoryText(category);
            messageBuilder.Append(categoryText);

            var button = new InlineKeyboardButton(category.Name)
            {
                CallbackData = _callbackGenerateHelper.GenerateCategoriesCallbackFormatStringOnGet(category.Name)
            };

            buttons.Add(button);
        }

        var keyboard = new InlineKeyboardMarkup(buttons);
        

        await ResponseAsync(messageBuilder.ToString(), replyMarkup: keyboard);
    }

    private async Task OnStartCommandAsync(long telegramUserId, string? username)
    {
        var defaultResponseMessage =
            "Добро пожаловать в kanu store!\nМы занимаемся продажей шмотья разных каст (ниже вам будут представленны наши товары, сгрупированные по категориям).\nДля подробностей /info";

        var keyboard = new ReplyKeyboardMarkup(UserCommands.Select(x => new KeyboardButton(x)));

        var containsUser = await _dbContext.Users.AnyAsync(u => u.TelegramId == telegramUserId);
        if (containsUser)
        {
            await ResponseAsync(defaultResponseMessage, replyMarkup: keyboard);
        }
        else
        {
            var welcomeResponse =
                "Добро пожаловать в kanu store!\\nМы занимаемся продажей шмотья разных каст\nЗаполните форму ниже для авторизации.";
            await ResponseAsync(welcomeResponse, replyMarkup: keyboard);

            var age = await GetIntValueAsync(TimeSpan.FromSeconds(180), "Введите ваш возраст");
            if (age == null) return;

            var user = Internal_User.Create(telegramUserId, username, age.Value, false);
            await using (var transaction = await _dbContext.Database.BeginTransactionAsync())
            {
                try
                {
                    _dbContext.Users.Add(user);

                    await _dbContext.SaveChangesAsync();
                    await transaction.CommitAsync();
                }
                catch (Exception exception)
                {
                    _logger.LogError(exception.Message);

                    var responseOnServerError = "Что-то пошло не так, попробуйте позже";
                    await ResponseAsync(responseOnServerError, replyMarkup: keyboard);
                    return;
                }
            }

            await ResponseAsync(defaultResponseMessage, replyMarkup: keyboard);
        }

        var isAdmin = await CheckAdminPermissionAsync(telegramUserId);
        if (isAdmin)
        {
            var adminCommandsMessage = GenerateAdminCommandsString();
            await ResponseAsync(adminCommandsMessage, replyMarkup: keyboard);
        }

        await OnGetCategoriesCommandAsync();
    }

    private async Task OnGetInfoCommandAsync()
    {
        var mainInfo = """
                       Мы занимаемся продажей стильной уличной одежды, для того чтобы вы могли выглядеть как актеры голивуда.
                       Чтобы заказть себе что-нибудь, найдите товар в нашем боте/тгк, и свяжитесь с мене   джером:
                       контакты: @qsz44
                       """;
        await ResponseAsync(mainInfo);

        var otherInfo = """
                        По поводу передачи товара, есть 3 варианта:
                        1. Самомвывоз (мы с вами договариваемся о месте и времяни встречи, там передаем вам вещь, а вы ее оплавиваете) - бесплатно.
                        2. Доствака в пределах москвы (мы можем привести вам вещь курьером, если вы отправите 40% предоплаты, которые, в случае если вам товар не понравится, вернутся обратно к вам) - доп 250р.
                        3. CDEK по всей россии.
                        """;
        await ResponseAsync(otherInfo);
    }

    #endregion

    #endregion

    #region InputHelpingTools

    private async Task<int?> GetIntValueAsync(TimeSpan span, string question)
    {
        int? value = null;

        while (value == null)
            if (int.TryParse(await AwaitTextInputAsync(span,
                    question), out var result))
            {
                value = result;
            }
            else
            {
                var wrongFormatException = "Неправильный формат числовой переменной.";
                await ResponseAsync(wrongFormatException);
            }

        return value;
    }

    private async Task<double?> GetDoubleValueAsync(TimeSpan span, string question)
    {
        double? value = null;

        while (value == null)
            if (double.TryParse(await AwaitTextInputAsync(
                    span, question), out var result))
            {
                value = result;
            }
            else
            {
                var wrongFormatException = "Неправильный формат числовой переменной.";
                await ResponseAsync(wrongFormatException);
            }

        return value;
    }

    #endregion

    #region LogicHelpingTools

    private async Task<ShoppingItem> GetShoppingItem()
    {
        var categoryName = await AwaitTextInputAsync(TimeSpan.FromSeconds(180), "Введите название категории") ??
                           throw new Exception();
        var itemCategory = await _dbContext.ShoppingCategories.FirstOrDefaultAsync(x =>
            x.Name.ToLower() == categoryName.ToLower());
        if (itemCategory == null)
        {
            await ResponseAsync("У нас нет категорий с таким названием");
            throw new Exception();
        }

        var itemName = await AwaitTextInputAsync(TimeSpan.FromSeconds(180), "Введите название товара") ??
                       throw new Exception();
        var itemDescription = await AwaitTextInputAsync(TimeSpan.FromSeconds(180), "Введите описание товара") ??
                              throw new Exception();
        var unitsInStock =
            await GetIntValueAsync(TimeSpan.FromSeconds(180), "Введите количество товара в наличи (число)") ??
            throw new Exception();
        var price = await GetDoubleValueAsync(TimeSpan.FromSeconds(180), "Введите цену товара") ??
                    throw new Exception();


        await ResponseAsync("Отправьте фотографии товара (1-9)");
        var result = await GetPhotosFromUserAsync(itemName);
        if (result.Count == 0) throw new Exception();


        return ShoppingItem.Create(itemName, itemDescription, price, unitsInStock, itemCategory, result);
    }

    private async Task<List<string>> GetPhotosFromUserAsync(string itemName)
    {
        var photoFileNames = new List<string>();

        var container = await AwaitMessageAsync(new Filter<Message>(), TimeSpan.FromSeconds(1080));
        while (container?.Update != null)
        {
            if (photoFileNames.Count >= 9) break;

            if (container.Container.Message?.Photo == null) await ResponseAsync("Wrong photo format");

            var photos = container!.Container.Message!.Photo!;
            var photo = photos.Last();


            var fileName = Guid.NewGuid().ToString();
            photoFileNames.Add(fileName);

            await _photoDownloadHelper.DownloadFile(photo.FileId, itemName, fileName);

            container = await AwaitMessageAsync(new Filter<Message>(), TimeSpan.FromSeconds(1));
        }

        return photoFileNames;
    }

    #endregion

    #region EverysingElseHelping

    private async Task ResponseCartAsync(Cart cart, long userId)
    {
        await ResponseAsync("Ваша карзина:");
        foreach (var item in cart.ItemsAdded)
            try
            {
                await SendResponsePhotosForShoppingItemAsync(item, userId);
            }
            catch (Exception exception)
            {
                _logger.LogError(exception.Message);
                await ResponseAsync("Что то пошло не так, попробуйте позже");
                return;
            }

        await ResponseAsync($"Общая сумма к оплате составит {cart.ItemsAdded.Sum(x => x.Price)}р");
    }

    private async Task GetResponsePhotosForSingleShoppingAsync(ShoppingItem shoppingItem, long userId)
    {
        var responseMessage = GenerateShortShoppingItemMessage(shoppingItem);

        var photoPathUrl =
            _photoDownloadHelper.GenerateFilePathString(shoppingItem.PhotoFileNames[0], shoppingItem.Name);

        if (!IO_File.Exists(photoPathUrl))
        {
            await BotClient.SendTextMessageAsync(userId, "Что-то пошло не так с загрузкой фотографий.");
            await BotClient.SendTextMessageAsync(userId, responseMessage);
            throw new Exception("No such files with this path");
        }

        using (Stream fileStream = IO_File.Open(photoPathUrl, FileMode.Open, FileAccess.Read, FileShare.Read))
        {
            InputOnlineFile file = new InputMedia(fileStream, shoppingItem.PhotoFileNames[0]);
            await BotClient.SendPhotoAsync(userId, file, responseMessage, replyMarkup: new InlineKeyboardMarkup([
                new InlineKeyboardButton("Удалить из карзины")
                {
                    CallbackData = _callbackGenerateHelper.GenerateCallbackOnRemoveFromCart(shoppingItem.Name)
                }
            ]));
        }
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

    private async Task SendResponsePhotosForShoppingItemAsync(ShoppingItem shoppingItem, long userId)
    {
        if (shoppingItem.PhotoFileNames.Count == 0) throw new Exception("No such photos (business logic mistake)");

        await GetResponsePhotosForSingleShoppingAsync(shoppingItem, userId);
    }

    /// <summary>
    ///     Generates response based on user role, that shows available commands
    /// </summary>
    private string GenerateAdminCommandsString()
    {
        var messageBuilder = new StringBuilder();
        messageBuilder.Append("Вы админ, вот ваши админ-команды:\n");

        foreach (var command in AdminCommands) messageBuilder.Append($"\n{command}");
        return messageBuilder.ToString();
    }

    private static string GenerateCategoryText(ShoppingCategory category)
    {
        return $"\n\n{category.Name}: \n{category.Description}";
    }

    private string GenerateShortShoppingItemMessage(ShoppingItem item)
    {
        var messageBuilder = new StringBuilder();

        messageBuilder.Append($"{item.Name} - {item.Price}р\n");

        return messageBuilder.ToString();
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
}