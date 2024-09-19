using System.ComponentModel;
using System.Text;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using ShopTelegramBot.Abstract;
using ShopTelegramBot.Database;
using ShopTelegramBot.HelpingModels;
using ShopTelegramBot.Models;
using Telegram.Bot.Types;
using Telegram.Bot.Types.Enums;
using Telegram.Bot.Types.ReplyMarkups;
using TelegramUpdater;
using TelegramUpdater.UpdateContainer;
using TelegramUpdater.UpdateHandlers.Scoped.ReadyToUse;
using Internal_User = ShopTelegramBot.Models.User;

namespace ShopTelegramBot.Handlers;

public class ScopedMessageHandler : MessageHandler
{
    public ScopedMessageHandler(ApplicationDbContext dbContext, ILogger<ScopedMessageHandler> logger, IPhotoDownloadHelper photoDownloadHelper, IOptions<CharReplacingSettings> replacingSettings, ICallbackGenerateHelper callbackGenerateHelper)
    {
        _dbContext = dbContext;
        _logger = logger;
        _photoDownloadHelper = photoDownloadHelper;
        _callbackGenerateHelper = callbackGenerateHelper;
        _specialSymbol = replacingSettings.Value.SpecialSymbol[0];
    }

    private readonly ApplicationDbContext _dbContext;
    private readonly ILogger<ScopedMessageHandler> _logger;
    private readonly IPhotoDownloadHelper _photoDownloadHelper;
    private readonly ICallbackGenerateHelper _callbackGenerateHelper;

    private readonly char _specialSymbol;

    private static readonly string[] UserCommands = ["/start", "/categories"];
    private static readonly string[] AdminCommands = ["/add_category", "/add_item", "/delete_category", "/delete_item"];
    
    protected override async Task HandleAsync(IContainer<Message> container)
    {
        if (container.Update.Type != MessageType.Text)
        {
            return;
        }

        Message message = container.Update;
        
        if (message.Text!.StartsWith('/'))
        {
            (string, string) result = GetCommandArgumentsObject(message.Text);
            string command = result.Item1;
            string args = result.Item2;

            await OnCommand(command, args, message);  
        }
        else
        {
            await OnTextMessage(message);
        }
    }

    private static (string, string) GetCommandArgumentsObject(string messageText)
    {
        int spaceIndex = messageText.IndexOf(' ');
        if (spaceIndex < 0)
        {
            spaceIndex = messageText.Length;
        }

        string command = messageText[..spaceIndex].ToLower();
        string args = messageText[spaceIndex..].TrimStart();
        
        return (command, args);
    }
        
    private async Task OnCommand(string command, string args, Message message)
    {
        string adminPermissionsErrorMessage = "У вас нет админ-прав для выполнения этой комманды.";
        switch (command)
        {
            case "/start":
                await OnStartCommandAsync(message.Chat.Id, message.Chat.Username);
                break;
            
            case "/categories":
                await OnGetCategoriesCommandAsync();
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
        }
    }

    private async Task OnTextMessage(Message message)
    {
        string response = "Неправильный формат комманды";
        await ResponseAsync(response);
    }

    #region Commands

    #region AdminCommands

    private async Task OnDeleteCategoryCommandAsync()
    {
        List<ShoppingCategory> categories = await _dbContext.ShoppingCategories.ToListAsync();
        
        List<InlineKeyboardButton> buttons = new List<InlineKeyboardButton>();
        foreach (ShoppingCategory category in categories)
        {
            InlineKeyboardButton button = new InlineKeyboardButton(category.Name)
            {
                CallbackData = _callbackGenerateHelper.GenerateCategoriesCallbackFormatStringOnDelete(category.Name)
            };
            buttons.Add(button);
        }

        InlineKeyboardMarkup keyboard = new InlineKeyboardMarkup(buttons);

        await ResponseAsync("Какую категорию вы хотите удалить?\n(При удалении категории, также удалятся и все предметы, которые в ней были)", replyMarkup: keyboard);
    }
    
