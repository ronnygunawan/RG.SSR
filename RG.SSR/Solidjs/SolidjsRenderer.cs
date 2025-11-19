using Microsoft.Extensions.Options;
using RG.SSR.EmbeddedResources;
using RG.SSR.JavaScript;
using RG.SSR.Options;
using System.Collections.Concurrent;
using System.Reflection;
using System.Text.Json;

namespace RG.SSR.Solidjs
{
    internal sealed class SolidjsRenderer : ISolidjsRenderer
    {
        private static string? _solidjsScript;
        private static readonly object _solidjsScriptLock = new();
        private static string? _solidjsSsrScript;
        private static readonly object _solidjsSsrScriptLock = new();
        private static readonly ConcurrentDictionary<string, string> _componentCache = new();

        private static readonly JsonSerializerOptions _propsSerializerOptions = new()
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase
        };

        private readonly JavaScriptEngine _javaScriptEngine;
        private readonly IOptions<ServerSideRendererOptions> _optionsAccessor;
        private bool _solidjsScriptRendered;

        public SolidjsRenderer(
            JavaScriptEngine javaScriptEngine,
            IOptions<ServerSideRendererOptions> optionsAccessor
        )
        {
            _javaScriptEngine = javaScriptEngine;
            _optionsAccessor = optionsAccessor;
        }

        private string GetSolidjsScript(Assembly componentAssembly)
        {
            if (_solidjsScript is null)
            {
                lock (_solidjsScriptLock)
                {
                    if (_solidjsScript is null)
                    {
                        string solidjsLibraryScript;
                        using (Stream solidjsScriptStream = EmbeddedResourceResolver.ResolveJavaScriptResourceStream(
                            assembly: componentAssembly,
                            resourceName: _optionsAccessor.Value.Solidjs.SolidjsLibraryResourceName
                        ) ?? throw new FileNotFoundException($"SolidJS library was not found in the assembly '{componentAssembly.FullName}'.", _optionsAccessor.Value.Solidjs.SolidjsLibraryResourceName))
                        {
                            using StreamReader solidjsScriptReader = new(solidjsScriptStream);
                            solidjsLibraryScript = solidjsScriptReader.ReadToEnd();
                        }

                        string solidjsWebScript;
                        using (Stream solidjsWebScriptStream = EmbeddedResourceResolver.ResolveJavaScriptResourceStream(
                            assembly: componentAssembly,
                            resourceName: _optionsAccessor.Value.Solidjs.SolidjsWebLibraryResourceName
                        ) ?? throw new FileNotFoundException($"SolidJS Web library was not found in the assembly '{componentAssembly.FullName}'.", _optionsAccessor.Value.Solidjs.SolidjsWebLibraryResourceName))
                        {
                            using StreamReader solidjsWebScriptReader = new(solidjsWebScriptStream);
                            solidjsWebScript = solidjsWebScriptReader.ReadToEnd();
                        }

                        _solidjsScript = $"{solidjsLibraryScript}{solidjsWebScript}";
                    }
                }
            }

            return _solidjsScript;
        }

        private static string GetSsrScript()
        {
            if (_solidjsSsrScript is null)
            {
                lock (_solidjsSsrScriptLock)
                {
                    if (_solidjsSsrScript is null)
                    {
                        using Stream stream = Assembly.GetExecutingAssembly().GetManifestResourceStream("RG.SSR.Solidjs.Scripts.SolidjsSSR.js") ?? throw new InvalidProgramException("Could not find the SolidjsSSR.js script.");
                        using StreamReader reader = new(stream);
                        _solidjsSsrScript = reader.ReadToEnd();
                    }
                }
            }

            return _solidjsSsrScript;
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

            string id = "solidjs-" + Guid.NewGuid().ToString()[..8];

            if (_optionsAccessor.Value.Solidjs.InlineLibrary && !_solidjsScriptRendered)
            {
                _solidjsScriptRendered = true;
                
                return $$"""
                    <script defer>{{GetSolidjsScript(componentAssembly)}}</script>
                    <div id="{{id}}">{{renderedComponent}}</div>
                    <script type="module">
                    import { render } from 'solid-js/web';
                    {{componentScript}}
                    render(() => {{componentName}}(), document.getElementById("{{id}}"));
                    </script>
                    """;
            }
            else
            {
                return $$"""
                    <div id="{{id}}">{{renderedComponent}}</div>
                    <script type="module">
                    import { render } from 'solid-js/web';
                    {{componentScript}}
                    render(() => {{componentName}}(), document.getElementById("{{id}}"));
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

            string id = "solidjs-" + Guid.NewGuid().ToString()[..8];

            if (_optionsAccessor.Value.Solidjs.InlineLibrary && !_solidjsScriptRendered)
            {
                _solidjsScriptRendered = true;

                return $$"""
                    <script defer>{{GetSolidjsScript(componentAssembly)}}</script>
                    <div id="{{id}}">{{renderedComponent}}</div>
                    <script type="module">
                    import { render } from 'solid-js/web';
                    {{componentScript}}
                    const props = {{propsJson}};
                    render(() => {{componentName}}(props), document.getElementById("{{id}}"));
                    </script>
                    """;
            }
            else
            {
                return $$"""
                    <div id="{{id}}">{{renderedComponent}}</div>
                    <script type="module">
                    import { render } from 'solid-js/web';
                    {{componentScript}}
                    const props = {{propsJson}};
                    render(() => {{componentName}}(props), document.getElementById("{{id}}"));
                    </script>
                    """;
            }
        }
    }
}
