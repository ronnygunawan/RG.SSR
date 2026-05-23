using Microsoft.ClearScript.JavaScript;
using RG.SSR.JavaScript;
using Shouldly;
using Xunit;

namespace RG.SSR.Tests.Integration;

/// <summary>
/// Integration tests verifying transitive dependency resolution works end-to-end.
/// Validates Requirements 4.1, 4.2, 4.5.
/// </summary>
public class TransitiveDependencyTests : IDisposable
{
    private readonly ModuleLoader _moduleLoader;
    private readonly JavaScriptEngine _engine;

    public TransitiveDependencyTests()
    {
        _moduleLoader = new ModuleLoader();
        _engine = new JavaScriptEngine(_moduleLoader);
    }

    [Fact]
    public void ThreeLevelDependencyChain_ResolvesAndRendersCorrectly()
    {
        // Arrange: 3-level dependency chain: Component → Utility → Helper
        // Helper exports a function that returns a greeting prefix
        string helperModule = """
export function getPrefix() {
    return 'Hello';
}
""";

        // Utility imports Helper and builds a full greeting
        string utilityModule = """
import { getPrefix } from 'helper';
export function greet(name) {
    return getPrefix() + ', ' + name + '!';
}
""";

        // Component imports Utility and uses it to produce output
        string componentModule = """
import { greet } from 'utility';
const message = greet('World');
message;
""";

        _moduleLoader.RegisterModule("helper", helperModule);
        _moduleLoader.RegisterModule("utility", utilityModule);

        // Act: Evaluate the component module with assembly context set
        // Using the test assembly as the component assembly (it won't be used for bare specifiers)
        string result = _engine.RenderModule(componentModule, typeof(TransitiveDependencyTests).Assembly);

        // Assert: The transitive chain resolved correctly
        result.ShouldBe("Hello, World!");
    }

    [Fact]
    public void ThreeLevelDependencyChain_WithObjectExports_ResolvesCorrectly()
    {
        // Arrange: Helper exports configuration data
        string helperModule = """
export const config = { separator: ' - ', suffix: '!!!' };
""";

        // Utility imports Helper config and uses it to format
        string utilityModule = """
import { config } from 'helper';
export function format(a, b) {
    return a + config.separator + b + config.suffix;
}
""";

        // Component imports Utility
        string componentModule = """
import { format } from 'utility';
const result = format('Left', 'Right');
result;
""";

        _moduleLoader.RegisterModule("helper", helperModule);
        _moduleLoader.RegisterModule("utility", utilityModule);

        // Act
        string result = _engine.RenderModule(componentModule, typeof(TransitiveDependencyTests).Assembly);

        // Assert
        result.ShouldBe("Left - Right!!!");
    }

    [Fact]
    public void FourLevelDependencyChain_ResolvesCorrectly()
    {
        // Arrange: 4-level chain: Component → Service → Utility → Constants
        string constantsModule = """
export const GREETING = 'Hi';
export const PUNCTUATION = '!';
""";

        string utilityModule = """
import { GREETING, PUNCTUATION } from 'constants';
export function buildMessage(name) {
    return GREETING + ' ' + name + PUNCTUATION;
}
""";

        string serviceModule = """
import { buildMessage } from 'utility';
export function getWelcome(user) {
    return 'Welcome: ' + buildMessage(user);
}
""";

        string componentModule = """
import { getWelcome } from 'service';
const output = getWelcome('Alice');
output;
""";

        _moduleLoader.RegisterModule("constants", constantsModule);
        _moduleLoader.RegisterModule("utility", utilityModule);
        _moduleLoader.RegisterModule("service", serviceModule);

        // Act
        string result = _engine.RenderModule(componentModule, typeof(TransitiveDependencyTests).Assembly);

        // Assert
        result.ShouldBe("Welcome: Hi Alice!");
    }

