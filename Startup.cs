
namespace Opc.Ua.Cloud.Publisher
{
    using Microsoft.AspNetCore.Builder;
    using Microsoft.AspNetCore.Hosting;
    using Microsoft.Extensions.Configuration;
    using Microsoft.Extensions.DependencyInjection;
    using Microsoft.Extensions.Hosting;
    using Microsoft.Extensions.Logging;
    using System;
    using System.IO;
    using System.Threading.Tasks;
    using Opc.Ua.Cloud.Publisher.Configuration;
    using Opc.Ua.Cloud.Publisher.Interfaces;

    public class Startup
    {
        public Startup(IConfiguration configuration)
        {
            Configuration = configuration;
        }

        public IConfiguration Configuration { get; }

        // This method gets called by the runtime. Use this method to add services to the container.
        public void ConfigureServices(IServiceCollection services)
        {
            services.AddSession(options => {
                options.IdleTimeout = TimeSpan.FromMinutes(5);
            });

            services.AddControllersWithViews();

            services.AddSignalR();

            string logFilePath = Configuration["LOG_FILE_PATH"];
            if (string.IsNullOrEmpty(logFilePath))
            {
                logFilePath = "./logs/UACloudPublisher.log";
            }
            services.AddLogging(logging =>
            {
                logging.AddFile(logFilePath);
            });

            // add our singletons
            services.AddSingleton<IUAApplication, UAApplication>();
            services.AddSingleton<IUAClient, UAClient>();
            
            if (!string.IsNullOrEmpty(Configuration["USE_KAFKA"]))
            {
                services.AddSingleton<IBrokerClient, KafkaClient>();
            }
            else
            {
                services.AddSingleton<IBrokerClient, MQTTClient>();
            }

            services.AddSingleton<IPublishedNodesFileHandler, PublishedNodesFileHandler>();
            services.AddSingleton<ICommandProcessor, CommandProcessor>();
            services.AddSingleton<OpcSessionHelper>();

            // add our message processing engine
            services.AddSingleton<IMessageProcessor, MessageProcessor>();
            services.AddSingleton<IMessageSource, MonitoredItemNotification>();
            services.AddSingleton<IMessageEncoder, PubSubTelemetryEncoder>();
            services.AddSingleton<IMessagePublisher, StoreForwardPublisher>();

            // setup file storage
            switch (Configuration["STORAGE_TYPE"])
            {
                case "Azure": services.AddSingleton<IFileStorage, AzureFileStorage>(); break;
                default: services.AddSingleton<IFileStorage, LocalFileStorage>(); break;
            }
        }

        // This method gets called by the runtime. Use this method to configure the HTTP request pipeline.
        public void Configure(IApplicationBuilder app,
                              IWebHostEnvironment env,
                              ILoggerFactory loggerFactory,
                              IUAApplication uaApp,
                              IMessageProcessor engine,
                              IBrokerClient subscriber,
                              IPublishedNodesFileHandler publishedNodesFileHandler,
                              IFileStorage storage)
        {
            ILogger logger = loggerFactory.CreateLogger("Statup");

            if (env.IsDevelopment())
            {
                app.UseDeveloperExceptionPage();
            }
            else
            {
                app.UseExceptionHandler("/Browser/Error");

                // The default HSTS value is 30 days. You may want to change this for production scenarios, see https://aka.ms/aspnetcore-hsts.
                app.UseHsts();
            }

            app.UseHttpsRedirection();

            app.UseStaticFiles();

            app.UseSession();

            app.UseRouting();

            app.UseEndpoints(endpoints =>
            {
                endpoints.MapControllerRoute(
                    name: "default",
                    pattern: "{controller=Home}/{action=Index}/{id?}");
                endpoints.MapHub<StatusHub>("/statushub");
            });

            // create our app
            uaApp.CreateAsync().GetAwaiter().GetResult();

            // kick off the task to show periodic diagnostic info
            _ = Task.Run(() => Diagnostics.Singleton.RunAsync());

            // connect to broker
            subscriber.Connect();

            // run the telemetry engine
            _ = Task.Run(() => engine.Run());

            // load our persistency file
            if (Settings.Instance.AutoLoadPersistedNodes)
            {
                try
                {
                    string persistencyFilePath = storage.FindFileAsync(Path.Combine(Directory.GetCurrentDirectory(), "settings"), "persistency.json").GetAwaiter().GetResult();
                    byte[] persistencyFile = storage.LoadFileAsync(persistencyFilePath).GetAwaiter().GetResult();
                    if (persistencyFile == null)
                    {
                        // no file persisted yet
                        logger.LogInformation("Persistency file not found.");
                    }
                    else
                    {
                        logger.LogInformation($"Parsing persistency file...");
                        publishedNodesFileHandler.ParseFile(persistencyFile);
                        logger.LogInformation("Persistency file parsed successfully.");
                    }
                }
                catch (Exception ex)
                {
                    logger.LogError(ex, "Persistency file not loaded!");
                }
            }
        }
    }
}
