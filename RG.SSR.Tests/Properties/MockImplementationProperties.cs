using FsCheck;
using FsCheck.Xunit;
using Microsoft.ClearScript;
using Microsoft.ClearScript.JavaScript;
using Microsoft.ClearScript.V8;
using RG.SSR.JavaScript;

namespace RG.SSR.Tests.Properties;

// Feature: es-module-support, Property 10: SSR Mock Implementations
// Validates: Requirements 3.4

/// <summary>
/// Property-based tests verifying that SSR mock implementations behave correctly:
/// - useState returns [initialState, noop]
/// - createElement returns {tag, props, children}
/// - useMemo returns the factory result
/// - useCallback returns the callback unchanged
/// </summary>
public class MockImplementationProperties : IDisposable
{
    private readonly V8ScriptEngine _engine;
    private readonly ModuleLoader _moduleLoader;

    public MockImplementationProperties()
    {
        _moduleLoader = new ModuleLoader();
        _engine = new V8ScriptEngine();
        _engine.DocumentSettings.Loader = _moduleLoader;
    }

    public void Dispose()
    {
        _engine.Dispose();
    }

    /// <summary>
    /// Generates random initial state values representable as JavaScript literals.
    /// </summary>
    private static Arbitrary<string> InitialStateArbitrary()
    {
        var gen = Gen.OneOf(
            Arb.Default.Int32().Generator.Select(i => i.ToString()),
            Arb.Default.Float().Generator
                .Where(f => !double.IsNaN(f) && !double.IsInfinity(f))
                .Select(f => f.ToString("G", System.Globalization.CultureInfo.InvariantCulture)),
            Gen.Elements("true", "false", "null"),
            Arb.Default.NonEmptyString().Generator
                .Select(s => "'" + s.Get.Replace("\\", "\\\\").Replace("'", "\\'").Replace("\n", "\\n").Replace("\r", "\\r") + "'")
        );
        return gen.ToArbitrary();
    }

    /// <summary>
    /// Generates random HTML tag names for createElement tests.
    /// </summary>
    private static Arbitrary<string> TagNameArbitrary()
    {
        return Gen.Elements(
            "div", "span", "p", "h1", "h2", "h3", "ul", "li", "a",
            "button", "input", "form", "section", "article", "header", "footer",
            "nav", "main", "table", "tr", "td", "img"
        ).ToArbitrary();
    }

    /// <summary>
    /// Generates random props objects as JavaScript object literal strings.
    /// </summary>
    private static Arbitrary<string> PropsArbitrary()
    {
        var gen = Gen.OneOf(
            Gen.Constant("null"),
            Gen.Constant("{}"),
            Gen.Elements(
                "{ className: 'test' }",
                "{ id: 'myId' }",
                "{ onClick: () => {} }",
                "{ style: { color: 'red' } }",
                "{ disabled: true }",
                "{ href: '/path' }",
                "{ value: 42 }",
                "{ name: 'field', type: 'text' }",
                "{ 'data-testid': 'item' }"
            )
        );
        return gen.ToArbitrary();
    }

    /// <summary>
    /// Generates random children arrays as JavaScript array literal strings.
    /// </summary>
    private static Arbitrary<string> ChildrenArbitrary()
    {
        var gen = Gen.OneOf(
            Gen.Constant(""),
            Gen.Constant(", 'hello'"),
            Gen.Constant(", 'child1', 'child2'"),
            Gen.Constant(", 'text', null, 'more'"),
            Gen.Constant(", 42"),
            Gen.Constant(", true, false"),
            Gen.Constant(", 'a', 'b', 'c'")
        );
        return gen.ToArbitrary();
    }

    /// <summary>
    /// Generates random factory return values for useMemo tests.
    /// These must be valid JavaScript expressions that work in both
    /// `() => expr` and standalone `expr` contexts.
    /// </summary>
    private static Arbitrary<string> FactoryResultArbitrary()
    {
        return Gen.OneOf(
            Arb.Default.Int32().Generator.Select(i => i.ToString()),
            Gen.Elements("'hello'", "'world'", "'computed'", "'memo_result'"),
            Gen.Elements("true", "false", "null"),
            Gen.Elements("42", "0", "-1", "100", "999"),
            Gen.Elements("[]", "[1,2,3]")
        ).ToArbitrary();
    }

    /// <summary>
    /// Property: For any initial state value, useState returns [initialState, noop].
    /// The first element is the initial state, and the second element is a function (noop).
    /// </summary>
    [Property(MaxTest = 100)]
    public Property UseState_ReturnsInitialStateAndNoop()
    {
        return Prop.ForAll(
            InitialStateArbitrary(),
            (string initialState) =>
            {
                // Evaluate a module that imports useState and tests it
                var moduleCode = $$"""
import { useState } from 'preact/hooks';
const result = useState({{initialState}});
const isArray = Array.isArray(result);
const hasLength2 = result.length === 2;
const firstElement = JSON.stringify(result[0]);
const secondIsFunction = typeof result[1] === 'function';
JSON.stringify({ isArray, hasLength2, firstElement, secondIsFunction });
""";
                var result = _engine.Evaluate(
                    new DocumentInfo { Category = ModuleCategory.Standard },
                    moduleCode
                ) as string;

                if (result == null) return false.Label("Module evaluation returned null");

                // Parse the JSON result
                var expected = System.Text.Json.JsonDocument.Parse(result);
                var root = expected.RootElement;

                var isArray = root.GetProperty("isArray").GetBoolean();
                var hasLength2 = root.GetProperty("hasLength2").GetBoolean();
                var secondIsFunction = root.GetProperty("secondIsFunction").GetBoolean();

                // Verify the first element matches the initial state
                var firstElementJson = root.GetProperty("firstElement").GetString();
                var expectedJson = _engine.Evaluate($"JSON.stringify({initialState})") as string;

                return (isArray && hasLength2 && secondIsFunction && firstElementJson == expectedJson)
                    .Label($"useState({initialState}) should return [initialState, noop]. " +
                           $"isArray={isArray}, hasLength2={hasLength2}, secondIsFunction={secondIsFunction}, " +
                           $"firstElement={firstElementJson}, expected={expectedJson}");
            });
    }

