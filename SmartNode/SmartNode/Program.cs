using Logic.CaseRepository;
using Logic.FactoryInterface;
using Logic.Mapek;
using Logic.Models.DatabaseModels;
using Logic.Models.MapekModels;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using MongoDB.Driver;
using SmartNode.Streaming;
using System.CommandLine;
using System.Reflection;
using System.Runtime.CompilerServices;

[assembly: InternalsVisibleTo("TestProject")]

namespace SmartNode
{
    internal class Program
    {
        public static FilepathArguments? FilepathArguments;

        static async Task<int> Main(string[] args)
        {
            RootCommand rootCommand = new();
            Option<string> fileNameArg = new("--appsettings")
            {
                Description = "Which appsettings file to use."
            };
            rootCommand.Add(fileNameArg);
            Option<string> baseDirName = new("--basedir")
            {
                Description = "The base directory for models etc. Used as prefix for all relative paths in `appsettings`."
            };
            rootCommand.Add(baseDirName);

            Argument<string> inferredModelArg = new Argument<string>("inferred-model")
            {
                Description = "The path to the inferred model file.",
                Arity = ArgumentArity.ZeroOrOne
            };
            rootCommand.Add(inferredModelArg);

            ParseResult parseResult = rootCommand.Parse(args);
            string? settingsFile = parseResult.GetValue(fileNameArg);
            string? baseDir = parseResult.GetValue(baseDirName);
            string? positionalInferredModel = parseResult.GetValue(inferredModelArg);

            var appSettings = settingsFile is not null ? Path.Combine("Properties", settingsFile) : Path.Combine("Properties", $"appsettings.json");

            var builder = Host.CreateApplicationBuilder(args);
            builder.Configuration.AddJsonFile(appSettings);

            var filepathArguments = builder.Configuration.GetSection("FilepathArguments").Get<FilepathArguments>();
            var coordinatorSettings = builder.Configuration.GetSection("CoordinatorSettings").Get<CoordinatorSettings>();
            var databaseSettings = builder.Configuration.GetSection("DatabaseSettings").Get<DatabaseSettings>();
            var streamingOptions = builder.Configuration.GetSection("StreamingWebSocket").Get<StreamingWebSocketOptions>() ?? new StreamingWebSocketOptions();

            string? rootDirectory;
            try {
                var location = Directory.GetParent(Assembly.GetExecutingAssembly().Location);
                if (baseDir != null) {
                    rootDirectory = baseDir;
                } else if (Directory.Exists("/app")) {
                    rootDirectory = "/app"; // Standard Docker path
                } else {
                    // Dev environment: climb out of bin/Debug/net8.0
                    rootDirectory = location?.Parent?.Parent?.Parent?.Parent?.Parent?.FullName ?? Directory.GetCurrentDirectory();
                }
            } catch {
                rootDirectory = Directory.GetCurrentDirectory();
            }

            // TODO: we can use reflection for this.
            // Fix full paths.
            filepathArguments!.OntologyFilepath = Path.GetFullPath(Path.Combine(rootDirectory, filepathArguments.OntologyFilepath));
            filepathArguments.FmuDirectory = Path.GetFullPath(Path.Combine(rootDirectory, filepathArguments.FmuDirectory));
            filepathArguments.DataDirectory = Path.GetFullPath(Path.Combine(rootDirectory, filepathArguments.DataDirectory));
            filepathArguments.InferenceRulesFilepath = Path.GetFullPath(Path.Combine(rootDirectory, filepathArguments.InferenceRulesFilepath));
            filepathArguments.InstanceModelFilepath = Path.GetFullPath(Path.Combine(rootDirectory, filepathArguments.InstanceModelFilepath));
            filepathArguments.InferredModelFilepath = Path.GetFullPath(Path.Combine(rootDirectory, filepathArguments.InferredModelFilepath));
            filepathArguments.InferenceEngineFilepath = Path.GetFullPath(Path.Combine(rootDirectory, filepathArguments.InferenceEngineFilepath));

            // In Docker, FMUs are moved to /app by the Dockerfile.
            if (Directory.Exists("/app") && File.Exists("/app/roomM370.fmu")) {
                filepathArguments.FmuDirectory = "/app";
            }

            FilepathArguments = filepathArguments;

            // Override with positional argument if provided (matches Docker example)
            if (!string.IsNullOrEmpty(positionalInferredModel)) {
                filepathArguments.InferredModelFilepath = positionalInferredModel;
            }

            // Register services here.
            builder.Services.AddLogging(loggingBuilder =>
            {
                loggingBuilder.AddConsole(options => options.TimestampFormat = "HH:mm:ss ");
            });
            builder.Services.AddSingleton(filepathArguments);
            builder.Services.AddSingleton(coordinatorSettings!);
            builder.Services.AddSingleton(databaseSettings!);
            builder.Services.AddSingleton(streamingOptions);
            // Register a factory to allow for dynamic constructor argument passing through DI.
            builder.Services.AddSingleton<IMongoClient, MongoClient>(serviceProvider => new MongoClient(databaseSettings!.ConnectionString));
            builder.Services.AddSingleton<ICaseRepository, CaseRepository>(serviceProvider => new CaseRepository(serviceProvider));
            builder.Services.AddSingleton<IFactory, Factory>(serviceProvider => new Factory(coordinatorSettings!.Environment));
            builder.Services.AddSingleton<IMapekMonitor, MapekMonitor>(serviceProvider => new MapekMonitor(serviceProvider));

            // @DAT191 Streaming-server goes >HERE<
            builder.Services.AddSingleton<WebSocketStreamingSimulationProvider>();
            builder.Services.AddSingleton<IStreamingSimulationProvider>(serviceProvider => serviceProvider.GetRequiredService<WebSocketStreamingSimulationProvider>());
            builder.Services.AddSingleton<IStreamingTelemetryHub>(serviceProvider => serviceProvider.GetRequiredService<WebSocketStreamingSimulationProvider>());
            builder.Services.AddHostedService<StreamingWebSocketServer>();
            
            builder.Services.AddSingleton<IMapekPlan, MapekPlan>(serviceProvider => {
                return coordinatorSettings!.UseEuclid ? new EuclidMapekPlan(serviceProvider) : new MapekPlan(serviceProvider);
            });
            builder.Services.AddSingleton<IBangBangPlanner, BangBangPlanner>(serviceProvider => new BangBangPlanner(serviceProvider));
            builder.Services.AddSingleton<IMapekExecute, MapekExecute>(serviceProvider => new MapekExecute(serviceProvider));
            builder.Services.AddSingleton<IMapekKnowledge, MapekKnowledge>(serviceProvider => new MapekKnowledge(serviceProvider));
            builder.Services.AddSingleton<IMapekManager, MapekManager>(serviceprovider => new MapekManager(serviceprovider));

            using var host = builder.Build();

            var logger = host.Services.GetRequiredService<ILogger<Program>>();
            await host.StartAsync();

            // Get an instance of the MAPE-K manager.
            var mapekManager = host.Services.GetRequiredService<IMapekManager>();

            // Start the loop.
            try
            {
                await mapekManager.StartLoop();
            }
            catch (Exception exception)
            {
                logger.LogCritical(exception, "Exception");
                throw;
            }
            finally
            {
                await host.StopAsync();
            }

            // XXX review
            // host.Run();
            logger.LogInformation("MAPE-K ended.");
            return 0;
        }
    }
}
