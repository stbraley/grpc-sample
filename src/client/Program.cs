using System;
using System.Threading.Tasks;
using client.Services;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Serilog;
using Serilog.Core;
using Steeltoe.Discovery.Client;
using Steeltoe.Discovery.Eureka;
using Polly;

namespace client
{
    class Program
    {
        static async Task<int> Main(string[] args)
        {
            int appReturnCode = 0;

            try
            {
                IConfiguration config = new ConfigurationBuilder()
                    .SetBasePath(AppDomain.CurrentDomain.BaseDirectory)
                    .AddJsonFile("appsettings.json", true)
                    .AddEnvironmentVariables()
                    .AddCommandLine(args)
                    .Build();

                await CreateHostBuilder(args, config)
                    .Build()
                    .RunAsync();
            }
            catch (Exception e)
            {
                
                if (Log.Logger == Logger.None)
                {
                    Console.WriteLine("Exception occurred before logger was created.");
                    Console.WriteLine($"{e.GetType()}: {e.Message}");
                    Console.WriteLine(e.StackTrace);
                }
                else
                {
                    Log.Logger.Error(e, "Global Error {Name} exiting", "GRPC Client");
                }
            }

            return appReturnCode;
        }

        public static IHostBuilder CreateHostBuilder(string[] args, IConfiguration configuration) =>
            Host.CreateDefaultBuilder(args)
                .UseEnvironment(configuration["Environment"])
                .ConfigureHostConfiguration(builder => { builder.AddConfiguration(configuration); })
                .UseConsoleLifetime()
                .ConfigureServices((hostContext, services) =>
                {
                    services.AddDiscoveryClient(configuration);
                    services.AddHostedService<ConsoleHostedService>();
                    services.AddSingleton<ILogger>(Log.Logger);
                    services.AddGrpc();

                    //services.AddAppServices();
                    services.AddGrpcClients();

                })
                .UseSerilog((builderContext, loggerConfiguration) =>
                {
                    loggerConfiguration.ReadFrom.Configuration(builderContext.Configuration);
                });
    }
}
