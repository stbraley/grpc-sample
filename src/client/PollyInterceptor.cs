using System;
using System.Linq;
using System.Threading.Tasks;
using Grpc.Core;
using Grpc.Core.Interceptors;
using Microsoft.Extensions.DependencyInjection;
using Polly;
using Serilog;

namespace client
{
    public class PollyInterceptor : Interceptor
    {
        private readonly IServiceProvider _serviceProvider;
        
        private static readonly StatusCode[] GRpcErrors = new[] {
            StatusCode.DeadlineExceeded,
            StatusCode.Internal,
            StatusCode.NotFound,
            StatusCode.ResourceExhausted,
            StatusCode.Unavailable,
            StatusCode.Unknown
        };

        public PollyInterceptor(IServiceProvider serviceProvider)
        {
            _serviceProvider = serviceProvider;
        }

        public override AsyncUnaryCall<TResponse> AsyncUnaryCall<TRequest, TResponse>(
            TRequest request,
            ClientInterceptorContext<TRequest, TResponse> context,
            AsyncUnaryCallContinuation<TRequest, TResponse> continuation)
        {
            var call = continuation(request, context);
            return new AsyncUnaryCall<TResponse>(HandleResponse(call.ResponseAsync), call.ResponseHeadersAsync, call.GetStatus, call.GetTrailers, call.Dispose);
        }

        private async Task<TResponse> HandleResponse<TResponse>(Task<TResponse> t)
        {
            return await Policy
                .Handle<RpcException>((ex) => GRpcErrors.Contains( ex.StatusCode ))
                .WaitAndRetryAsync(
                    3,
                    (input) => TimeSpan.FromSeconds(3 + input),
                    (outcome, timespan, retryAttempt, context) =>
                    {
                        if (outcome is RpcException rpcException)
                        {
                            _serviceProvider.GetService<ILogger>()?
                                .Error("Request Failed with {status} Retry in {seconds}", rpcException.StatusCode, timespan);
                        }
                    }
                )
                .ExecuteAsync( async () =>
                {
                    return await t;
                });
        }
    }
}