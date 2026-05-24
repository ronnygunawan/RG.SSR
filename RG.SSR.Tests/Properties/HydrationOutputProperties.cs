using FsCheck;
using FsCheck.Xunit;
using Microsoft.Extensions.Options;
using RG.SSR.JavaScript;
using RG.SSR.Options;
using RG.SSR.React;
using RG.SSR.Preact;
using System.Reflection;
using System.Text;

namespace RG.SSR.Tests.Properties;

// Feature: es-module-support, Property 7: Hydration Script Tag Type
/// <summary>
/// For any component rendered with isStatic=false, if the component source contains ES module syntax
/// then the hydration script SHALL be emitted within a &lt;script type="module"&gt; tag, and if the
/// component source does not contain ES module syntax then the hydration script SHALL be emitted
/// within a &lt;script defer&gt; tag.
/// </summary>
/// **Validates: Requirements 7.2, 7.3**
public class HydrationOutputProperties
{
    /// <summary>
    /// A mock Assembly subclass that provides controlled manifest resource names and streams.
    /// Each instance has a unique FullName to avoid static cache collisions across test iterations.
    /// </summary>
    private class MockAssembly : Assembly
    {
        private readonly Dictionary<string, byte[]> _resources;
        private readonly string _fullName;

        public MockAssembly(Dictionary<string, byte[]> resources, string uniqueId)
        {
            _resources = resources;
            _fullName = $"MockAssembly_{uniqueId}, Version=1.0.0.0";
        }

        public override string[] GetManifestResourceNames()
        {
            return _resources.Keys.ToArray();
        }

        public override Stream? GetManifestResourceStream(string name)
        {
            if (_resources.TryGetValue(name, out var data))
            {
                return new MemoryStream(data);
            }
            return null;
        }

        public override string FullName => _fullName;
    }

    private static string NextUniqueId() => Guid.NewGuid().ToString("N");

    /// <summary>
    /// Generates random component names (valid JavaScript identifiers).
    /// </summary>
    private static Arbitrary<string> ComponentNameArbitrary()
    {
        return Gen.Elements(
            "MyComponent", "TestWidget", "PageHeader",
            "ContentBlock", "InfoPanel", "DataDisplay", "SimpleCard",
            "TextBlock", "StatusBadge", "AlertBox"
        ).ToArbitrary();
    }

    /// <summary>
    /// Generates random HTML tag names for component output.
    /// </summary>
    private static Arbitrary<string> TagNameArbitrary()
    {
        return Gen.Elements(
            "div", "span", "p", "h1", "h2", "h3",
            "section", "article", "header", "footer", "nav", "main"
        ).ToArbitrary();
    }

    /// <summary>
    /// Generates random text content for components.
    /// </summary>
    private static Arbitrary<string> TextContentArbitrary()
    {
        return Gen.Elements(
            "Hello", "World", "Test", "Content",
            "Rendered", "Output", "Sample", "Data",
            "Welcome", "Title"
        ).ToArbitrary();
    }

    /// <summary>
    /// Creates a MockAssembly with a plain script component (no import/export).
    /// </summary>
    private static MockAssembly CreatePlainScriptAssembly(string componentName, string tag, string text)
    {
        string uniqueId = NextUniqueId();
        string script = $"function {componentName}() {{ return {{ tag: '{tag}', props: null, children: ['{text}'] }}; }}";
        var resources = new Dictionary<string, byte[]>
        {
            [$"MockAssembly_{uniqueId}.{componentName}.js"] = Encoding.UTF8.GetBytes(script)
        };
        return new MockAssembly(resources, uniqueId);
    }

    /// <summary>
    /// Creates a MockAssembly with an ES module component for the React renderer.
    /// Uses import/export syntax to trigger module detection.
    /// </summary>
    private static MockAssembly CreateReactEsModuleAssembly(string componentName, string tag, string text)
    {
        string uniqueId = NextUniqueId();
        string script = $"import {{ createElement }} from 'react';\nexport default function {componentName}() {{ return {{ tag: '{tag}', props: null, children: ['{text}'] }}; }}";
        var resources = new Dictionary<string, byte[]>
        {
            [$"MockAssembly_{uniqueId}.{componentName}.js"] = Encoding.UTF8.GetBytes(script)
        };
        return new MockAssembly(resources, uniqueId);
    }

