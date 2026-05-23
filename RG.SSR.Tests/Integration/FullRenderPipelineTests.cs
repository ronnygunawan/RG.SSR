using Microsoft.Extensions.Options;
using RG.SSR.JavaScript;
using RG.SSR.Options;
using RG.SSR.React;
using RG.SSR.Preact;
using Shouldly;
using System.Reflection;
using System.Text;
using Xunit;

namespace RG.SSR.Tests.Integration;

/// <summary>
/// Integration tests for the full render pipeline.
/// Validates Requirements 4.2, 6.2, 6.3, 6.4.
/// </summary>
[Collection("Sequential")]
public class FullRenderPipelineTests
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

    private static int _counter;
    private static string NextUniqueId() => Interlocked.Increment(ref _counter).ToString();

    /// <summary>
    /// Resets the static _ssrModuleRegistered flag in ReactRenderer via reflection.
    /// This is necessary because each test creates a new ModuleLoader,
    /// but the static flag prevents re-registration of the SSR module.
    /// </summary>
    private static void ResetReactSsrModuleRegistered()
    {
        var field = typeof(ReactRenderer).GetField("_ssrModuleRegistered", BindingFlags.NonPublic | BindingFlags.Static);
        field?.SetValue(null, false);
    }

    private static (ReactRenderer renderer, ModuleLoader moduleLoader, JavaScriptEngine engine) CreateReactRenderer()
    {
        ResetReactSsrModuleRegistered();
        var moduleLoader = new ModuleLoader();
        var engine = new JavaScriptEngine(moduleLoader);
        var ssrOptions = new ServerSideRendererOptions();
        ssrOptions.React.InlineLibrary = false;
        var options = Microsoft.Extensions.Options.Options.Create(ssrOptions);
        var renderer = new ReactRenderer(engine, moduleLoader, options);
        return (renderer, moduleLoader, engine);
    }

    private static (PreactRenderer renderer, ModuleLoader moduleLoader, JavaScriptEngine engine) CreatePreactRenderer()
    {
        var moduleLoader = new ModuleLoader();
        var engine = new JavaScriptEngine(moduleLoader);
        var ssrOptions = new ServerSideRendererOptions();
        ssrOptions.React.InlineLibrary = false;
        ssrOptions.Preact.InlineLibrary = false;
        var options = Microsoft.Extensions.Options.Options.Create(ssrOptions);
        var renderer = new PreactRenderer(engine, options);
        return (renderer, moduleLoader, engine);
    }

    // ===== Test 1: 3-level dependency graph resolves and renders correctly =====

    [Fact]
    public void ReactRenderer_ThreeLevelDependencyGraph_ResolvesAndRendersCorrectly()
    {
        // Arrange: Component → utility → helper (3 levels)
        // Helper provides a greeting prefix
        // Utility uses helper to build a full greeting
        // Component uses utility to render a greeting element
        ResetReactSsrModuleRegistered();

        string uniqueId = NextUniqueId();

        // The component is an ES module that imports from 'utility'
        string componentScript = """
            import { createElement } from 'react';
            import { greet } from 'utility';

            export default function Greeting() {
                return createElement('span', null, greet('World'));
            }
            """;

        string utilityModule = """
            import { getPrefix } from 'helper';
            export function greet(name) {
                return getPrefix() + ', ' + name + '!';
            }
            """;

        string helperModule = """
            export function getPrefix() {
                return 'Hello';
            }
            """;

        var resources = new Dictionary<string, byte[]>
        {
            [$"MockAssembly_{uniqueId}.Greeting.js"] = Encoding.UTF8.GetBytes(componentScript)
        };
        var assembly = new MockAssembly(resources, uniqueId);

        var (renderer, moduleLoader, engine) = CreateReactRenderer();
        using (engine)
        {
            // Register the utility and helper modules so they can be resolved by bare specifier
            moduleLoader.RegisterModule("helper", helperModule);
            moduleLoader.RegisterModule("utility", utilityModule);

            // Act
            string output = renderer.Render(assembly, "Greeting", isStatic: true);

            // Assert: The 3-level chain resolved and rendered correctly
            output.ShouldContain("Hello, World!");
            output.ShouldContain("<span");
        }
    }

    [Fact]
    public void ReactRenderer_ThreeLevelDependencyGraph_WithProps_ResolvesAndRendersCorrectly()
    {
        // Arrange: Component → formatter → constants (3 levels with props)
        ResetReactSsrModuleRegistered();

        string uniqueId = NextUniqueId();

        string componentScript = """
            import { createElement } from 'react';
            import { formatMessage } from 'formatter';

            export default function MessageDisplay(props) {
                return createElement('div', null, formatMessage(props.name));
            }
            """;

        string formatterModule = """
            import { PREFIX, SUFFIX } from 'constants';
            export function formatMessage(name) {
                return PREFIX + name + SUFFIX;
            }
            """;

        string constantsModule = """
            export const PREFIX = 'Welcome, ';
            export const SUFFIX = '!';
            """;

        var resources = new Dictionary<string, byte[]>
        {
            [$"MockAssembly_{uniqueId}.MessageDisplay.js"] = Encoding.UTF8.GetBytes(componentScript)
        };
        var assembly = new MockAssembly(resources, uniqueId);

        var (renderer, moduleLoader, engine) = CreateReactRenderer();
        using (engine)
        {
            moduleLoader.RegisterModule("constants", constantsModule);
            moduleLoader.RegisterModule("formatter", formatterModule);

            // Act
            string output = renderer.Render(assembly, "MessageDisplay", new { Name = "Alice" }, isStatic: true);

            // Assert
            output.ShouldContain("Welcome, Alice!");
            output.ShouldContain("<div");
        }
    }

    [Fact]
    public void PreactRenderer_ThreeLevelDependencyGraph_ResolvesAndRendersCorrectly()
    {
        // Arrange: Component → utility → helper (3 levels) through Preact renderer
        string uniqueId = NextUniqueId();

        string componentScript = """
            export function Greeting() {
                return createElement('p', null, buildGreeting('World'));
            }
            import { buildGreeting } from 'greeter';
            """;

        string greeterModule = """
            import { exclaim } from 'punctuation';
            export function buildGreeting(name) {
                return 'Hi, ' + name + exclaim();
            }
            """;

        string punctuationModule = """
            export function exclaim() {
                return '!!!';
            }
            """;

        var resources = new Dictionary<string, byte[]>
        {
            [$"MockAssembly_{uniqueId}.Greeting.js"] = Encoding.UTF8.GetBytes(componentScript)
        };
        var assembly = new MockAssembly(resources, uniqueId);

        var (renderer, moduleLoader, engine) = CreatePreactRenderer();
        using (engine)
        {
            moduleLoader.RegisterModule("punctuation", punctuationModule);
            moduleLoader.RegisterModule("greeter", greeterModule);

            // Act
            string output = renderer.Render(assembly, "Greeting", isStatic: true);

            // Assert
            output.ShouldContain("Hi, World!!!");
            output.ShouldContain("<p");
        }
    }

    // ===== Test 2: Plain script components produce identical output to prior behavior =====

    [Fact]
    public void ReactRenderer_PlainScriptComponent_ProducesExpectedOutput()
    {
        // Arrange: A plain script component (no import/export) should render via engine.Evaluate
        string uniqueId = NextUniqueId();

        string componentScript = "function SimpleCard() { return { tag: 'div', props: null, children: ['Hello Plain'] }; }";

        var resources = new Dictionary<string, byte[]>
        {
            [$"MockAssembly_{uniqueId}.SimpleCard.js"] = Encoding.UTF8.GetBytes(componentScript)
        };
        var assembly = new MockAssembly(resources, uniqueId);

        var (renderer, _, engine) = CreateReactRenderer();
        using (engine)
        {
            // Act
            string output = renderer.Render(assembly, "SimpleCard", isStatic: true);

            // Assert: Plain script renders correctly
            output.ShouldContain("<div");
            output.ShouldContain("Hello Plain");
        }
    }

    [Fact]
    public void ReactRenderer_PlainScriptComponent_WithProps_ProducesExpectedOutput()
    {
        // Arrange: A plain script component with props
        string uniqueId = NextUniqueId();

        string componentScript = "function Greeter(props) { return { tag: 'h1', props: null, children: ['Hello, ' + props.name] }; }";

        var resources = new Dictionary<string, byte[]>
        {
            [$"MockAssembly_{uniqueId}.Greeter.js"] = Encoding.UTF8.GetBytes(componentScript)
        };
        var assembly = new MockAssembly(resources, uniqueId);

        var (renderer, _, engine) = CreateReactRenderer();
        using (engine)
        {
            // Act
            string output = renderer.Render(assembly, "Greeter", new { Name = "Bob" }, isStatic: true);

            // Assert
            output.ShouldContain("<h1");
            output.ShouldContain("Hello, Bob");
        }
    }

    [Fact]
    public void PreactRenderer_PlainScriptComponent_ProducesExpectedOutput()
    {
        // Arrange: A plain script component through Preact renderer
        string uniqueId = NextUniqueId();

        string componentScript = "function InfoBox() { return { tag: 'section', props: null, children: ['Info content'] }; }";

        var resources = new Dictionary<string, byte[]>
        {
            [$"MockAssembly_{uniqueId}.InfoBox.js"] = Encoding.UTF8.GetBytes(componentScript)
        };
        var assembly = new MockAssembly(resources, uniqueId);

        var (renderer, _, engine) = CreatePreactRenderer();
        using (engine)
        {
            // Act
            string output = renderer.Render(assembly, "InfoBox", isStatic: true);

            // Assert
            output.ShouldContain("<section");
            output.ShouldContain("Info content");
        }
    }

    [Fact]
    public void ReactRenderer_PlainScript_And_EsModule_ProduceEquivalentHtml()
    {
        // Arrange: Same component logic, one as plain script, one as ES module
        // Both should produce the same rendered HTML content
        ResetReactSsrModuleRegistered();

        string plainUniqueId = NextUniqueId();
        string moduleUniqueId = NextUniqueId();

        string plainScript = "function TestComp() { return { tag: 'div', props: null, children: ['Same Output'] }; }";
        string moduleScript = """
            import { createElement } from 'react';
            export default function TestComp() {
                return { tag: 'div', props: null, children: ['Same Output'] };
            }
            """;

        var plainResources = new Dictionary<string, byte[]>
        {
            [$"MockAssembly_{plainUniqueId}.TestComp.js"] = Encoding.UTF8.GetBytes(plainScript)
        };
        var plainAssembly = new MockAssembly(plainResources, plainUniqueId);

        var moduleResources = new Dictionary<string, byte[]>
        {
            [$"MockAssembly_{moduleUniqueId}.TestComp.js"] = Encoding.UTF8.GetBytes(moduleScript)
        };
        var moduleAssembly = new MockAssembly(moduleResources, moduleUniqueId);

        // Render plain script
        var (plainRenderer, _, plainEngine) = CreateReactRenderer();
        string plainOutput;
        using (plainEngine)
        {
            plainOutput = plainRenderer.Render(plainAssembly, "TestComp", isStatic: true);
        }

        // Render ES module
        ResetReactSsrModuleRegistered();
        var (moduleRenderer, _, moduleEngine) = CreateReactRenderer();
        string moduleOutput;
        using (moduleEngine)
        {
            moduleOutput = moduleRenderer.Render(moduleAssembly, "TestComp", isStatic: true);
        }

        // Assert: Both produce the same HTML content
        plainOutput.ShouldBe(moduleOutput);
    }

    // ===== Test 3: Option classes retain default values (backward compatibility) =====

    [Fact]
    public void ServerSideRendererOptions_RetainsDefaultValues()
    {
        // Arrange & Act
        var options = new ServerSideRendererOptions();

        // Assert: React and Preact sub-options are initialized
        options.React.ShouldNotBeNull();
        options.Preact.ShouldNotBeNull();
    }

    [Fact]
    public void ReactOptions_RetainsAllDefaultValues()
    {
        // Arrange & Act
        var options = new ReactOptions();

        // Assert: All properties have their expected default values
        options.ReactLibraryResourceName.ShouldBe("umd.react.production.min.js");
        options.ReactDomLibraryResourceName.ShouldBe("umd.react-dom.production.min.js");
        options.InlineLibrary.ShouldBeTrue();
    }

    [Fact]
    public void PreactOptions_RetainsAllDefaultValues()
    {
        // Arrange & Act
        var options = new PreactOptions();

        // Assert: All properties have their expected default values
        options.PreactUmdLibraryResourceName.ShouldBe("preact.umd.min.js");
        options.PreactHooksUmdLibraryResourceName.ShouldBe("preact.hooks.umd.min.js");
        options.PreactCompatUmdLibraryResourceName.ShouldBe("preact.compat.umd.min.js");
        options.InlineLibrary.ShouldBeTrue();
        options.ReactCompat.ShouldBeFalse();
    }

    [Fact]
    public void ReactOptions_PropertiesAreSettable()
    {
        // Arrange & Act: Verify properties can be set (backward compat with existing config code)
        var options = new ReactOptions
        {
            ReactLibraryResourceName = "custom-react.js",
            ReactDomLibraryResourceName = "custom-react-dom.js",
            InlineLibrary = false
        };

        // Assert
        options.ReactLibraryResourceName.ShouldBe("custom-react.js");
        options.ReactDomLibraryResourceName.ShouldBe("custom-react-dom.js");
        options.InlineLibrary.ShouldBeFalse();
    }

    [Fact]
    public void PreactOptions_PropertiesAreSettable()
    {
        // Arrange & Act: Verify properties can be set (backward compat with existing config code)
        var options = new PreactOptions
        {
            PreactUmdLibraryResourceName = "custom-preact.js",
            PreactHooksUmdLibraryResourceName = "custom-hooks.js",
            PreactCompatUmdLibraryResourceName = "custom-compat.js",
            InlineLibrary = false,
            ReactCompat = true
        };

        // Assert
        options.PreactUmdLibraryResourceName.ShouldBe("custom-preact.js");
        options.PreactHooksUmdLibraryResourceName.ShouldBe("custom-hooks.js");
        options.PreactCompatUmdLibraryResourceName.ShouldBe("custom-compat.js");
        options.InlineLibrary.ShouldBeFalse();
        options.ReactCompat.ShouldBeTrue();
    }

    [Fact]
    public void ServerSideRendererOptions_ReactAndPreactSubOptions_HaveCorrectTypes()
    {
        // Verify the option class structure hasn't changed (backward compatibility)
        var options = new ServerSideRendererOptions();

        // The React property should be of type ReactOptions
        options.React.ShouldBeOfType<ReactOptions>();

        // The Preact property should be of type PreactOptions
        options.Preact.ShouldBeOfType<PreactOptions>();
    }
}
