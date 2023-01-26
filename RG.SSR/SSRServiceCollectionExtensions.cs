using Microsoft.Extensions.DependencyInjection;
using RG.SSR.JavaScript;
using RG.SSR.Options;
using RG.SSR.React;

namespace RG.SSR
{
    public static class SSRServiceCollectionExtensions
    {
        public static IServiceCollection AddServerSideRendering(this IServiceCollection services, Action<ServerSideRendererOptions>? configureOptions = null)
        {
            services.AddOptions<ServerSideRendererOptions>();
            services.AddScoped<JavaScriptEngine>();
            services.AddScoped<IReactRenderer, ReactRenderer>();
            services.AddTransient<ServerSideRenderer>();

            if (configureOptions is not null)
            {
                services.PostConfigure(configureOptions);
            }

            return services;
        }
    }
}