    /// <summary>
    /// Creates a MockAssembly with an ES module component for the Preact renderer.
    /// Uses export syntax to trigger module detection. The Preact renderer inlines the
    /// component code into its wrapper module which already imports from 'preact',
    /// so we avoid duplicate import declarations.
    /// </summary>
    private static MockAssembly CreatePreactEsModuleAssembly(string componentName, string tag, string text)
    {
        string uniqueId = NextUniqueId();
        string script = $"import {{ createElement }} from 'preact';\nexport function {componentName}() {{ return createElement('{tag}', null, '{text}'); }}";
        var resources = new Dictionary<string, byte[]>
        {
            [$"MockAssembly_{uniqueId}.{componentName}.js"] = Encoding.UTF8.GetBytes(script)
        };
        return new MockAssembly(resources, uniqueId);
    }

    /// <summary>
    /// Creates a fresh ReactRenderer with its own engine and module loader.
    /// InlineLibrary is disabled to avoid needing React UMD in mock assemblies.
    /// </summary>
    private static (ReactRenderer renderer, JavaScriptEngine engine) CreateReactRenderer()
    {
        var moduleLoader = new ModuleLoader();
        var engine = new JavaScriptEngine(moduleLoader);
        var ssrOptions = new ServerSideRendererOptions();
        ssrOptions.React.InlineLibrary = false;
        var options = Microsoft.Extensions.Options.Options.Create(ssrOptions);
        var renderer = new ReactRenderer(engine, moduleLoader, options);
        return (renderer, engine);
    }

    /// <summary>
    /// Creates a fresh PreactRenderer with its own engine and module loader.
    /// InlineLibrary is disabled to avoid needing Preact UMD in mock assemblies.
    /// </summary>
    private static (PreactRenderer renderer, JavaScriptEngine engine) CreatePreactRenderer()
    {
        var moduleLoader = new ModuleLoader();
        var engine = new JavaScriptEngine(moduleLoader);
        var ssrOptions = new ServerSideRendererOptions();
        ssrOptions.React.InlineLibrary = false;
        ssrOptions.Preact.InlineLibrary = false;
        var options = Microsoft.Extensions.Options.Options.Create(ssrOptions);
        var renderer = new PreactRenderer(engine, options);
        return (renderer, engine);
    }

    // ===== Property 7: Hydration Script Tag Type =====

    /// <summary>
    /// Property 7a: For React components with module syntax rendered with isStatic=false,
    /// the output contains &lt;script type="module"&gt; and does NOT contain &lt;script defer&gt;.
    /// </summary>
    [Property(MaxTest = 100)]
    public Property React_ModuleComponent_NonStatic_EmitsScriptTypeModule()
    {
        return Prop.ForAll(
            ComponentNameArbitrary(),
            TagNameArbitrary(),
            TextContentArbitrary(),
            (string componentName, string tag, string text) =>
            {
                var assembly = CreateReactEsModuleAssembly(componentName, tag, text);
                var (renderer, engine) = CreateReactRenderer();
                using (engine)
                {
                    string output = renderer.Render(assembly, componentName, isStatic: false);

                    var containsModuleScript = output.Contains("<script type=\"module\">");
                    var doesNotContainDeferScript = !output.Contains("<script defer>");

                    return (containsModuleScript && doesNotContainDeferScript)
                        .Label($"Module component with isStatic=false should emit <script type=\"module\">. " +
                               $"ComponentName='{componentName}', " +
                               $"ContainsModuleScript={containsModuleScript}, " +
                               $"DoesNotContainDeferScript={doesNotContainDeferScript}, " +
                               $"Output='{output[..Math.Min(output.Length, 300)]}'");
                }
            });
    }

    /// <summary>
    /// Property 7b: For React components without module syntax rendered with isStatic=false,
    /// the output contains &lt;script defer&gt; and does NOT contain &lt;script type="module"&gt;.
    /// </summary>
    [Property(MaxTest = 100)]
    public Property React_PlainScriptComponent_NonStatic_EmitsScriptDefer()
    {
        return Prop.ForAll(
            ComponentNameArbitrary(),
            TagNameArbitrary(),
            TextContentArbitrary(),
            (string componentName, string tag, string text) =>
            {
                var assembly = CreatePlainScriptAssembly(componentName, tag, text);
                var (renderer, engine) = CreateReactRenderer();
                using (engine)
                {
                    string output = renderer.Render(assembly, componentName, isStatic: false);

                    var containsDeferScript = output.Contains("<script defer>");
                    var doesNotContainModuleScript = !output.Contains("<script type=\"module\">");

                    return (containsDeferScript && doesNotContainModuleScript)
                        .Label($"Plain script component with isStatic=false should emit <script defer>. " +
                               $"ComponentName='{componentName}', " +
                               $"ContainsDeferScript={containsDeferScript}, " +
                               $"DoesNotContainModuleScript={doesNotContainModuleScript}, " +
                               $"Output='{output[..Math.Min(output.Length, 300)]}'");
                }
            });
    }

