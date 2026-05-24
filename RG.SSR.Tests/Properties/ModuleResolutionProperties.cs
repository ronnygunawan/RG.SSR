using FsCheck;
using FsCheck.Xunit;
using Microsoft.ClearScript;
using Microsoft.ClearScript.JavaScript;
using RG.SSR.JavaScript;
using System.Reflection;

namespace RG.SSR.Tests.Properties;

// Feature: es-module-support, Property 5: Immutable Module Registration
/// <summary>
/// For any specifier string S, if a module is registered with specifier S and source code A,
/// then a subsequent registration with specifier S and source code B SHALL be silently ignored,
/// and importing S SHALL always return source A.
/// </summary>
/// **Validates: Requirements 5.3**
public class ImmutableModuleRegistrationProperties
{
    /// <summary>
    /// Generates valid module specifiers: non-empty strings with max 256 characters.
    /// </summary>
    private static Arbitrary<string> ValidSpecifierArbitrary()
    {
        return Arb.Default.NonEmptyString()
            .Generator
            .Select(nes => nes.Get.Length > 256 ? nes.Get.Substring(0, 256) : nes.Get)
            .Where(s => !string.IsNullOrEmpty(s))
            .ToArbitrary();
    }

    /// <summary>
    /// Generates valid module source code: non-empty strings representing JS source.
    /// </summary>
    private static Arbitrary<string> ValidSourceCodeArbitrary()
    {
        return Arb.Default.NonEmptyString()
            .Generator
            .Select(nes => $"export const value = '{nes.Get.Replace("'", "\\'")}';")
            .Where(s => !string.IsNullOrEmpty(s))
            .ToArbitrary();
    }

    [Property(MaxTest = 100)]
    public Property RegisterModule_FirstRegistrationWins_SubsequentRegistrationIgnored()
    {
        return Prop.ForAll(
            ValidSpecifierArbitrary(),
            ValidSourceCodeArbitrary(),
            ValidSourceCodeArbitrary(),
            (string specifier, string sourceA, string sourceB) =>
            {
                // Ensure sourceA and sourceB are different to make the test meaningful
                if (sourceA == sourceB)
                    sourceB = sourceB + " // different";

                var moduleLoader = new ModuleLoader();

                // Register with specifier S and source A
                moduleLoader.RegisterModule(specifier, sourceA);

                // Attempt to register with same specifier S and source B
                moduleLoader.RegisterModule(specifier, sourceB);

                // Resolve the module via LoadDocumentAsync - it should return source A
                var settings = new Microsoft.ClearScript.DocumentSettings();
                var task = moduleLoader.LoadDocumentAsync(
                    settings,
                    sourceInfo: null!,
                    specifier: specifier,
                    category: Microsoft.ClearScript.JavaScript.ModuleCategory.Standard,
                    contextCallback: null!);

                var document = task.GetAwaiter().GetResult();

                // Read the document contents
                using var reader = new System.IO.StreamReader(document.Contents);
                var resolvedSource = reader.ReadToEnd();

                // The resolved source should be source A (first registration wins)
                return (resolvedSource == sourceA)
                    .Label($"Expected first registered source to be returned. " +
                           $"Specifier: '{specifier}', " +
                           $"Expected: '{sourceA.Substring(0, Math.Min(50, sourceA.Length))}...', " +
                           $"Got: '{resolvedSource.Substring(0, Math.Min(50, resolvedSource.Length))}...'");
            });
    }

