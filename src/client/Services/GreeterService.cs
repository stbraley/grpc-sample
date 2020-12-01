using System;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Sockets;
using System.Threading.Tasks;
using Grpc.Core;
using Grpc.Net.Client;
using Polly;
using Serilog;
using server;

namespace client.Services
{
    public class GreeterService : IDisposable
    {
        private Greeter.GreeterClient _greeterClient;
        private readonly GrpcChannel _grpcChannel;

        private readonly ILogger _logger;

        private static readonly StatusCode[] GRpcErrors = new[] {
            StatusCode.DeadlineExceeded,
            StatusCode.Internal,
            StatusCode.NotFound,
            StatusCode.ResourceExhausted,
            StatusCode.Unavailable,
            StatusCode.Unknown
        };

        private static readonly SocketError[] SocketErrors = new[]
        {
            SocketError.AddressNotAvailable,
            SocketError.ConnectionRefused,
            SocketError.HostNotFound,
            SocketError.HostUnreachable,
            SocketError.HostDown
        };

        public GreeterService(ILogger logger)
        {
            _logger = logger;
            _grpcChannel = GrpcChannel.ForAddress(new Uri("https://localhost:5001"));
            _greeterClient = new Greeter.GreeterClient(_grpcChannel);
        }

        public static StatusCode? GetStatusCode(HttpResponseMessage response)
        {
            if (null == response)
                return StatusCode.Unknown;

            var headers = response.Headers;

            if (!headers.Contains("grpc-status") && response.StatusCode == HttpStatusCode.OK)
                return StatusCode.OK;

            if (headers.Contains("grpc-status"))
                return (StatusCode)int.Parse(headers.GetValues("grpc-status").First());

            return null;
        }

        public async Task<string> SayHello( string name )
        {
            var reply = await Policy
                .HandleInner<SocketException>((socketException) =>
                {
                    _logger.Error(socketException, "Failed to call Grpc service. {SocketStatus}", socketException.SocketErrorCode);
                    return SocketErrors.Contains(socketException.SocketErrorCode);
                })
                .Or<RpcException>((rpcException) =>
                {
                    _logger.Error(rpcException, "Failed to call Grpc service. {Status}", rpcException.StatusCode);
                    return GRpcErrors.Contains(rpcException.StatusCode );
                })
                .WaitAndRetryAsync(
                    3,
                    (input) => TimeSpan.FromSeconds(3 + input),
                    (ex, ts,ctx) =>
                    {
                        _logger.Information("Retrying GRPC call after {seconds}", ts);
                        _greeterClient = new Greeter.GreeterClient(_grpcChannel);
                    })
                .ExecuteAsync<HelloReply>(async () => await _greeterClient.SayHelloAsync(
                    request: new HelloRequest { Name = "Me!"}, 
                    deadline: DateTime.Now.AddSeconds(1).ToUniversalTime()
                ));

            return reply.Message;

        }

        public void Dispose()
        {
            _grpcChannel?.Dispose();
        }
    }
}
