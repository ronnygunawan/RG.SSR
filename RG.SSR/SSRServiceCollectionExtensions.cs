using Microsoft.Extensions.DependencyInjection;
using RG.SSR.JavaScript;
using RG.SSR.Options;
using RG.SSR.Preact;
using RG.SSR.React;

namespace RG.SSR
{
    public static class SSRServiceCollectionExtensions
    {
        public static IServiceCollection AddServerSideRendering(this IServiceCollection services, Action<ServerSideRendererOptions>? configureOptions = null)
        {
            services.AddOptions<ServerSideRendererOptions>();
            services.AddSingleton<ModuleLoader>();
            services.AddScoped<JavaScriptEngine>();
            services.AddTransient<IReactRenderer, ReactRenderer>();
            services.AddTransient<IPreactRenderer, PreactRenderer>();
            services.AddTransient<ServerSideRenderer>();

            if (configureOptions is not null)
            {
                services.PostConfigure(configureOptions);
            }

            return services;
        }
    }
}
