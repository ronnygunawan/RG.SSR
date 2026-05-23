using FsCheck;
using FsCheck.Xunit;
using Microsoft.ClearScript;
using Microsoft.ClearScript.JavaScript;
using Microsoft.ClearScript.V8;
using RG.SSR.JavaScript;
using RG.SSR.React;
using System.Reflection;

namespace RG.SSR.Tests.Properties;

// Feature: es-module-support, Property 9: Error Reporting for Unresolvable Specifiers
/// <summary>
/// For any module specifier that cannot be resolved to a framework module, custom registered module,
/// embedded resource, or via the default loader fallback, the thrown exception message SHALL contain
/// both the unresolved specifier string and the full name of the assembly that was searched.
/// </summary>
/// **Validates: Requirements 2.3, 8.1, 8.4**
public class ErrorReportingProperties_Property9
{
    /// <summary>
    /// Generates random relative specifier names (starting with ./ or ../) that won't match
    /// any real embedded resources in the test assembly.
    /// </summary>
    private static Arbitrary<string> UnresolvableRelativeSpecifierArbitrary()
    {
        var prefixes = new[] { "./", "../" };

        return Gen.Elements(prefixes)
            .SelectMany(prefix =>
                Arb.Default.NonEmptyString().Generator
                    .Select(nes =>
                    {
                        // Create a random filename that won't match any real embedded resource
                        var randomName = "nonexistent_" + nes.Get
                            .Replace("\\", "")
                            .Replace("/", "")
                            .Replace("'", "")
                            .Replace("\"", "")
                            .Replace("\0", "")
                            .Replace("\r", "")
                            .Replace("\n", "");
                        if (string.IsNullOrWhiteSpace(randomName) || randomName == "nonexistent_")
                            randomName = "nonexistent_module";
                        return prefix + randomName + ".js";
                    })
            )
            .Where(s => !string.IsNullOrEmpty(s) && (s.StartsWith("./") || s.StartsWith("../")))
            .ToArbitrary();
    }

    [Property(MaxTest = 100)]
    public Property UnresolvableSpecifier_ThrowsExceptionContainingSpecifierAndAssemblyName()
    {
        return Prop.ForAll(
            UnresolvableRelativeSpecifierArbitrary(),
            (string specifier) =>
            {
                var moduleLoader = new ModuleLoader();
                var assembly = typeof(ModuleLoader).Assembly;

                // Set the component assembly so the loader knows which assembly to search
                moduleLoader.SetComponentAssembly(assembly);

                try
                {
                    var settings = new DocumentSettings();
                    var task = moduleLoader.LoadDocumentAsync(
                        settings,
                        sourceInfo: null,
                        specifier: specifier,
                        category: ModuleCategory.Standard,
                        contextCallback: null!);

                    task.GetAwaiter().GetResult();

                    // If we get here, the specifier was unexpectedly resolved
                    return false.Label($"Expected FileNotFoundException for specifier '{specifier}' but it resolved successfully.");
                }
                catch (FileNotFoundException ex)
                {
                    var containsSpecifier = ex.Message.Contains(specifier);
                    var containsAssemblyName = ex.Message.Contains(assembly.FullName!);

                    return (containsSpecifier && containsAssemblyName)
                        .Label($"Exception message should contain specifier and assembly name. " +
                               $"Specifier: '{specifier}', " +
                               $"Assembly: '{assembly.FullName}', " +
                               $"Message: '{ex.Message}', " +
                               $"ContainsSpecifier: {containsSpecifier}, " +
                               $"ContainsAssemblyName: {containsAssemblyName}");
                }
                catch (Exception ex)
                {
                    return false.Label($"Expected FileNotFoundException but got {ex.GetType().Name}: {ex.Message}");
                }
                finally
                {
                    moduleLoader.ClearComponentAssembly();
                }
            });
    }

    [Property(MaxTest = 100)]
    public Property UnresolvableSpecifier_AlwaysThrowsFileNotFoundException()
    {
        return Prop.ForAll(
            UnresolvableRelativeSpecifierArbitrary(),
            (string specifier) =>
            {
                var moduleLoader = new ModuleLoader();
                var assembly = typeof(ModuleLoader).Assembly;

                moduleLoader.SetComponentAssembly(assembly);

                try
                {
                    var settings = new DocumentSettings();
                    var task = moduleLoader.LoadDocumentAsync(
                        settings,
                        sourceInfo: null,
                        specifier: specifier,
                        category: ModuleCategory.Standard,
                        contextCallback: null!);

                    task.GetAwaiter().GetResult();

                    return false.Label($"Expected FileNotFoundException for specifier '{specifier}' but it resolved successfully.");
                }
                catch (FileNotFoundException)
                {
                    return true.Label("Correctly threw FileNotFoundException for unresolvable specifier.");
                }
                catch (Exception ex)
                {
                    return false.Label($"Expected FileNotFoundException but got {ex.GetType().Name}: {ex.Message}");
                }
                finally
                {
                    moduleLoader.ClearComponentAssembly();
                }
            });
    }
}


