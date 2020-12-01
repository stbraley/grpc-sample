using Microsoft.Extensions.DependencyInjection;

namespace client.Services
{
    public static class Extensions
    {
        public static IServiceCollection AddAppServices(this IServiceCollection serviceCollection)
        {
            serviceCollection.AddTransient<GreeterService>();
            return serviceCollection;
        }
    }
}