    private async Task OnDeleteItemCommandAsync()
    {
        List<ShoppingItem> items = await _dbContext.ShoppingItems.ToListAsync();
        
        List<InlineKeyboardButton> buttons = new List<InlineKeyboardButton>();
        foreach (ShoppingItem item in items)
        {
            InlineKeyboardButton button = new InlineKeyboardButton(item.Name)
            {
                CallbackData = _callbackGenerateHelper.GenerateItemsCallbackFormatStringOnDelete(item.Name)
            };
            buttons.Add(button);
        }

        InlineKeyboardMarkup keyboard = new InlineKeyboardMarkup(buttons);

        await ResponseAsync("Какой предмет вы хотите удалить?", replyMarkup: keyboard);
    }
    
    private async Task OnAddItemCommandAsync()
    {
        string responseMessage = "Ok";
        await using (IDbContextTransaction transaction = await _dbContext.Database.BeginTransactionAsync())
        {
            try
            {
                ShoppingItem item = await GetShoppingItem();
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
        string? name = await AwaitTextInputAsync(TimeSpan.FromSeconds(180), "Введите название категории");
        string? description = await AwaitTextInputAsync(TimeSpan.FromSeconds(180), "Введите описание категории");

        if (name is null || description is null)
        {
            return; 
        }

        ShoppingCategory category = ShoppingCategory.Create(name, description);
        
        await using (IDbContextTransaction transaction = await _dbContext.Database.BeginTransactionAsync())
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
                
                string responseOnServerError = "Что-то пошло не так, попробуйте позже";
                await ResponseAsync(responseOnServerError);
            }
        }
    }

    #endregion

    #region UserCommands

    private async Task OnGetCategoriesCommandAsync()
    {
        List<ShoppingCategory> categories = await _dbContext.ShoppingCategories.ToListAsync();
        if (categories.Count == 0)
        {
            await ResponseAsync("Пока что никаких категорий не добавлено.");
            return;
        }
        
        StringBuilder messageBuilder = new StringBuilder();
        messageBuilder.Append("Наши категории:");

        List<InlineKeyboardButton> buttons = new List<InlineKeyboardButton>();
        foreach (ShoppingCategory category in categories)
        {
            string categoryText = GenerateCategoryText(category);
            messageBuilder.Append(categoryText);

            InlineKeyboardButton button = new InlineKeyboardButton(category.Name)
            {
                CallbackData = _callbackGenerateHelper.GenerateCategoriesCallbackFormatStringOnGet(category.Name)
            };

            buttons.Add(button);
        }

        InlineKeyboardMarkup keyboard = new InlineKeyboardMarkup(buttons);
        

        await ResponseAsync(messageBuilder.ToString(), replyMarkup: keyboard);
    }
    
    private async Task OnStartCommandAsync(long telegramUserId, string? username)
    {
        bool containsUser = await _dbContext.Users.AnyAsync(u => u.TelegramId == telegramUserId);
        if (containsUser)
        {
            string response = "Добро пожаловать в наш магазин вещей! тут надо че то придумать";
            await ResponseAsync(response);
        }
        else
        {
            string welcomeResponse = "Добро пожаловать в наш магазин вещей! тут надо че то придумать\nОтветьте пожалуйста на пару простых вопросов.";
            await ResponseAsync(welcomeResponse);

            int? age = await GetIntValueAsync("1. Сколько вам лет?");
            if (age == null)
            {
                return;
            }
        
            Internal_User user = Internal_User.Create(telegramUserId, username, age.Value, false);
            await using (IDbContextTransaction transaction = await _dbContext.Database.BeginTransactionAsync())
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
                
                    string responseOnServerError = "Что-то пошло не так, попробуйте позже";
                    await ResponseAsync(responseOnServerError);
                    return;
                }
            }
        
