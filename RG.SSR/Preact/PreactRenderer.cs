using Microsoft.ClearScript.JavaScript;
using Microsoft.Extensions.Options;
using RG.SSR.EmbeddedResources;
using RG.SSR.JavaScript;
using RG.SSR.Options;
using System.Collections.Concurrent;
using System.Net.NetworkInformation;
using System.Reflection;
using System.Text.Json;

namespace RG.SSR.Preact
{
    internal sealed class PreactRenderer : IPreactRenderer
    {
        private static string? _preactUmdScript;
        private static readonly object _preactUmdScriptLock = new();
        private static string? _preactSsrScript;
        private static readonly object _preactSsrScriptLock = new();
        private static readonly ConcurrentDictionary<string, string> _componentCache = new();

        private static readonly JsonSerializerOptions _propsSerializerOptions = new()
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase
        };

        private readonly JavaScriptEngine _javaScriptEngine;
        private readonly IOptions<ServerSideRendererOptions> _optionsAccessor;
        private bool _preactScriptRendered;

        public PreactRenderer(
            JavaScriptEngine javaScriptEngine,
            IOptions<ServerSideRendererOptions> optionsAccessor
        )
        {
            _javaScriptEngine = javaScriptEngine;
            _optionsAccessor = optionsAccessor;
        }

        private string GetPreactScript(Assembly componentAssembly)
        {
            if (_preactUmdScript is null)
            {
                lock (_preactUmdScriptLock)
                {
                    if (_preactUmdScript is null)
                    {
                        string preactUmdScript;
                        using (Stream preactScriptStream = EmbeddedResourceResolver.ResolveJavaScriptResourceStream(
                            assembly: componentAssembly,
                            resourceName: _optionsAccessor.Value.Preact.PreactUmdLibraryResourceName
                        ) ?? throw new FileNotFoundException($"Preact library was not found in the assembly '{componentAssembly.FullName}'.", _optionsAccessor.Value.Preact.PreactUmdLibraryResourceName))
                        {
                            using StreamReader preactScriptReader = new(preactScriptStream);
                            preactUmdScript = preactScriptReader.ReadToEnd();
                        }

                        string preactHooksUmdScript;
                        using (Stream preactHooksScriptStream = EmbeddedResourceResolver.ResolveJavaScriptResourceStream(
                            assembly: componentAssembly,
                            resourceName: _optionsAccessor.Value.Preact.PreactHooksUmdLibraryResourceName
                        ) ?? throw new FileNotFoundException($"Preact hooks library was not found in the assembly '{componentAssembly.FullName}'.", _optionsAccessor.Value.Preact.PreactHooksUmdLibraryResourceName))
                        {
                            using StreamReader preactHooksScriptReader = new(preactHooksScriptStream);
                            preactHooksUmdScript = preactHooksScriptReader.ReadToEnd();
                        }

                        if (_optionsAccessor.Value.Preact.ReactCompat)
                        {
                            using Stream preactCompatScriptStream = EmbeddedResourceResolver.ResolveJavaScriptResourceStream(
                                assembly: componentAssembly,
                                resourceName: _optionsAccessor.Value.Preact.PreactCompatUmdLibraryResourceName
                            ) ?? throw new FileNotFoundException($"Preact compat library was not found in the assembly '{componentAssembly.FullName}'.", _optionsAccessor.Value.Preact.PreactCompatUmdLibraryResourceName);
                            using StreamReader preactCompatScriptReader = new(preactCompatScriptStream);
                            string preactCompatUmdScript = preactCompatScriptReader.ReadToEnd();

                            _preactUmdScript = $"{preactUmdScript}{preactHooksUmdScript}{preactCompatUmdScript}";
                        }
                        else
                        {
                            _preactUmdScript = $"{preactUmdScript}{preactHooksUmdScript}";
                        }
                    }
                }
            }

            return _preactUmdScript;
        }

        private static string GetSsrScript()
        {
            if (_preactSsrScript is null)
            {
                lock (_preactSsrScriptLock)
                {
                    if (_preactSsrScript is null)
                    {
                        using Stream stream = Assembly.GetExecutingAssembly().GetManifestResourceStream("RG.SSR.Preact.Scripts.PreactSSR.js") ?? throw new InvalidProgramException("Could not find the PreactSSR.js script.");
                        using StreamReader reader = new(stream);
                        _preactSsrScript = reader.ReadToEnd();
                    }
                }
            }

            return _preactSsrScript;
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

            string id = "preact-" + Guid.NewGuid().ToString()[..8];

            if (_optionsAccessor.Value.React.InlineLibrary && !_preactScriptRendered)
            {
                _preactScriptRendered = true;

                return $"""
                    <script defer>{GetPreactScript(componentAssembly)}const React=preact;</script>
                    <div id="{id}">{renderedComponent}</div>
                    <script defer>
                    {componentScript}
                    preact.hydrate(preact.createElement({componentName}, null), document.getElementById("{id}"));
                    </script>
                    """;
            }
            else
            {
                return $"""
                    <div id="{id}">{renderedComponent}</div>
                    <script defer>
                    {componentScript}
                    preact.hydrate(preact.createElement({componentName}, null), document.getElementById("{id}"));
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

            string id = "preact-" + Guid.NewGuid().ToString()[..8];

            if (_optionsAccessor.Value.React.InlineLibrary && !_preactScriptRendered)
            {
                _preactScriptRendered = true;

                return $"""
                    <script defer>{GetPreactScript(componentAssembly)}const React=preact;</script>
                    <div id="{id}">{renderedComponent}</div>
                    <script defer>
                    {componentScript}
                    preact.hydrate(preact.createElement({componentName}, {propsJson}), document.getElementById("{id}"));
                    </script>
                    """;
            }
            else
            {
                return $"""
                    <div id="{id}">{renderedComponent}</div>
                    <script defer>
                    {componentScript}
                    preact.hydrate(preact.createElement({componentName}, {propsJson}), document.getElementById("{id}"));
                    </script>
                    """;
            }
        }
    }
}