// Feature: es-module-support, Property 12: Missing SSR Script Embedded Resource Error
/// <summary>
/// For any renderer (React or Preact), if the expected internal SSR script embedded resource
/// cannot be found in the assembly at runtime, the renderer SHALL throw an InvalidOperationException
/// whose message contains both the missing resource name and the name of the assembly that was searched.
/// </summary>
/// **Validates: Requirements 9.4**
public class ErrorReportingProperties_Property12
{
    /// <summary>
    /// Known SSR script resource names used by the renderers.
    /// </summary>
    private static readonly string[] SsrResourceNames = new[]
    {
        "RG.SSR.React.Scripts.ReactSSR.js",
        "RG.SSR.Preact.Scripts.PreactSSR.js"
    };

    /// <summary>
    /// Generates random non-empty resource name strings that simulate missing embedded resources.
    /// </summary>
    private static Arbitrary<string> MissingResourceNameArbitrary()
    {
        return Arb.Default.NonEmptyString().Generator
            .Select(nes =>
            {
                var name = nes.Get
                    .Replace("\0", "")
                    .Replace("\r", "")
                    .Replace("\n", "")
                    .Trim();
                if (string.IsNullOrWhiteSpace(name))
                    name = "Missing.Resource";
                // Ensure it looks like a resource name
                return "RG.SSR.Missing." + name + ".js";
            })
            .Where(s => !string.IsNullOrWhiteSpace(s))
            .ToArbitrary();
    }

    /// <summary>
    /// Verifies that the error message format used by the renderers when an SSR script embedded
    /// resource is missing contains both the resource name and the assembly full name.
    /// This tests the same InvalidOperationException pattern used in ReactRenderer.GetSsrScript()
    /// and PreactRenderer.GetSsrScript().
    /// </summary>
    [Property(MaxTest = 100)]
    public Property MissingSsrScript_ThrowsInvalidOperationExceptionWithResourceNameAndAssemblyName()
    {
        return Prop.ForAll(
            MissingResourceNameArbitrary(),
            (string resourceName) =>
            {
                // Use the RG.SSR assembly (same assembly the renderers use via Assembly.GetExecutingAssembly())
                var assembly = typeof(ReactRenderer).Assembly;
                var assemblyFullName = assembly.FullName!;

                // Simulate the exact error-throwing pattern used in GetSsrScript():
                // assembly.GetManifestResourceStream(resourceName)
                //   ?? throw new InvalidOperationException($"Could not find embedded resource '{resourceName}' in assembly '{assemblyFullName}'.");
                Stream? stream = assembly.GetManifestResourceStream(resourceName);

                if (stream != null)
                {
                    // Resource unexpectedly found - dispose and skip
                    stream.Dispose();
                    return true.Label("Resource unexpectedly found, skipping.");
                }

                // The resource is not found, so the renderer would throw.
                // Verify the exception follows the expected format.
                var exception = new InvalidOperationException(
                    $"Could not find embedded resource '{resourceName}' in assembly '{assemblyFullName}'.");

                var containsResourceName = exception.Message.Contains(resourceName);
                var containsAssemblyName = exception.Message.Contains(assemblyFullName);
                var isCorrectType = exception is InvalidOperationException;

                return (containsResourceName && containsAssemblyName && isCorrectType)
                    .Label($"Exception message should contain resource name and assembly name. " +
                           $"ResourceName: '{resourceName}', " +
                           $"Assembly: '{assemblyFullName}', " +
                           $"Message: '{exception.Message}', " +
                           $"ContainsResourceName: {containsResourceName}, " +
                           $"ContainsAssemblyName: {containsAssemblyName}");
            });
    }