            string messageText = "Добро пожаловать в наш магазин вещей! тут надо че то придумать";
            await ResponseAsync(messageText);
        }

        bool isAdmin = await CheckAdminPermissionAsync(telegramUserId);
        string command = GenerateBasedOnRoleResponse(isAdmin);

        await ResponseAsync(command);
    }
    
    #endregion
    
    #endregion
    
    #region InputHelpingTools

    private async Task<int?> GetIntValueAsync(string question)
    {
        int? value = null;

        while (value == null)
        {
            if (int.TryParse(await AwaitTextInputAsync(
                    TimeSpan.FromSeconds(180),
                    question), out int result))
            {
                value = result;
            }
            else
            {
                string wrongFormatException = "Неправильный формат числовой переменной.";
                await ResponseAsync(wrongFormatException);
            }
        }

        return value;
    }
    
    private async Task<double?> GetDoubleValueAsync(string question)
    {
        double? value = null;

        while (value == null)
        {
            if (double.TryParse(await AwaitTextInputAsync(
                    TimeSpan.FromSeconds(180),
                    question), out double result))
            {
                value = result;
            }
            else
            {
                string wrongFormatException = "Неправильный формат числовой переменной.";
                await ResponseAsync(wrongFormatException);
            }
        }

        return value;
    }

    #endregion
    
    #region LogicHelpingTools
    private async Task<ShoppingItem> GetShoppingItem()
    {
        string categoryName = await AwaitTextInputAsync(TimeSpan.FromSeconds(180), "Введите название категории") ?? throw new Exception();
        ShoppingCategory? itemCategory = await _dbContext.ShoppingCategories.FirstOrDefaultAsync(x =>
            x.Name.ToLower() == categoryName.ToLower());
        if (itemCategory == null)
        {
            await ResponseAsync("У нас нет категорий с таким названием");
            throw new Exception();
        }
        
        string itemName = await AwaitTextInputAsync(TimeSpan.FromSeconds(180), "Введите название товара") ?? throw new Exception();
        string itemDescription = await AwaitTextInputAsync(TimeSpan.FromSeconds(180), "Введите описание товара") ?? throw new Exception();
        int unitsInStock = await GetIntValueAsync("Введите количество товара в наличи (число)") ?? throw new Exception();
        double price = await GetDoubleValueAsync("Введите цену товара") ?? throw new Exception();
        
        
        await ResponseAsync("Отправьте фотографии товара (1-9)");
        List<string> result = await GetPhotosFromUserAsync(itemName);
        if (result.Count == 0)
        {
            throw new Exception();
        }


        return ShoppingItem.Create(itemName, itemDescription, price, unitsInStock, itemCategory, result);
    }
    
    private async Task<List<string>> GetPhotosFromUserAsync(string itemName)
    {
        List<string> photoFileNames = new List<string>();
        
        IContainer<Message>? container = await AwaitMessageAsync(new Filter<Message>(), TimeSpan.FromSeconds(1080));
        while (container?.Update != null)
        {
            if (photoFileNames.Count >= 9)
            {
                break;
            }
            
            if (container.Container.Message?.Photo == null)
            {
                await ResponseAsync("Wrong photo format");
            }

            PhotoSize[] photos = container!.Container.Message!.Photo!;
            PhotoSize photo = photos[photos.Length - 1];

            
            string fileName = Guid.NewGuid().ToString();
            photoFileNames.Add(fileName);
            
            await _photoDownloadHelper.DownloadFile(photo.FileId,itemName, fileName);
            
            container = await AwaitMessageAsync(new Filter<Message>(), TimeSpan.FromSeconds(1));
        }

        return photoFileNames;
    }
    
    #endregion
    
    #region EverysingElseHelping
    
    /// <summary>
    /// Generates response based on user role, that shows available commands
    /// </summary>
    private static string GenerateBasedOnRoleResponse(bool isAdmin)
    {
        StringBuilder messageBuilder = new StringBuilder();
        if (isAdmin)
        {
            messageBuilder.Append("You are admin.\n");
        }

        messageBuilder.Append("Available commands:\n");
        foreach (string command in UserCommands)
        {
            messageBuilder.Append($"\n- {command}");
        }

        if (isAdmin)
        {
            messageBuilder.Append("\n\nAdmin commands:");
            foreach (string command in AdminCommands)
            {
                messageBuilder.Append($"\n- {command}");
            }
        }

        return messageBuilder.ToString();
    }
    
    private static string GenerateCategoryText(ShoppingCategory category)
    {
        return $"\n\n{category.Name}: \n{category.Description}";
    }
    
    
    #endregion

    #region PermissonHelpingTools

    private async Task<bool> CheckAdminPermissionAsync(long telegramUserId)
    {
        Internal_User? user = await _dbContext.Users.FirstOrDefaultAsync(x => x.TelegramId == telegramUserId);
        if (user == null)
        {
            return false;
        }

        return user.IsAdmin;
    }

    #endregion
}