    [Fact]
    public void CircularDependency_DoesNotCauseInfiniteLoop()
    {
        // Arrange: Module A imports Module B, Module B imports Module A
        // Per ES module spec, circular references resolve without infinite loops.
        // Already-evaluated exports are available; not-yet-evaluated exports are undefined.
        string moduleA = """
import { getB } from 'moduleB';
export function getA() {
    return 'A';
}
export function getAB() {
    return getA() + getB();
}
""";

        string moduleB = """
import { getA } from 'moduleA';
export function getB() {
    return 'B';
}
export function getBA() {
    return getB() + (typeof getA === 'function' ? getA() : '?');
}
""";

        string componentModule = """
import { getB } from 'moduleB';
import { getA } from 'moduleA';
const result = getA() + getB();
result;
""";

        _moduleLoader.RegisterModule("moduleA", moduleA);
        _moduleLoader.RegisterModule("moduleB", moduleB);

        // Act: Should not throw or hang
        string result = _engine.RenderModule(componentModule, typeof(TransitiveDependencyTests).Assembly);

        // Assert: Both modules resolved (circular dependency handled by V8)
        result.ShouldBe("AB");
    }

    [Fact]
    public void AssemblyContext_RemainsSetDuringFullModuleGraphEvaluation()
    {
        // Arrange: Verify that the assembly context is set once before evaluation
        // and cleared after. We test this indirectly by ensuring that a multi-level
        // chain that uses the assembly context (via relative specifiers or registered modules)
        // all resolves within a single RenderModule call.
        string baseModule = """
export function base() { return 'base'; }
""";

        string midModule = """
import { base } from 'baseModule';
export function mid() { return base() + '-mid'; }
""";

        string topModule = """
import { mid } from 'midModule';
const result = mid() + '-top';
result;
""";

        _moduleLoader.RegisterModule("baseModule", baseModule);
        _moduleLoader.RegisterModule("midModule", midModule);

        // Act: Single RenderModule call should resolve the entire graph
        string result = _engine.RenderModule(topModule, typeof(TransitiveDependencyTests).Assembly);

        // Assert: Full chain resolved within one evaluation context
        result.ShouldBe("base-mid-top");
    }

    [Fact]
    public void TransitiveDependency_WithFrameworkModule_ResolvesCorrectly()
    {
        // Arrange: Component → Utility → Framework (react)
        // Utility uses createElement from react
        string utilityModule = """
import { createElement } from 'react';
export function createDiv(text) {
    return createElement('div', null, text);
}
""";

        // Component imports utility and renders
        string componentModule = """
import { createDiv } from 'utility';
const vdom = createDiv('Hello');
JSON.stringify(vdom);
""";

        _moduleLoader.RegisterModule("utility", utilityModule);

        // Act
        string result = _engine.RenderModule(componentModule, typeof(TransitiveDependencyTests).Assembly);

        // Assert: createElement from the framework module was resolved transitively
        result.ShouldContain("\"tag\":\"div\"");
        result.ShouldContain("Hello");
    }

    [Fact]
    public void MultipleModulesImportingSameDependency_ShareModuleInstance()
    {
        // Arrange: Two modules import the same dependency - they should get the same instance
        string sharedModule = """
export const sharedObject = { value: 42 };
""";

        string moduleA = """
import { sharedObject } from 'shared';
export function getFromA() { return sharedObject; }
""";

        string moduleB = """
import { sharedObject } from 'shared';
export function getFromB() { return sharedObject; }
""";

        string componentModule = """
import { getFromA } from 'modA';
import { getFromB } from 'modB';
const same = getFromA() === getFromB();
same.toString();
""";

        _moduleLoader.RegisterModule("shared", sharedModule);
        _moduleLoader.RegisterModule("modA", moduleA);
        _moduleLoader.RegisterModule("modB", moduleB);

        // Act
        string result = _engine.RenderModule(componentModule, typeof(TransitiveDependencyTests).Assembly);

        // Assert: Same module instance is shared (object identity)
        result.ShouldBe("true");
    }

    public void Dispose()
    {
        _engine.Dispose();
    }
}