    /// <summary>
    /// Property 7c: For Preact components with module syntax rendered with isStatic=false,
    /// the output contains &lt;script type="module"&gt; and does NOT contain &lt;script defer&gt;.
    /// </summary>
    [Property(MaxTest = 100)]
    public Property Preact_ModuleComponent_NonStatic_EmitsScriptTypeModule()
    {
        return Prop.ForAll(
            ComponentNameArbitrary(),
            TagNameArbitrary(),
            TextContentArbitrary(),
            (string componentName, string tag, string text) =>
            {
                var assembly = CreatePreactEsModuleAssembly(componentName, tag, text);
                var (renderer, engine) = CreatePreactRenderer();
                using (engine)
                {
                    string output = renderer.Render(assembly, componentName, isStatic: false);

                    var containsModuleScript = output.Contains("<script type=\"module\">");
                    var doesNotContainDeferScript = !output.Contains("<script defer>");

                    return (containsModuleScript && doesNotContainDeferScript)
                        .Label($"Preact module component with isStatic=false should emit <script type=\"module\">. " +
                               $"ComponentName='{componentName}', " +
                               $"ContainsModuleScript={containsModuleScript}, " +
                               $"DoesNotContainDeferScript={doesNotContainDeferScript}, " +
                               $"Output='{output[..Math.Min(output.Length, 300)]}'");
                }
            });
    }

    /// <summary>
    /// Property 7d: For Preact components without module syntax rendered with isStatic=false,
    /// the output contains &lt;script defer&gt; and does NOT contain &lt;script type="module"&gt;.
    /// </summary>
    [Property(MaxTest = 100)]
    public Property Preact_PlainScriptComponent_NonStatic_EmitsScriptDefer()
    {
        return Prop.ForAll(
            ComponentNameArbitrary(),
            TagNameArbitrary(),
            TextContentArbitrary(),
            (string componentName, string tag, string text) =>
            {
                var assembly = CreatePlainScriptAssembly(componentName, tag, text);
                var (renderer, engine) = CreatePreactRenderer();
                using (engine)
                {
                    string output = renderer.Render(assembly, componentName, isStatic: false);

                    var containsDeferScript = output.Contains("<script defer>");
                    var doesNotContainModuleScript = !output.Contains("<script type=\"module\">");

                    return (containsDeferScript && doesNotContainModuleScript)
                        .Label($"Preact plain script component with isStatic=false should emit <script defer>. " +
                               $"ComponentName='{componentName}', " +
                               $"ContainsDeferScript={containsDeferScript}, " +
                               $"DoesNotContainModuleScript={doesNotContainModuleScript}, " +
                               $"Output='{output[..Math.Min(output.Length, 300)]}'");
                }
            });
    }

    // ===== Property 8: Static Rendering Produces No Scripts =====
    // Feature: es-module-support, Property 8: Static Rendering Produces No Scripts
    /// <summary>
    /// For any component (whether ES module or plain script) rendered with isStatic=true,
    /// the output SHALL contain no &lt;script tags and no container &lt;div&gt; wrapper —
    /// only the server-rendered HTML content.
    /// </summary>
    /// **Validates: Requirements 7.4**

    /// <summary>
    /// Property 8a: For React ES module components rendered with isStatic=true,
    /// the output contains no &lt;script tags and no container div wrapper.
    /// </summary>
    [Property(MaxTest = 100)]
    public Property React_ModuleComponent_Static_ProducesNoScripts()
    {
        return Prop.ForAll(
            ComponentNameArbitrary(),
            TagNameArbitrary(),
            TextContentArbitrary(),
            (string componentName, string tag, string text) =>
            {
                var assembly = CreateReactEsModuleAssembly(componentName, tag, text);
                var (renderer, engine) = CreateReactRenderer();
                using (engine)
                {
                    string output = renderer.Render(assembly, componentName, isStatic: true);

                    var noScriptTag = !output.Contains("<script", StringComparison.OrdinalIgnoreCase);
                    var noContainerDiv = !output.Contains("<div id=\"react-");

                    return (noScriptTag && noContainerDiv)
                        .Label($"React module component with isStatic=true should produce no scripts and no container div. " +
                               $"ComponentName='{componentName}', " +
                               $"NoScriptTag={noScriptTag}, " +
                               $"NoContainerDiv={noContainerDiv}, " +
                               $"Output='{output[..Math.Min(output.Length, 300)]}'");
                }
            });
    }

