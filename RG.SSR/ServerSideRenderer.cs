using Microsoft.AspNetCore.Html;
using System.Reflection;

namespace RG.SSR
{
    public sealed class ServerSideRenderer
    {
        private readonly IReactRenderer _reactRenderer;

        public ServerSideRenderer(
            IReactRenderer reactRenderer
        )
        {
            _reactRenderer = reactRenderer;
        }

        public HtmlString RenderReact(string componentName, bool isStatic = false)
        {
            return new HtmlString(
                value: _reactRenderer.Render(
                    componentAssembly: Assembly.GetCallingAssembly(),
                    componentName,
                    isStatic
                )
            );
        }

        public HtmlString RenderReact<TProps>(string componentName, TProps props, bool isStatic = false)
        {
            return new HtmlString(
                value: _reactRenderer.Render(
                    componentAssembly: Assembly.GetCallingAssembly(),
                    componentName,
                    props,
                    isStatic
                )
            );
        }
    }
}
