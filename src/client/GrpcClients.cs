using System;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Sockets;
using System.Threading.Tasks;
using Grpc.Core;
using Microsoft.Extensions.DependencyInjection;
using Polly;
using Polly.Extensions.Http;
using Serilog;

namespace client
{
    public static class GrpcClients
    {
        private static ILogger _logger;

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

        public static IServiceCollection AddGrpcClients(this IServiceCollection collection)
        {
            collection
                .AddGrpcClient<server.Greeter.GreeterClient>((sp, o) =>
                {
                    o.Address = new Uri("https://localhost:5001");
                })
                .AddPolicyInterceptor()
                .AddPolicyHandler((services, request) =>
                    HttpPolicyExtensions.HandleTransientHttpError()
                    .Or<RpcException>((ex) =>
                    {
                        return true;
                    })
                    .Or<TaskCanceledException>((ex) =>
                    {

                        return true;
                    })
                    .OrResult(httpMessage =>
                    {
                        var grpcStatus = GetStatusCode(httpMessage);
                        var httpStatusCode = httpMessage.StatusCode;
                        return grpcStatus != null &&
                               ((httpStatusCode == HttpStatusCode.OK && GRpcErrors.Contains(grpcStatus.Value)));
                    })
                    .WaitAndRetryAsync(
                        3,
                        (input) => TimeSpan.FromSeconds(3 + input),
                        (outcome, timespan, retryAttempt, context) =>
                        {
                            var uri = request.RequestUri;
                            var grpcStatus = GetStatusCode(outcome?.Result);

                            services.GetService<ILogger>()?
                                .Error(
                                    "Request {uri} failed with {grpcStatus}. Retry in {seconds}",
                                    uri,
                                    grpcStatus,
                                    timespan);
                        }
                    ));

            return collection;
        }
    }
}
