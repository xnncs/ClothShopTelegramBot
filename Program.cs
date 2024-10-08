﻿using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using ShopTelegramBot.Abstract;
using ShopTelegramBot.Database;
using ShopTelegramBot.Handlers;
using ShopTelegramBot.Helpers;
using ShopTelegramBot.HelpingModels;
using Telegram.Bot;
using Telegram.Bot.Types;
using Telegram.Bot.Types.Enums;
using TelegramUpdater;
using TelegramUpdater.Hosting;
using PathHelper = ShopTelegramBot.Helpers.PathHelper;
using Serilog;


var host = Host.CreateDefaultBuilder(args)
    .ConfigureHostConfiguration(configuration =>
    {
        IPathHelper pathHelper = new PathHelper();

        var appsettingsFilePath = @"appsettings.json";

        configuration.SetBasePath(pathHelper.GetProjectDirectoryPath())
                    .AddJsonFile(appsettingsFilePath);
    })
    .ConfigureServices((hostContext, services) =>
    {   
        services.AddSerilog(loggerConfiguration =>
        {
            loggerConfiguration.MinimumLevel.Debug()
                .WriteTo.Console()
                .ReadFrom.Configuration(hostContext.Configuration);
        });
        
        services.Configure<CharReplacingSettings>(hostContext.Configuration.GetSection(nameof(CharReplacingSettings)));

        services.AddDbContext<ApplicationDbContext>();

        services.AddTransient<IPathHelper, PathHelper>();
        services.AddScoped<IPhotoDownloadHelper, PhotoDownloadHelper>();
        services.AddScoped<ICallbackGenerateHelper, CallbackGenerateHelper>();

        ConfigureTelegramUpdater(services, hostContext.Configuration);
    }).Build();

await host.RunAsync();
return;


void ConfigureTelegramUpdater(IServiceCollection services, IConfiguration configuration)
{
    var token = configuration.GetSection("TelegramBotToken").Value ??
                throw new Exception("Server error: no telegram bot token configured");

    var client = new TelegramBotClient(token);

    services.AddHttpClient("TelegramBotClient").AddTypedClient<ITelegramBotClient>(httpClient => client);

    var updaterOptions = new UpdaterOptions(10,
        allowedUpdates: [UpdateType.Message, UpdateType.CallbackQuery]);

    services.AddTelegramUpdater(client, updaterOptions, botBuilder =>
    {
        botBuilder.AddDefaultExceptionHandler()
            .AddScopedUpdateHandler<ScopedMessageHandler, Message>()
            .AddScopedUpdateHandler<ScopedCallbackHandler, CallbackQuery>();
    });
}