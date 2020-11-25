using System;
using System.Threading;
using System.Threading.Tasks;
using Grpc.Core;
using Grpc.Net.Client;
using Microsoft.Extensions.Hosting;
using Polly;
using Serilog;
using server;

namespace client
{
    public class ConsoleHostedService : IHostedService
    {
        private readonly IHostApplicationLifetime _appLifetime;
        private readonly ILogger _logger;
        private int? _exitCode;

        public ConsoleHostedService(IHostApplicationLifetime appLifetime, ILogger logger)
        {
            _appLifetime = appLifetime;
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
                        _logger.Information("Hello grpc!");

                        var channel = GrpcChannel.ForAddress("https://localhost:5001");
                        var client = new Greeter.GreeterClient(channel);

                        var reply = await Policy.Handle<RpcException>((rpcException) =>
                            {
                                _logger.Error(rpcException,"Failed to call Grpc service. {Status}", rpcException.StatusCode);
                                return rpcException.StatusCode == StatusCode.DeadlineExceeded;
                            }).WaitAndRetryAsync(3, (input) =>
                            {
                                _logger.Information("Retrying GRPC call in {seconds}", input + 3);

                                channel.Dispose();
                                channel = GrpcChannel.ForAddress("https://localhost:5001");
                                client = new Greeter.GreeterClient(channel);

                                return TimeSpan.FromSeconds(3 + input);
                            })
                            .ExecuteAsync<HelloReply>(async () =>
                            {
                                return await client.SayHelloAsync(
                                    request: new HelloRequest { Name = "Me!" },
                                    deadline: DateTime.Now.AddSeconds(1).ToUniversalTime()
                                );
                            });


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
