using Microsoft.Extensions.DependencyInjection;

namespace Naiad.Blazor;

public static class NaiadBlazorServiceCollectionExtensions
{
    public static IServiceCollection AddNaiadBlazor(
        this IServiceCollection services,
        Action<NaiadBlazorOptions>? configure = null)
    {
        ArgumentNullException.ThrowIfNull(services);

        if (configure is null)
        {
            services.AddOptions<NaiadBlazorOptions>();
            return services;
        }

        services.Configure(configure);
        return services;
    }
}
