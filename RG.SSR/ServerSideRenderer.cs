using Microsoft.AspNetCore.Html;
using System.Reflection;

namespace RG.SSR
{
    public sealed class ServerSideRenderer
    {
        private readonly IReactRenderer _reactRenderer;
        private readonly IPreactRenderer _preactRenderer;
        private readonly ISolidjsRenderer _solidjsRenderer;

        public ServerSideRenderer(
            IReactRenderer reactRenderer,
            IPreactRenderer preactRenderer,
            ISolidjsRenderer solidjsRenderer
        )
        {
            _reactRenderer = reactRenderer;
            _preactRenderer = preactRenderer;
            _solidjsRenderer = solidjsRenderer;
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

        public HtmlString RenderSolidjs(string componentName, bool isStatic = false)
        {
            return new HtmlString(
                value: _solidjsRenderer.Render(
                    componentAssembly: Assembly.GetCallingAssembly(),
                    componentName,
                    isStatic
                )
            );
        }

        public HtmlString RenderSolidjs<TProps>(string componentName, TProps props, bool isStatic = false)
        {
            return new HtmlString(
                value: _solidjsRenderer.Render(
                    componentAssembly: Assembly.GetCallingAssembly(),
                    componentName,
                    props,
                    isStatic
                )
            );
        }
    }
}