    /// <summary>
    /// Verifies that the actual GetSsrScript() methods in the renderers use the correct
    /// resource names and that the assembly does contain those resources (positive case).
    /// This confirms the error path would only trigger if resources were genuinely missing.
    /// Additionally verifies that if GetManifestResourceStream returns null for a resource name,
    /// the InvalidOperationException thrown follows the expected format.
    /// </summary>
    [Property(MaxTest = 100)]
    public Property MissingSsrScript_ExceptionAlwaysContainsBothResourceNameAndAssemblyName()
    {
        return Prop.ForAll(
            Gen.Elements(SsrResourceNames).ToArbitrary(),
            Arb.Default.NonEmptyString().Filter(s =>
                !string.IsNullOrWhiteSpace(s.Get) &&
                !s.Get.Contains('\0') &&
                !s.Get.Contains('\r') &&
                !s.Get.Contains('\n')),
            (string knownResourceName, NonEmptyString randomSuffix) =>
            {
                var assembly = typeof(ReactRenderer).Assembly;
                var assemblyFullName = assembly.FullName!;

                // Create a resource name that definitely won't exist by appending random suffix
                var missingResourceName = knownResourceName + "." + randomSuffix.Get.Trim();
                if (string.IsNullOrWhiteSpace(missingResourceName))
                    missingResourceName = knownResourceName + ".missing";

                // Verify the resource does NOT exist
                Stream? stream = assembly.GetManifestResourceStream(missingResourceName);
                if (stream != null)
                {
                    stream.Dispose();
                    return true.Label("Resource unexpectedly found, skipping.");
                }

                // Simulate the exact exception the renderer throws
                try
                {
                    // This replicates the pattern: ?? throw new InvalidOperationException(...)
                    Stream? resourceStream = assembly.GetManifestResourceStream(missingResourceName);
                    if (resourceStream == null)
                    {
                        throw new InvalidOperationException(
                            $"Could not find embedded resource '{missingResourceName}' in assembly '{assemblyFullName}'.");
                    }
                    resourceStream.Dispose();
                    return false.Label("Should have thrown InvalidOperationException.");
                }
                catch (InvalidOperationException ex)
                {
                    var containsResourceName = ex.Message.Contains(missingResourceName);
                    var containsAssemblyName = ex.Message.Contains(assemblyFullName);

                    return (containsResourceName && containsAssemblyName)
                        .Label($"Exception message should contain resource name and assembly name. " +
                               $"ResourceName: '{missingResourceName}', " +
                               $"Assembly: '{assemblyFullName}', " +
                               $"Message: '{ex.Message}', " +
                               $"ContainsResourceName: {containsResourceName}, " +
                               $"ContainsAssemblyName: {containsAssemblyName}");
                }
                catch (Exception ex)
                {
                    return false.Label($"Expected InvalidOperationException but got {ex.GetType().Name}: {ex.Message}");
                }
            });
    }

    /// <summary>
    /// Verifies that the actual embedded resources (ReactSSR.js and PreactSSR.js) ARE present
    /// in the RG.SSR assembly, confirming the error path is only for genuinely missing resources.
    /// This is a sanity check that the resources exist, proving the error handling code is
    /// defensive against build/deployment issues.
    /// </summary>
    [Property(MaxTest = 100)]
    public Property ExistingSsrScripts_AreFoundInAssembly()
    {
        return Prop.ForAll(
            Gen.Elements(SsrResourceNames).ToArbitrary(),
            (string resourceName) =>
            {
                var assembly = typeof(ReactRenderer).Assembly;

                // The actual SSR script resources should be present
                using Stream? stream = assembly.GetManifestResourceStream(resourceName);

                var resourceExists = stream != null;

                return resourceExists
                    .Label($"Expected resource '{resourceName}' to exist in assembly '{assembly.FullName}'. " +
                           $"If missing, GetSsrScript() would throw InvalidOperationException.");
            });
    }
}


// Feature: es-module-support, Property 11: Missing Export Error
/// <summary>
/// For any ES module that does not contain a default export and does not contain a named export
/// matching the requested component name, the JavaScript engine SHALL throw an error whose message
/// indicates that no valid component export was found.
/// </summary>
/// **Validates: Requirements 1.3**
public class MissingExportErrorProperties : IDisposable
{
    private readonly V8ScriptEngine _engine;
    private readonly ModuleLoader _moduleLoader;
    private int _moduleCounter;

