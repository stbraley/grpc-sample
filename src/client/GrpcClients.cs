using System;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Sockets;
using System.Threading.Tasks;
using Grpc.Core;
using Grpc.Core.Interceptors;
using Grpc.Net.Client;
using Microsoft.Extensions.DependencyInjection;
using Polly;
using Polly.Extensions.Http;
using Serilog;

namespace client
{
    public class SimpleInterceptor : Interceptor
    {
        public override AsyncUnaryCall<TResponse> AsyncUnaryCall<TRequest, TResponse>(
            TRequest request,
            ClientInterceptorContext<TRequest, TResponse> context,
            AsyncUnaryCallContinuation<TRequest, TResponse> continuation)
        {
            //LogCall(context.Method);
            //AddCallerMetadata(ref context);


            var call = continuation(request, context);

         
            return new AsyncUnaryCall<TResponse>(HandleResponse(call.ResponseAsync), call.ResponseHeadersAsync, call.GetStatus, call.GetTrailers, call.Dispose);
        }

        private void AddCallerMetadata<TRequest, TResponse>(ref ClientInterceptorContext<TRequest, TResponse> context)
            where TRequest : class
            where TResponse : class
        {
            var headers = context.Options.Headers;

            // Call doesn't have a headers collection to add to.
            // Need to create a new context with headers for the call.
            if (headers == null)
            {
                headers = new Metadata();
                var options = context.Options.WithHeaders(headers);
                context = new ClientInterceptorContext<TRequest, TResponse>(context.Method, context.Host, options);
            }

            // Add caller metadata to call headers
            headers.Add("caller-user", Environment.UserName);
            headers.Add("caller-machine", Environment.MachineName);
            headers.Add("caller-os", Environment.OSVersion.ToString());
        }

        private void LogCall<TRequest, TResponse>(Method<TRequest, TResponse> method)
            where TRequest : class
            where TResponse : class
        {
            var initialColor = Console.ForegroundColor;
            Console.ForegroundColor = ConsoleColor.Green;
            Console.WriteLine($"Starting call. Type: {method.Type}. Request: {typeof(TRequest)}. Response: {typeof(TResponse)}");
            Console.ForegroundColor = initialColor;
        }

        private async Task<TResponse> HandleResponse<TResponse>(Task<TResponse> t)
        {
            try
            {
                var response = await t;
                Console.WriteLine($"Response received: {response}");
                return response;
            }
            catch (Exception ex)
            {
                // Log error to the console.
                // Note: Configuring .NET Core logging is the recommended way to log errors
                // https://docs.microsoft.com/aspnet/core/grpc/diagnostics#grpc-client-logging
                var initialColor = Console.ForegroundColor;
                Console.ForegroundColor = ConsoleColor.Red;
                Console.WriteLine($"Call error: {ex.Message}");
                Console.ForegroundColor = initialColor;

                throw;
            }
        }
    }
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
            collection.AddTransient<SimpleInterceptor>();
            collection
                .AddGrpcClient<server.Greeter.GreeterClient>((sp, o) =>
                {
                    o.Address = new Uri("https://localhost:5001");
                })
                .AddInterceptor<SimpleInterceptor>()
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