    [Property(MaxTest = 100)]
    public Property RegisterModule_MultipleRegistrationsWithSameSpecifier_AlwaysReturnsFirst()
    {
        return Prop.ForAll(
            ValidSpecifierArbitrary(),
            ValidSourceCodeArbitrary(),
            ValidSourceCodeArbitrary(),
            (string specifier, string sourceA, string sourceB) =>
            {
                // Create a third distinct source
                var sourceC = sourceB + " // third registration";

                var moduleLoader = new ModuleLoader();

                // Register the first source
                moduleLoader.RegisterModule(specifier, sourceA);

                // Attempt to register subsequent sources with the same specifier
                moduleLoader.RegisterModule(specifier, sourceB);
                moduleLoader.RegisterModule(specifier, sourceC);

                // Resolve the module - should always return the first registered source
                var settings = new Microsoft.ClearScript.DocumentSettings();
                var task = moduleLoader.LoadDocumentAsync(
                    settings,
                    sourceInfo: null!,
                    specifier: specifier,
                    category: Microsoft.ClearScript.JavaScript.ModuleCategory.Standard,
                    contextCallback: null!);

                var document = task.GetAwaiter().GetResult();

                using var reader = new System.IO.StreamReader(document.Contents);
                var resolvedSource = reader.ReadToEnd();

                return (resolvedSource == sourceA)
                    .Label($"After 3 registrations, expected first source to be returned");
            });
    }
}


// Feature: es-module-support, Property 4: Bare Specifier Resolution to Registered Module
/// <summary>
/// For any non-empty specifier string registered in the module loader and any component that
/// imports that specifier, the ModuleLoader.LoadDocumentAsync SHALL return a document whose
/// contents match the registered source code.
/// </summary>
/// **Validates: Requirements 2.2, 5.2**
public class BareSpecifierResolutionProperties
{
    private static readonly string[] FrameworkSpecifiers = { "preact", "preact/hooks", "react" };

    /// <summary>
    /// Generates valid bare specifiers: non-empty strings that do NOT start with ./ or ../ or /
    /// and do NOT conflict with pre-registered framework modules.
    /// </summary>
    private static Arbitrary<string> BareSpecifierArbitrary()
    {
        return Arb.Default.NonEmptyString()
            .Generator
            .Select(nes => nes.Get.Length > 256 ? nes.Get.Substring(0, 256) : nes.Get)
            .Where(s => !string.IsNullOrEmpty(s))
            .Where(s => !s.StartsWith("./") && !s.StartsWith("../") && !s.StartsWith("/"))
            .Where(s => !FrameworkSpecifiers.Contains(s))
            .ToArbitrary();
    }

    /// <summary>
    /// Generates valid module source code strings.
    /// </summary>
    private static Arbitrary<string> ValidSourceCodeArbitrary()
    {
        return Arb.Default.NonEmptyString()
            .Generator
            .Select(nes => $"export const value = '{nes.Get.Replace("'", "\\'")}';")
            .Where(s => !string.IsNullOrEmpty(s))
            .ToArbitrary();
    }

    [Property(MaxTest = 100)]
    public Property LoadDocumentAsync_BareSpecifier_ReturnsRegisteredModuleSource()
    {
        return Prop.ForAll(
            BareSpecifierArbitrary(),
            ValidSourceCodeArbitrary(),
            (string specifier, string sourceCode) =>
            {
                var moduleLoader = new ModuleLoader();

                // Register the module with the bare specifier
                moduleLoader.RegisterModule(specifier, sourceCode);

                // Resolve the module via LoadDocumentAsync
                var settings = new DocumentSettings();
                var task = moduleLoader.LoadDocumentAsync(
                    settings,
                    sourceInfo: null!,
                    specifier: specifier,
                    category: ModuleCategory.Standard,
                    contextCallback: null!);

                var document = task.GetAwaiter().GetResult();

                // Read the document contents
                using var reader = new System.IO.StreamReader(document.Contents);
                var resolvedSource = reader.ReadToEnd();

                // The resolved source should match the registered source code
                return (resolvedSource == sourceCode)
                    .Label($"Expected registered source to be returned for bare specifier. " +
                           $"Specifier: '{specifier}', " +
                           $"Expected: '{sourceCode.Substring(0, Math.Min(50, sourceCode.Length))}...', " +
                           $"Got: '{resolvedSource.Substring(0, Math.Min(50, resolvedSource.Length))}...'");
            });
    }