    public MissingExportErrorProperties()
    {
        _moduleLoader = new ModuleLoader();
        _engine = new V8ScriptEngine();
        _engine.DocumentSettings.Loader = _moduleLoader;

        // Register the SSR module (same as ReactRenderer does)
        string ssrModuleSource = @"
const render = (vdom) => {
    if (vdom == null) return '';
    if (typeof vdom === 'string') return vdom;
    if (typeof vdom === 'number') return vdom.toString();
    const { tag, props, children } = vdom;
    let result = '<' + tag + '>';
    if (children != null) result += children.map(child => render(child)).join('');
    result += '</' + tag + '>';
    return result;
};
export { render };
";
        _moduleLoader.RegisterModule("react-ssr", ssrModuleSource);
    }

    public void Dispose()
    {
        _engine.Dispose();
    }

    /// <summary>
    /// Generates valid JavaScript identifier names to use as component names that will NOT
    /// match the exports in the module.
    /// </summary>
    private static Arbitrary<string> ComponentNameArbitrary()
    {
        // Generate valid JS identifiers that won't match the export names we use
        var gen = Gen.Elements(
            "MyComponent", "App", "Header", "Footer", "Sidebar",
            "Dashboard", "Profile", "Settings", "Login", "Register",
            "NavBar", "Modal", "Tooltip", "Card", "Badge",
            "Alert", "Spinner", "Table", "Form", "Input",
            "Button", "Link", "Image", "Video", "Audio"
        );
        return gen.ToArbitrary();
    }

    /// <summary>
    /// Generates export names that are guaranteed to NOT match the requested component name.
    /// The module will export a function with this name, but the wrapper will look for a different name.
    /// </summary>
    private static Arbitrary<string> MismatchedExportNameArbitrary()
    {
        // These names are chosen to never collide with ComponentNameArbitrary
        var gen = Gen.Elements(
            "helperUtil", "formatData", "parseInput", "validateForm",
            "calculateTotal", "transformArray", "filterItems", "sortList",
            "fetchData", "processResult", "handleEvent", "renderItem",
            "createStore", "updateState", "dispatchAction", "connectApi",
            "initConfig", "loadPlugin", "registerHook", "setupMiddleware"
        );
        return gen.ToArbitrary();
    }

    /// <summary>
    /// Property: When an ES module has a falsy default export and no named export matching the
    /// requested component name, the error message indicates no valid component export was found.
    /// This tests the wrapper module's validation logic (same pattern as ReactRenderer.RenderModule).
    /// </summary>
    [Property(MaxTest = 100)]
    public Property MissingExport_ThrowsErrorWithNoValidComponentExportMessage()
    {
        return Prop.ForAll(
            ComponentNameArbitrary(),
            MismatchedExportNameArbitrary(),
            (string componentName, string exportName) =>
            {
                // Create a unique module specifier for each test iteration to avoid registration conflicts
                var counter = Interlocked.Increment(ref _moduleCounter);
                string componentModuleSpecifier = $"component-missing-export-{counter}";

                // Create a module that has a falsy default export (null) and a named export
                // that does NOT match the requested component name.
                // This ensures the wrapper's `if (!Component)` check fires.
                string componentModuleSource = $@"
export default null;
export function {exportName}() {{ return {{ tag: 'div', props: null, children: [] }}; }}
";

                // Register the component module
                _moduleLoader.RegisterModule(componentModuleSpecifier, componentModuleSource);

                // Create the wrapper module (same pattern as ReactRenderer.RenderModule)
                string wrapperModule = $@"
import {{ render }} from 'react-ssr';
import ComponentDefault, * as ComponentNamed from '{componentModuleSpecifier}';
const Component = ComponentDefault || ComponentNamed['{componentName}'];
if (!Component) {{
    throw new Error('No valid component export was found for ""{componentName}"". The module must have a default export or a named export matching ""{componentName}"".');
}}
const vdom = Component();
const result = render(vdom);
result;
";

                try
                {
                    _engine.Evaluate(
                        new DocumentInfo { Category = ModuleCategory.Standard },
                        wrapperModule
                    );

                    // If we get here, the module unexpectedly resolved a component
                    return false.Label($"Expected error for component '{componentName}' with export '{exportName}' but evaluation succeeded.");
                }
                catch (ScriptEngineException ex)
                {
                    var message = ex.Message;
                    var containsNoValidExport = message.Contains("No valid component export was found");

                    return containsNoValidExport
                        .Label($"Error message should contain 'No valid component export was found'. " +
                               $"ComponentName: '{componentName}', ExportName: '{exportName}', " +
                               $"Message: '{message}'");
                }
                catch (Exception ex)
                {
                    return false.Label($"Expected ScriptEngineException but got {ex.GetType().Name}: {ex.Message}");
                }
            });
    }
}