    /// <summary>
    /// Property: For any tag, props, and children, createElement returns {tag, props, children}.
    /// </summary>
    [Property(MaxTest = 100)]
    public Property CreateElement_ReturnsVirtualDomObject()
    {
        return Prop.ForAll(
            TagNameArbitrary(),
            PropsArbitrary(),
            ChildrenArbitrary(),
            (string tag, string props, string children) =>
            {
                var moduleCode = $$"""
import { createElement } from 'preact';
const result = createElement('{{tag}}', {{props}}{{children}});
const hasTag = result.tag === '{{tag}}';
const hasProps = JSON.stringify(result.props) === JSON.stringify({{props}});
const hasChildren = Array.isArray(result.children);
JSON.stringify({ hasTag, hasProps, hasChildren, tag: result.tag });
""";
                var result = _engine.Evaluate(
                    new DocumentInfo { Category = ModuleCategory.Standard },
                    moduleCode
                ) as string;

                if (result == null) return false.Label("Module evaluation returned null");

                var parsed = System.Text.Json.JsonDocument.Parse(result);
                var root = parsed.RootElement;

                var hasTag = root.GetProperty("hasTag").GetBoolean();
                var hasProps = root.GetProperty("hasProps").GetBoolean();
                var hasChildren = root.GetProperty("hasChildren").GetBoolean();
                var returnedTag = root.GetProperty("tag").GetString();

                return (hasTag && hasProps && hasChildren)
                    .Label($"createElement('{tag}', {props}{children}) should return {{tag, props, children}}. " +
                           $"hasTag={hasTag}, hasProps={hasProps}, hasChildren={hasChildren}, returnedTag={returnedTag}");
            });
    }

    /// <summary>
    /// Property: For any factory function, useMemo returns the factory result.
    /// </summary>
    [Property(MaxTest = 100)]
    public Property UseMemo_ReturnsFactoryResult()
    {
        return Prop.ForAll(
            FactoryResultArbitrary(),
            (string factoryResult) =>
            {
                var moduleCode = $$"""
import { useMemo } from 'preact/hooks';
function factory() { return {{factoryResult}}; }
const result = useMemo(factory);
const expected = factory();
const resultJson = JSON.stringify(result);
const expectedJson = JSON.stringify(expected);
const areEqual = resultJson === expectedJson;
JSON.stringify({ resultJson, expectedJson, areEqual });
""";
                var result = _engine.Evaluate(
                    new DocumentInfo { Category = ModuleCategory.Standard },
                    moduleCode
                ) as string;

                if (result == null) return false.Label("Module evaluation returned null");

                var parsed = System.Text.Json.JsonDocument.Parse(result);
                var root = parsed.RootElement;

                var areEqual = root.GetProperty("areEqual").GetBoolean();
                var resultStr = root.GetProperty("resultJson").GetString();
                var expectedStr = root.GetProperty("expectedJson").GetString();

                return areEqual
                    .Label($"useMemo(() => {factoryResult}) should return factory(). " +
                           $"result={resultStr}, expected={expectedStr}");
            });
    }

    /// <summary>
    /// Property: For any callback function, useCallback returns the callback unchanged.
    /// </summary>
    [Property(MaxTest = 100)]
    public Property UseCallback_ReturnsCallbackUnchanged()
    {
        return Prop.ForAll(
            FactoryResultArbitrary(),
            (string returnValue) =>
            {
                var moduleCode = $$"""
import { useCallback } from 'preact/hooks';
const myCallback = () => {{returnValue}};
const result = useCallback(myCallback);
const isSameReference = result === myCallback;
const callResult = result();
const expectedResult = myCallback();
const sameOutput = JSON.stringify(callResult) === JSON.stringify(expectedResult);
JSON.stringify({ isSameReference, sameOutput });
""";
                var result = _engine.Evaluate(
                    new DocumentInfo { Category = ModuleCategory.Standard },
                    moduleCode
                ) as string;

                if (result == null) return false.Label("Module evaluation returned null");

                var parsed = System.Text.Json.JsonDocument.Parse(result);
                var root = parsed.RootElement;

                var isSameReference = root.GetProperty("isSameReference").GetBoolean();
                var sameOutput = root.GetProperty("sameOutput").GetBoolean();

                return (isSameReference && sameOutput)
                    .Label($"useCallback(cb) should return cb unchanged. " +
                           $"isSameReference={isSameReference}, sameOutput={sameOutput}");
            });
    }
}