    [Property(MaxTest = 100)]
    public Property LoadDocumentAsync_MultipleBareSpecifiers_EachResolvesToOwnSource()
    {
        return Prop.ForAll(
            BareSpecifierArbitrary(),
            ValidSourceCodeArbitrary(),
            ValidSourceCodeArbitrary(),
            (string specifier1, string source1, string source2) =>
            {
                // Use a derived specifier2 that is guaranteed different
                var specifier2 = specifier1 + "_other";
                if (specifier2.Length > 256)
                    specifier2 = specifier2.Substring(0, 256);

                var moduleLoader = new ModuleLoader();

                // Register both modules
                moduleLoader.RegisterModule(specifier1, source1);
                moduleLoader.RegisterModule(specifier2, source2);

                // Resolve both modules
                var settings = new DocumentSettings();

                var task1 = moduleLoader.LoadDocumentAsync(
                    settings,
                    sourceInfo: null!,
                    specifier: specifier1,
                    category: ModuleCategory.Standard,
                    contextCallback: null!);
                var document1 = task1.GetAwaiter().GetResult();

                var task2 = moduleLoader.LoadDocumentAsync(
                    settings,
                    sourceInfo: null!,
                    specifier: specifier2,
                    category: ModuleCategory.Standard,
                    contextCallback: null!);
                var document2 = task2.GetAwaiter().GetResult();

                // Read both document contents
                using var reader1 = new System.IO.StreamReader(document1.Contents);
                var resolved1 = reader1.ReadToEnd();

                using var reader2 = new System.IO.StreamReader(document2.Contents);
                var resolved2 = reader2.ReadToEnd();

                // Each specifier should resolve to its own registered source
                return (resolved1 == source1 && resolved2 == source2)
                    .Label($"Each bare specifier should resolve to its own registered source. " +
                           $"Specifier1 match: {resolved1 == source1}, " +
                           $"Specifier2 match: {resolved2 == source2}");
            });
    }
}


// Feature: es-module-support, Property 6: Module Instance Identity
/// <summary>
/// For any module specifier imported by two or more different modules within the same evaluation,
/// the ModuleLoader SHALL return the same module instance such that exported object references
/// are identical across all importers.
/// </summary>
/// **Validates: Requirements 4.3**
public class ModuleInstanceIdentityProperties : IDisposable
{
    private readonly ModuleLoader _moduleLoader;
    private readonly JavaScriptEngine _engine;

    public ModuleInstanceIdentityProperties()
    {
        _moduleLoader = new ModuleLoader();
        _engine = new JavaScriptEngine(_moduleLoader);
    }

    /// <summary>
    /// Generates valid JavaScript identifier-safe property names (alphanumeric, starting with a letter).
    /// </summary>
    private static Arbitrary<string> PropertyNameArbitrary()
    {
        return Gen.Elements(
            "alpha", "beta", "gamma", "delta", "epsilon", "zeta", "eta", "theta",
            "iota", "kappa", "lambda", "mu", "nu", "xi", "omicron", "pi",
            "rho", "sigma", "tau", "upsilon", "phi", "chi", "psi", "omega",
            "foo", "bar", "baz", "qux", "quux", "corge", "grault", "garply"
        ).ToArbitrary();
    }

    /// <summary>
    /// Generates random integer values to use as object property values.
    /// </summary>
    private static Arbitrary<int> PropertyValueArbitrary()
    {
        return Arb.Default.Int32();
    }

