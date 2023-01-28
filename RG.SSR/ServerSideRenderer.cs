using Microsoft.AspNetCore.Html;
using System.Reflection;

namespace RG.SSR
{
    public sealed class ServerSideRenderer
    {
        private readonly IReactRenderer _reactRenderer;
        private readonly IPreactRenderer _preactRenderer;

        public ServerSideRenderer(
            IReactRenderer reactRenderer,
            IPreactRenderer preactRenderer
        )
        {
            _reactRenderer = reactRenderer;
            _preactRenderer = preactRenderer;
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

        public HtmlString RenderPreact(string componentName, bool isStatic = false)
        {
            return new HtmlString(
                value: _preactRenderer.Render(
                    componentAssembly: Assembly.GetCallingAssembly(),
                    componentName,
                    isStatic
                )
            );
        }

        public HtmlString RenderPreact<TProps>(string componentName, TProps props, bool isStatic = false)
        {
            return new HtmlString(
                value: _preactRenderer.Render(
                    componentAssembly: Assembly.GetCallingAssembly(),
                    componentName,
                    props,
                    isStatic
                )
            );
        }
    }
}
