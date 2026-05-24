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

        private const string SsrModuleSpecifier = "react-ssr";

        private static readonly JsonSerializerOptions _propsSerializerOptions = new()
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase
        };

        private readonly JavaScriptEngine _javaScriptEngine;
        private readonly ModuleLoader _moduleLoader;
        private readonly IOptions<ServerSideRendererOptions> _optionsAccessor;
        private bool _reactScriptRendered;

        public ReactRenderer(
            JavaScriptEngine javaScriptEngine,
            ModuleLoader moduleLoader,
            IOptions<ServerSideRendererOptions> optionsAccessor
        )
        {
            _javaScriptEngine = javaScriptEngine;
            _moduleLoader = moduleLoader;
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
                        var assembly = Assembly.GetExecutingAssembly();
                        using Stream stream = assembly.GetManifestResourceStream("RG.SSR.React.Scripts.ReactSSR.js")
                            ?? throw new InvalidOperationException($"Could not find embedded resource 'RG.SSR.React.Scripts.ReactSSR.js' in assembly '{assembly.FullName}'.");
                        using StreamReader reader = new(stream);
                        _reactSsrScript = reader.ReadToEnd();
                    }
                }
            }

            return _reactSsrScript;
        }

        private void EnsureSsrModuleRegistered()
        {
            string ssrScript = GetSsrScript();
            // Convert the SSR script to an ES module that exports the render function
            string ssrModuleSource = ssrScript + "\nexport { render };";
            _moduleLoader.RegisterModule(SsrModuleSpecifier, ssrModuleSource);
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

            bool isModule = ModuleSyntaxDetector.ContainsModuleSyntax(componentScript);

            if (isModule)
            {
                return RenderModule(componentAssembly, componentName, componentScript, propsJson: null, isStatic);
            }

            // Plain script evaluation (unchanged behavior)
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

            string propsJson = JsonSerializer.Serialize(props, _propsSerializerOptions);

            bool isModule = ModuleSyntaxDetector.ContainsModuleSyntax(componentScript);

            if (isModule)
            {
                return RenderModule(componentAssembly, componentName, componentScript, propsJson, isStatic);
            }

            // Plain script evaluation (unchanged behavior)
            string ssrScript = GetSsrScript();

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

        private string RenderModule(Assembly componentAssembly, string componentName, string componentScript, string? propsJson, bool isStatic)
        {
            EnsureSsrModuleRegistered();

            // Use the component module specifier for wrapper imports and hydration output
            string componentModuleSpecifier = $"./{componentName}.js";

            // Construct a wrapper module that imports the SSR render function and the component,
            // then invokes the component and renders it.
            // Try default export first, fall back to named export matching componentName.
            string propsArg = propsJson ?? "undefined";
            string wrapperModule = $$"""
                import { render } from '{{SsrModuleSpecifier}}';
                import * as ComponentModule from '{{componentModuleSpecifier}}';
                const Component = ComponentModule.default || ComponentModule['{{componentName}}'];
                if (!Component) {
                    throw new Error('No valid component export was found for "{{componentName}}". The module must have a default export or a named export matching "{{componentName}}".');
                }
                const props = {{propsArg}};
                const vdom = props !== undefined ? Component(props) : Component();
                const result = render(vdom);
                result;
                """;

            string renderedComponent = _javaScriptEngine.RenderModule(wrapperModule, componentAssembly);

            if (isStatic)
            {
                return renderedComponent;
            }

            string id = "react-" + Guid.NewGuid().ToString()[..8];

            if (_optionsAccessor.Value.React.InlineLibrary && !_reactScriptRendered)
            {
                _reactScriptRendered = true;

                string hydrationProps = propsJson ?? "null";
                return $"""
                    <script defer>{GetReactScript(componentAssembly)}</script>
                    <div id="{id}">{renderedComponent}</div>
                    <script type="module">
                    import * as ComponentModule from '{componentModuleSpecifier}';
                    const Component = ComponentModule.default || ComponentModule['{componentName}'];
                    ReactDOM.hydrate(React.createElement(Component, {hydrationProps}), document.getElementById("{id}"));
                    </script>
                    """;
            }
            else
            {
                string hydrationProps = propsJson ?? "null";
                return $"""
                    <div id="{id}">{renderedComponent}</div>
                    <script type="module">
                    import * as ComponentModule from '{componentModuleSpecifier}';
                    const Component = ComponentModule.default || ComponentModule['{componentName}'];
                    ReactDOM.hydrate(React.createElement(Component, {hydrationProps}), document.getElementById("{id}"));
                    </script>
                    """;
            }
        }
    }
}
