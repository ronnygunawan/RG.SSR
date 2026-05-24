using FsCheck;
using FsCheck.Xunit;
using Microsoft.ClearScript;
using Microsoft.ClearScript.JavaScript;
using Microsoft.ClearScript.V8;
using RG.SSR.JavaScript;
using System.Reflection;

namespace RG.SSR.Tests.Properties;

// Feature: es-module-support, Property 1: Module Evaluation Equivalence
/// <summary>
/// For any valid component function and props, rendering the component as a plain script
/// (via engine.Evaluate) and as an ES module (via module evaluation with export default)
/// SHALL produce identical HTML output.
/// </summary>
/// **Validates: Requirements 1.4, 6.2**
public class RenderEquivalenceProperties
{
    private static readonly string _ssrScript;

    static RenderEquivalenceProperties()
    {
        // Load the SSR script from the embedded resource once
        var assembly = typeof(RG.SSR.React.ReactRenderer).Assembly;
        using Stream stream = assembly.GetManifestResourceStream("RG.SSR.React.Scripts.ReactSSR.js")
            ?? throw new InvalidOperationException("Could not find ReactSSR.js embedded resource.");
        using StreamReader reader = new(stream);
        _ssrScript = reader.ReadToEnd();
    }

    /// <summary>
    /// Creates a fresh V8 engine with the SSR module registered for each test iteration.
    /// This avoids global scope pollution between iterations.
    /// </summary>
    private static (V8ScriptEngine engine, ModuleLoader loader) CreateEngine()
    {
        var moduleLoader = new ModuleLoader();
        var engine = new V8ScriptEngine();
        engine.DocumentSettings.Loader = moduleLoader;

        // Register the SSR script as a module so ES module components can import render
        string ssrModuleSource = _ssrScript + "\nexport { render };";
        moduleLoader.RegisterModule("react-ssr", ssrModuleSource);

        return (engine, moduleLoader);
    }

    /// <summary>
    /// Generates random HTML tag names for component output.
    /// </summary>
    private static Arbitrary<string> TagNameArbitrary()
    {
        return Gen.Elements(
            "div", "span", "p", "h1", "h2", "h3",
            "section", "article", "header", "footer", "nav", "main",
            "ul", "li", "a", "button", "table", "tr", "td"
        ).ToArbitrary();
    }

    /// <summary>
    /// Generates random text content for components (safe for use in JS strings).
    /// </summary>
    private static Arbitrary<string> TextContentArbitrary()
    {
        return Gen.Elements(
            "Hello", "World", "Test", "Content", "Rendered",
            "Output", "Sample", "Data", "Welcome", "Title",
            "Greeting", "Message", "Label", "Info", "Status"
        ).ToArbitrary();
    }

    /// <summary>
    /// Generates random component names (valid JavaScript identifiers).
    /// </summary>
    private static Arbitrary<string> ComponentNameArbitrary()
    {
        return Gen.Elements(
            "MyComponent", "TestWidget", "PageHeader",
            "ContentBlock", "InfoPanel", "DataDisplay",
            "SimpleCard", "TextBlock", "StatusBadge", "AlertBox"
        ).ToArbitrary();
    }

    /// <summary>
    /// Property: Rendering a component as a plain script and as an ES module with export default
    /// produces identical HTML output for simple components (single element with text).
    /// </summary>
    [Property(MaxTest = 100)]
    public Property PlainScript_And_EsModule_ProduceIdenticalHtml_SimpleComponent()
    {
        return Prop.ForAll(
            ComponentNameArbitrary(),
            TagNameArbitrary(),
            TextContentArbitrary(),
            (string componentName, string tag, string text) =>
            {
                var (engine, _) = CreateEngine();
                using (engine)
                {
                    // Plain script: define component as a global function, evaluate with SSR script
                    string plainScript = $$"""
                        {{_ssrScript}}
                        function {{componentName}}() {
                            return { tag: '{{tag}}', props: null, children: ['{{text}}'] };
                        }
                        render({{componentName}}());
                        """;
                    string plainResult = engine.Evaluate(plainScript) as string ?? "";

                    // ES module: define component with export default, import render from SSR module
                    string moduleScript = $$"""
                        import { render } from 'react-ssr';
                        export default function {{componentName}}() {
                            return { tag: '{{tag}}', props: null, children: ['{{text}}'] };
                        }
                        const __result = render({{componentName}}());
                        __result;
                        """;
                    string moduleResult = engine.Evaluate(
                        new DocumentInfo { Category = ModuleCategory.Standard },
                        moduleScript
                    ) as string ?? "";

                    return (plainResult == moduleResult && !string.IsNullOrEmpty(plainResult))
                        .Label($"Plain script and ES module should produce identical HTML. " +
                               $"ComponentName='{componentName}', Tag='{tag}', Text='{text}', " +
                               $"PlainResult='{plainResult}', ModuleResult='{moduleResult}'");
                }
            });
    }

