using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using ShopTelegramBot.Abstract;
using ShopTelegramBot.Database;
using ShopTelegramBot.Extensions;
using ShopTelegramBot.Handlers;
using ShopTelegramBot.Helpers;
using ShopTelegramBot.HelpingModels;
using Telegram.Bot;
using Telegram.Bot.Types;
using Telegram.Bot.Types.Enums;
using TelegramUpdater;
using TelegramUpdater.Hosting;
using PathHelper = ShopTelegramBot.Helpers.PathHelper;


IHostBuilder builder = Host.CreateDefaultBuilder();

builder.ConfigureHostConfiguration(configuration =>
{
   IPathHelper pathHelper = new PathHelper();
   
   string appsettingsFilePath = @"appsettings.json";
   
   configuration.SetBasePath(pathHelper.GetProjectDirectoryPath())
      .AddJsonFile(appsettingsFilePath);
});


builder.ConfigureServices((hostContext, services) =>
{
   services.Configure<CharReplacingSettings>(hostContext.Configuration.GetSection(nameof(CharReplacingSettings)));
   
   services.AddDbContext<ApplicationDbContext>();

   services.AddScoped<IPathHelper, PathHelper>();
   services.AddScoped<IPhotoDownloadHelper, PhotoDownloadHelper>();
   services.AddScoped<ICallbackGenerateHelper, CallbackGenerateHelper>();
   
   ConfigureTelegramUpdater(services, hostContext.Configuration);
});


IHost host = builder.Build();

await host.RunAsync();
return;



void ConfigureTelegramUpdater(IServiceCollection services, IConfiguration configuration)
{
   string token = configuration.GetSection("TelegramBotToken").Value ??
                  throw new Exception("Server error: no telegram bot token configured");

   TelegramBotClient client = new TelegramBotClient(token);

   services.AddHttpClient("TelegramBotClient").AddTypedClient<ITelegramBotClient>(httpClient => client);

   UpdaterOptions updaterOptions = new UpdaterOptions(maxDegreeOfParallelism: 10, 
      allowedUpdates: [UpdateType.Message, UpdateType.CallbackQuery]);

   services.AddTelegramUpdater(client, updaterOptions, botBuilder =>
   {
      botBuilder.AddDefaultExceptionHandler()
         .AddScopedUpdateHandler<ScopedMessageHandler, Message>()
         .AddScopedUpdateHandler<ScopedCallbackHandler, CallbackQuery>();
   });
}


