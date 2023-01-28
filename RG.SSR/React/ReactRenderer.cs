using Microsoft.Extensions.Options;
using RG.SSR.EmbeddedResources;
using RG.SSR.JavaScript;
using RG.SSR.Options;
using System.Collections.Concurrent;
using System.Reflection;
using System.Text.Json;

namespace RG.SSR.React
{
    internal sealed class ReactRenderer : IReactRenderer
    {
        private static string? _reactScript;
        private static readonly object _reactScriptLock = new();
        private static string? _reactSsrScript;
        private static readonly object _reactSsrScriptLock = new();
        private static readonly ConcurrentDictionary<string, string> _componentCache = new();

        private static readonly JsonSerializerOptions _propsSerializerOptions = new()
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase
        };

        private readonly JavaScriptEngine _javaScriptEngine;
        private readonly IOptions<ServerSideRendererOptions> _optionsAccessor;
        private bool _reactScriptRendered;

        public ReactRenderer(
            JavaScriptEngine javaScriptEngine,
            IOptions<ServerSideRendererOptions> optionsAccessor
        )
        {
            _javaScriptEngine = javaScriptEngine;
            _optionsAccessor = optionsAccessor;
        }

        private string GetReactScript(Assembly componentAssembly)
        {
            if (_reactScript is null)
            {
                lock (_reactScriptLock)
                {
                    if (_reactScript is null)
                    {
                        string reactScript;
                        using (Stream reactScriptStream = EmbeddedResourceResolver.ResolveJavaScriptResourceStream(
                            assembly: componentAssembly,
                            resourceName: _optionsAccessor.Value.React.ReactLibraryResourceName
                        ) ?? throw new FileNotFoundException($"React library was not found in the assembly '{componentAssembly.FullName}'.", _optionsAccessor.Value.React.ReactLibraryResourceName))
                        {
                            using StreamReader reactScriptReader = new(reactScriptStream);
                            reactScript = reactScriptReader.ReadToEnd();
                        }

                        string reactDomScript;
                        using (Stream reactDomScriptStream = EmbeddedResourceResolver.ResolveJavaScriptResourceStream(
                            assembly: componentAssembly,
                            resourceName: _optionsAccessor.Value.React.ReactDomLibraryResourceName
                        ) ?? throw new FileNotFoundException($"React DOM library was not found in the assembly '{componentAssembly.FullName}'.", _optionsAccessor.Value.React.ReactDomLibraryResourceName))
                        {
                            using StreamReader reactDomScriptReader = new(reactDomScriptStream);
                            reactDomScript = reactDomScriptReader.ReadToEnd();
                        }

                        _reactScript = $"{reactScript}{reactDomScript}";
                    }
                }
            }

            return _reactScript;
        }

        private static string GetSsrScript()
        {
            if (_reactSsrScript == null)
            {
                lock (_reactSsrScriptLock)
                {
                    if (_reactSsrScript == null)
                    {
                        using Stream stream = Assembly.GetExecutingAssembly().GetManifestResourceStream("RG.SSR.React.Scripts.ReactSSR.js") ?? throw new InvalidProgramException("Could not find the ReactSSR.js script.");
                        using StreamReader reader = new(stream);
                        _reactSsrScript = reader.ReadToEnd();
                    }
                }
            }

            return _reactSsrScript;
        }

        public string Render(Assembly componentAssembly, string componentName, bool isStatic)
        {
            string resourceKey = $"{componentAssembly.FullName}.{componentName}";

            string componentScript = _componentCache.GetOrAdd(
                key: resourceKey,
                valueFactory: key =>
                {
                    using Stream stream = EmbeddedResourceResolver.ResolveJavaScriptResourceStream(
                        assembly: componentAssembly,
                        resourceName: componentName
                    ) ?? throw new FileNotFoundException($"The resource '{componentName}' was not found in the assembly '{componentAssembly.FullName}'.", resourceKey);
                    using StreamReader reader = new(stream);
                    return reader.ReadToEnd();
                });

            string ssrScript = GetSsrScript();

            string renderScript = $"""
                {ssrScript}
                {componentScript}
                const vdom = {componentName}();
                const dom = render(vdom);
                dom;
                """;

            string renderedComponent = _javaScriptEngine.Render(renderScript);

            if (isStatic)
            {
                return renderedComponent;
            }

            string id = "react-" + Guid.NewGuid().ToString()[..8];

            if (_optionsAccessor.Value.React.InlineLibrary && !_reactScriptRendered)
            {
                _reactScriptRendered = true;
                
                return $"""
                    <script defer>{GetReactScript(componentAssembly)}</script>
                    <div id="{id}">{renderedComponent}</div>
                    <script defer>
                    {componentScript}
                    ReactDOM.hydrate(React.createElement({componentName}, null), document.getElementById("{id}"));
                    </script>
                    """;
            }
            else
            {
                return $"""
                    <div id="{id}">{renderedComponent}</div>
                    <script defer>
                    {componentScript}
                    ReactDOM.hydrate(React.createElement({componentName}, null), document.getElementById("{id}"));
                    </script>
                    """;
            }
        }

        public string Render<TProps>(Assembly componentAssembly, string componentName, TProps props, bool isStatic)
        {
            string resourceKey = $"{componentAssembly.FullName}.{componentName}";

            string componentScript = _componentCache.GetOrAdd(
                key: resourceKey,
                valueFactory: key =>
                {
                    using Stream stream = EmbeddedResourceResolver.ResolveJavaScriptResourceStream(
                        assembly: componentAssembly,
                        resourceName: componentName
                    ) ?? throw new FileNotFoundException($"The resource '{componentName}' was not found in the assembly '{componentAssembly.FullName}'.", resourceKey);
                    using StreamReader reader = new(stream);
                    return reader.ReadToEnd();
                });

            string ssrScript = GetSsrScript();

            string propsJson = JsonSerializer.Serialize(props, _propsSerializerOptions);

            string renderScript = $"""
                {ssrScript}
                {componentScript}
                const props = {propsJson};
                const vdom = {componentName}(props);
                const dom = render(vdom);
                dom;
                """;

            string renderedComponent = _javaScriptEngine.Render(renderScript);

            if (isStatic)
            {
                return renderedComponent;
            }

            string id = "react-" + Guid.NewGuid().ToString()[..8];

            if (_optionsAccessor.Value.React.InlineLibrary && !_reactScriptRendered)
            {
                _reactScriptRendered = true;

                return $"""
                    <script defer>{GetReactScript(componentAssembly)}</script>
                    <div id="{id}">{renderedComponent}</div>
                    <script defer>
                    {componentScript}
                    ReactDOM.hydrate(React.createElement({componentName}, {propsJson}), document.getElementById("{id}"));
                    </script>
                    """;
            }
            else
            {
                return $"""
                    <div id="{id}">{renderedComponent}</div>
                    <script defer>
                    {componentScript}
                    ReactDOM.hydrate(React.createElement({componentName}, {propsJson}), document.getElementById("{id}"));
                    </script>
                    """;
            }
        }
    }
}