    /// <summary>
    /// Property 8b: For React plain script components rendered with isStatic=true,
    /// the output contains no &lt;script tags and no container div wrapper.
    /// </summary>
    [Property(MaxTest = 100)]
    public Property React_PlainScriptComponent_Static_ProducesNoScripts()
    {
        return Prop.ForAll(
            ComponentNameArbitrary(),
            TagNameArbitrary(),
            TextContentArbitrary(),
            (string componentName, string tag, string text) =>
            {
                var assembly = CreatePlainScriptAssembly(componentName, tag, text);
                var (renderer, engine) = CreateReactRenderer();
                using (engine)
                {
                    string output = renderer.Render(assembly, componentName, isStatic: true);

                    var noScriptTag = !output.Contains("<script", StringComparison.OrdinalIgnoreCase);
                    var noContainerDiv = !output.Contains("<div id=\"react-");

                    return (noScriptTag && noContainerDiv)
                        .Label($"React plain script component with isStatic=true should produce no scripts and no container div. " +
                               $"ComponentName='{componentName}', " +
                               $"NoScriptTag={noScriptTag}, " +
                               $"NoContainerDiv={noContainerDiv}, " +
                               $"Output='{output[..Math.Min(output.Length, 300)]}'");
                }
            });
    }

    /// <summary>
    /// Property 8c: For Preact ES module components rendered with isStatic=true,
    /// the output contains no &lt;script tags and no container div wrapper.
    /// </summary>
    [Property(MaxTest = 100)]
    public Property Preact_ModuleComponent_Static_ProducesNoScripts()
    {
        return Prop.ForAll(
            ComponentNameArbitrary(),
            TagNameArbitrary(),
            TextContentArbitrary(),
            (string componentName, string tag, string text) =>
            {
                var assembly = CreatePreactEsModuleAssembly(componentName, tag, text);
                var (renderer, engine) = CreatePreactRenderer();
                using (engine)
                {
                    string output = renderer.Render(assembly, componentName, isStatic: true);

                    var noScriptTag = !output.Contains("<script", StringComparison.OrdinalIgnoreCase);
                    var noContainerDiv = !output.Contains("<div id=\"preact-");

                    return (noScriptTag && noContainerDiv)
                        .Label($"Preact module component with isStatic=true should produce no scripts and no container div. " +
                               $"ComponentName='{componentName}', " +
                               $"NoScriptTag={noScriptTag}, " +
                               $"NoContainerDiv={noContainerDiv}, " +
                               $"Output='{output[..Math.Min(output.Length, 300)]}'");
                }
            });
    }

    /// <summary>
    /// Property 8d: For Preact plain script components rendered with isStatic=true,
    /// the output contains no &lt;script tags and no container div wrapper.
    /// </summary>
    [Property(MaxTest = 100)]
    public Property Preact_PlainScriptComponent_Static_ProducesNoScripts()
    {
        return Prop.ForAll(
            ComponentNameArbitrary(),
            TagNameArbitrary(),
            TextContentArbitrary(),
            (string componentName, string tag, string text) =>
            {
                var assembly = CreatePlainScriptAssembly(componentName, tag, text);
                var (renderer, engine) = CreatePreactRenderer();
                using (engine)
                {
                    string output = renderer.Render(assembly, componentName, isStatic: true);

                    var noScriptTag = !output.Contains("<script", StringComparison.OrdinalIgnoreCase);
                    var noContainerDiv = !output.Contains("<div id=\"preact-");

                    return (noScriptTag && noContainerDiv)
                        .Label($"Preact plain script component with isStatic=true should produce no scripts and no container div. " +
                               $"ComponentName='{componentName}', " +
                               $"NoScriptTag={noScriptTag}, " +
                               $"NoContainerDiv={noContainerDiv}, " +
                               $"Output='{output[..Math.Min(output.Length, 300)]}'");
                }
            });
    }
}