    [Property(MaxTest = 100)]
    public Property TwoModulesImportingSameDependency_ExportedObjectReferencesAreIdentical()
    {
        return Prop.ForAll(
            PropertyNameArbitrary(),
            PropertyValueArbitrary().Generator.ToArbitrary(),
            (string propName, int propValue) =>
            {
                // Create a fresh engine for each test to avoid module caching across iterations
                var moduleLoader = new ModuleLoader();
                using var engine = new JavaScriptEngine(moduleLoader);

                // Use unique specifier names per iteration to avoid V8 module cache conflicts
                var uniqueId = Guid.NewGuid().ToString("N").Substring(0, 8);
                var sharedSpecifier = $"shared_{uniqueId}";
                var modASpecifier = $"modA_{uniqueId}";
                var modBSpecifier = $"modB_{uniqueId}";

                // Shared module exports an object with a random property name and value
                string sharedModule = $"export const sharedObj = {{ {propName}: {propValue} }};";

                // Module A imports the shared module and re-exports the object
                string moduleA = $"import {{ sharedObj }} from '{sharedSpecifier}';\nexport function getFromA() {{ return sharedObj; }}";

                // Module B imports the same shared module and re-exports the object
                string moduleB = $"import {{ sharedObj }} from '{sharedSpecifier}';\nexport function getFromB() {{ return sharedObj; }}";

                // Top-level module imports both A and B, checks object identity with ===
                string topModule = $@"
import {{ getFromA }} from '{modASpecifier}';
import {{ getFromB }} from '{modBSpecifier}';
const objA = getFromA();
const objB = getFromB();
const identical = objA === objB;
identical.toString();
";

                moduleLoader.RegisterModule(sharedSpecifier, sharedModule);
                moduleLoader.RegisterModule(modASpecifier, moduleA);
                moduleLoader.RegisterModule(modBSpecifier, moduleB);

                // Act: Evaluate the top-level module
                string result = engine.RenderModule(topModule, typeof(ModuleLoader).Assembly);

                // Assert: The object references should be identical (=== returns true)
                return (result == "true")
                    .Label($"Expected object identity (===) to be true for shared module. " +
                           $"Property: {propName}={propValue}, Result: {result}");
            });
    }

    [Property(MaxTest = 100)]
    public Property TwoModulesImportingSameDependency_ExportedObjectPropertyValuesMatch()
    {
        return Prop.ForAll(
            PropertyNameArbitrary(),
            PropertyValueArbitrary().Generator.ToArbitrary(),
            (string propName, int propValue) =>
            {
                // Create a fresh engine for each test
                var moduleLoader = new ModuleLoader();
                using var engine = new JavaScriptEngine(moduleLoader);

                var uniqueId = Guid.NewGuid().ToString("N").Substring(0, 8);
                var sharedSpecifier = $"shared_{uniqueId}";
                var modASpecifier = $"modA_{uniqueId}";
                var modBSpecifier = $"modB_{uniqueId}";

                // Shared module exports an object with the random property
                string sharedModule = $"export const sharedObj = {{ {propName}: {propValue} }};";

                // Module A imports and re-exports
                string moduleA = $"import {{ sharedObj }} from '{sharedSpecifier}';\nexport function getFromA() {{ return sharedObj; }}";

                // Module B imports and re-exports
                string moduleB = $"import {{ sharedObj }} from '{sharedSpecifier}';\nexport function getFromB() {{ return sharedObj; }}";

                // Top-level module checks that both modules see the same property value
                string topModule = $@"
import {{ getFromA }} from '{modASpecifier}';
import {{ getFromB }} from '{modBSpecifier}';
const objA = getFromA();
const objB = getFromB();
const sameValue = objA.{propName} === objB.{propName} && objA.{propName} === {propValue};
sameValue.toString();
";

                moduleLoader.RegisterModule(sharedSpecifier, sharedModule);
                moduleLoader.RegisterModule(modASpecifier, moduleA);
                moduleLoader.RegisterModule(modBSpecifier, moduleB);

                // Act
                string result = engine.RenderModule(topModule, typeof(ModuleLoader).Assembly);

                // Assert: Both importers see the same property value from the shared instance
                return (result == "true")
                    .Label($"Expected both importers to see same property value. " +
                           $"Property: {propName}={propValue}, Result: {result}");
            });
    }

    public void Dispose()
    {
        _engine.Dispose();
    }
}
