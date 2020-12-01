using System;
using System.Net.Http;
using Microsoft.Extensions.DependencyInjection;
using Polly;

namespace client
{
    public static class HttpClientBuilderExtensions
    {
        public static IHttpClientBuilder AddPolicyInterceptor(
            this IHttpClientBuilder builder)
        {  
            if (builder == null)
            {
                throw new ArgumentNullException(nameof(builder));
            }

            builder.Services.AddTransient<PollyInterceptor>();
            builder.AddInterceptor<PollyInterceptor>();
         
            return builder;
        }
    }
}