    /// <summary>
    /// Generates a combined component scenario with tag, text, and optional props.
    /// </summary>
    private static Arbitrary<(string Tag, string Text, string? Props)> ComponentScenarioArbitrary()
    {
        var tagGen = Gen.Elements("div", "span", "p", "h1", "h2", "h3", "section", "article", "header", "footer", "nav", "main");
        var textGen = Gen.Elements("Hello", "World", "Test", "Content", "Rendered", "Output", "Sample", "Data", "Welcome", "Title");
        var propsGen = Gen.OneOf(
            Gen.Constant<string?>(null),
            Gen.Elements<string?>("{ id: 'test' }", "{ title: 'hello' }", "{ name: 'world' }", "{ value: 'data' }", "{ label: 'item' }")
        );
        var gen = tagGen.SelectMany(tag => textGen.SelectMany(text => propsGen.Select(props => (tag, text, props))));
        return gen.ToArbitrary();
    }

    /// <summary>
    /// Property: Rendering a component with props as a plain script and as an ES module
    /// produces identical HTML output.
    /// </summary>
    [Property(MaxTest = 100)]
    public Property PlainScript_And_EsModule_ProduceIdenticalHtml_WithProps()
    {
        return Prop.ForAll(
            ComponentNameArbitrary(),
            ComponentScenarioArbitrary(),
            (string componentName, (string Tag, string Text, string? Props) scenario) =>
            {
                var (tag, text, props) = scenario;
                var (engine, _) = CreateEngine();
                using (engine)
                {
                    // Build the component body: if props are provided, use props.title (or similar),
                    // otherwise just use the text directly
                    string componentBody;
                    string invocation;

                    if (props != null)
                    {
                        // Component uses props - render the text from props or fallback
                        componentBody = $"return {{ tag: '{tag}', props: null, children: [p && p.title ? p.title : '{text}'] }};";
                        invocation = $"{componentName}({props})";
                    }
                    else
                    {
                        componentBody = $"return {{ tag: '{tag}', props: null, children: ['{text}'] }};";
                        invocation = $"{componentName}()";
                    }

                    // Plain script evaluation
                    string plainScript = $$"""
                        {{_ssrScript}}
                        function {{componentName}}(p) {
                            {{componentBody}}
                        }
                        render({{invocation}});
                        """;
                    string plainResult = engine.Evaluate(plainScript) as string ?? "";

                    // ES module evaluation
                    string moduleScript = $$"""
                        import { render } from 'react-ssr';
                        export default function {{componentName}}(p) {
                            {{componentBody}}
                        }
                        const __result = render({{invocation}});
                        __result;
                        """;
                    string moduleResult = engine.Evaluate(
                        new DocumentInfo { Category = ModuleCategory.Standard },
                        moduleScript
                    ) as string ?? "";

                    return (plainResult == moduleResult && !string.IsNullOrEmpty(plainResult))
                        .Label($"Plain script and ES module should produce identical HTML with props. " +
                               $"ComponentName='{componentName}', Tag='{tag}', Text='{text}', Props='{props ?? "null"}', " +
                               $"PlainResult='{plainResult}', ModuleResult='{moduleResult}'");
                }
            });
    }

    /// <summary>
    /// Generates a pair of tag names for nested component tests.
    /// </summary>
    private static Arbitrary<(string Outer, string Inner)> NestedTagsArbitrary()
    {
        var outerGen = Gen.Elements("div", "span", "p", "h1", "section", "article", "header", "footer", "nav", "main");
        var innerGen = Gen.Elements("span", "p", "em", "strong", "a", "li", "td", "small", "code", "label");
        var gen = outerGen.SelectMany(outer => innerGen.Select(inner => (outer, inner)));
        return gen.ToArbitrary();
    }

    /// <summary>
    /// Property: Rendering a component with nested children as a plain script and as an ES module
    /// produces identical HTML output.
    /// </summary>
    [Property(MaxTest = 100)]
    public Property PlainScript_And_EsModule_ProduceIdenticalHtml_NestedChildren()
    {
        return Prop.ForAll(
            ComponentNameArbitrary(),
            NestedTagsArbitrary(),
            TextContentArbitrary(),
            (string componentName, (string Outer, string Inner) tags, string text) =>
            {
                var (outerTag, innerTag) = tags;
                var (engine, _) = CreateEngine();
                using (engine)
                {
                    // Component returns nested vdom: outer element containing an inner element with text
                    string componentBody = $$"""return { tag: '{{outerTag}}', props: null, children: [{ tag: '{{innerTag}}', props: null, children: ['{{text}}'] }] };""";

                    // Plain script evaluation
                    string plainScript = $$"""
                        {{_ssrScript}}
                        function {{componentName}}() {
                            {{componentBody}}
                        }
                        render({{componentName}}());
                        """;
                    string plainResult = engine.Evaluate(plainScript) as string ?? "";

                    // ES module evaluation
                    string moduleScript = $$"""
                        import { render } from 'react-ssr';
                        export default function {{componentName}}() {
                            {{componentBody}}
                        }
                        const __result = render({{componentName}}());
                        __result;
                        """;
                    string moduleResult = engine.Evaluate(
                        new DocumentInfo { Category = ModuleCategory.Standard },
                        moduleScript
                    ) as string ?? "";

                    return (plainResult == moduleResult && !string.IsNullOrEmpty(plainResult))
                        .Label($"Plain script and ES module should produce identical HTML with nested children. " +
                               $"ComponentName='{componentName}', OuterTag='{outerTag}', InnerTag='{innerTag}', Text='{text}', " +
                               $"PlainResult='{plainResult}', ModuleResult='{moduleResult}'");
                }
            });
    }
}
