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
    public ScopedMessageHandler(ApplicationDbContext dbContext, ILogger<ScopedMessageHandler> logger, IPhotoDownloadHelper photoDownloadHelper, IOptions<CharReplacingSettings> replacingSettings)
    {
        _dbContext = dbContext;
        _logger = logger;
        _photoDownloadHelper = photoDownloadHelper;
        _specialSymbol = replacingSettings.Value.SpecialSymbol[0];
    }

    private readonly ApplicationDbContext _dbContext;
    private readonly ILogger<ScopedMessageHandler> _logger;
    private readonly IPhotoDownloadHelper _photoDownloadHelper;

    private readonly char _specialSymbol;

    private static readonly string[] UserCommands = ["/start", "/categories"];
    private static readonly string[] AdminCommands = ["/add_category", "/add_item"];
    
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
                    await ResponseAsync("You have no admin permissions for this command");
                    break;
                }
                await OnAddCategoryCommandAsync();
                break;
            case "/add_item":
                if (!await CheckAdminPermissionAsync(message.Chat.Id))
                {
                    await ResponseAsync("You have no admin permissions for this command");
                    break;
                }
                await OnAddItemCommandAsync();
                break;
        }
    }
    
    private async Task OnTextMessage(Message message)
    {
        string response = "Wrong command format.";
        await ResponseAsync(response);
    }

    #region Commands

    #region AdminCommands

    private async Task OnAddItemCommandAsync()
    {
        string responseMessage = "Everything is correct";
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

                responseMessage = "Something went wrong, try again latter";
            }
        }

        await ResponseAsync(responseMessage);
    }
    

    private async Task OnAddCategoryCommandAsync()
    {
        string? name = await AwaitTextInputAsync(TimeSpan.FromSeconds(180), "Enter category name");
        string? description = await AwaitTextInputAsync(TimeSpan.FromSeconds(180), "Enter category description");

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
                
                string responseOnServerError = "Sorry something went wrong, try again latter";
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
            await ResponseAsync("Sorry, right now no categories.");
            return;
        }
        
        StringBuilder messageBuilder = new StringBuilder();
        messageBuilder.Append("This is our categories:");

        List<InlineKeyboardButton> buttons = new List<InlineKeyboardButton>();
        foreach (ShoppingCategory category in categories)
        {
            string categoryText = GenerateCategoryText(category);
            messageBuilder.Append(categoryText);

            InlineKeyboardButton button = new InlineKeyboardButton(category.Name)
            {
                CallbackData = GenerateCategoriesCallbackFormatString(category.Name)
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
            string response = "Welcome back to out bot! \nYou are already registered";
            await ResponseAsync(response);
        }
        else
        {
            string welcomeResponse = "Welcome back to out bot! \nLet's answer some registration questions.";
            await ResponseAsync(welcomeResponse);

            int? age = await GetIntValueAsync("1. Whats your age?");
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
                
                    string responseOnServerError = "Sorry something went wrong, try again latter";
                    await ResponseAsync(responseOnServerError);
                    return;
                }
            }
        
            string messageText = "Welcome to our bot! \nThank you for registration!!";
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
                string wrongFormatException = "Wrong age format";
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
                string wrongFormatException = "Wrong age format";
                await ResponseAsync(wrongFormatException);
            }
        }

        return value;
    }

    #endregion
    
    #region LogicHelpingTools
    private async Task<ShoppingItem> GetShoppingItem()
    {
        string categoryName = await AwaitTextInputAsync(TimeSpan.FromSeconds(180), "Enter category name") ?? throw new Exception();
        ShoppingCategory? itemCategory = await _dbContext.ShoppingCategories.FirstOrDefaultAsync(x =>
            x.Name.ToLower() == categoryName.ToLower());
        if (itemCategory == null)
        {
            await ResponseAsync("No such category with this name.");
            throw new Exception();
        }
        
        string itemName = await AwaitTextInputAsync(TimeSpan.FromSeconds(180), "Enter item name") ?? throw new Exception();
        string itemDescription = await AwaitTextInputAsync(TimeSpan.FromSeconds(180), "Enter item description") ?? throw new Exception();
        int unitsInStock = await GetIntValueAsync("Enter units in stock value") ?? throw new Exception();
        double price = await GetDoubleValueAsync("Enter the price value") ?? throw new Exception();
        
        
        await ResponseAsync("Send items photos");
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
        
        IContainer<Message>? container = await AwaitMessageAsync(new Filter<Message>(), TimeSpan.FromSeconds(180));
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
    
    private static string GenerateCategoriesCallbackFormatString(string x)
    {
        return $"categories/{x.ToLower()}";
    }
    
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