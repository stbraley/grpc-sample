using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Hosting;
using Serilog;
using server;

namespace client
{
    public class ConsoleHostedService : IHostedService
    {
        private readonly IHostApplicationLifetime _appLifetime;
        private readonly ILogger _logger;
        private int? _exitCode;
        //private readonly GreeterService _greeterService;
        private readonly Greeter.GreeterClient _greeterService;

        public ConsoleHostedService(IHostApplicationLifetime appLifetime, ILogger logger, Greeter.GreeterClient greeterService)
        {
            _appLifetime = appLifetime;
            _greeterService = greeterService;
            _logger = logger;
        }

        public Task StartAsync(CancellationToken cancellationToken)
        {
            _appLifetime.ApplicationStarted.Register(() =>
            {
                Task.Run(async () =>
                {
                    try
                    {
                        var result = await _greeterService.SayHelloAsync(
                            new HelloRequest {Name = "ME"},
                            deadline: DateTime.Now.AddSeconds(1).ToUniversalTime());
                            
                       
                        //var result = await _greeterService.SayHello("ME");
                        
                        Console.WriteLine(result.Message);
                        _exitCode = 0;
                    }
                    catch (Exception ex)
                    {
                        _logger.Error(ex, "Unhandled exception!");
                        _exitCode = 1;
                    }
                    finally
                    {
                        // Stop the application once the work is done
                        _appLifetime.StopApplication();
                    }
                });
            });

            return Task.CompletedTask;
        }

        public Task StopAsync(CancellationToken cancellationToken)
        {
            return Task.CompletedTask;
        }
    }
}
