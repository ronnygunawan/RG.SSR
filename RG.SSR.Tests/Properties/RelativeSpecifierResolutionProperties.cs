using FsCheck;
using FsCheck.Xunit;
using RG.SSR.EmbeddedResources;
using System.Reflection;

namespace RG.SSR.Tests.Properties;

// Feature: es-module-support, Property 3: Relative Specifier Suffix-Matching Resolution Priority
/// <summary>
/// For any assembly containing embedded resources and a relative module specifier,
/// the EmbeddedResourceResolver SHALL resolve to the resource matching the specifier's
/// filename with .min.js suffix if available, otherwise .js suffix if available,
/// otherwise exact name match — following a strict priority order.
/// </summary>
/// **Validates: Requirements 2.1**
public class RelativeSpecifierResolutionProperties
{
    /// <summary>
    /// Represents which resource variants are present in an assembly for a given base name.
    /// </summary>
    private enum ResourceCombination
    {
        MinJsOnly,
        JsOnly,
        ExactOnly,
        MinJsAndJs,
        MinJsAndExact,
        JsAndExact,
        AllThree
    }

    /// <summary>
    /// A mock Assembly subclass that provides controlled manifest resource names and streams.
    /// This allows property-based testing of the EmbeddedResourceResolver without needing
    /// real compiled assemblies with embedded resources.
    /// </summary>
    private class MockAssembly : Assembly
    {
        private readonly Dictionary<string, byte[]> _resources;

        public MockAssembly(Dictionary<string, byte[]> resources)
        {
            _resources = resources;
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

        public override string FullName => "MockAssembly, Version=1.0.0.0";
    }

    /// <summary>
    /// Generates valid base resource names (simple identifiers without dots or special chars).
    /// </summary>
    private static Arbitrary<string> BaseNameArbitrary()
    {
        var gen = Gen.OneOf(
            // Simple lowercase names
            Gen.Elements(
                "utils", "helpers", "component", "module", "shared",
                "formatting", "constants", "api", "service", "store"
            ),
            // PascalCase names
            Gen.Elements(
                "Button", "Header", "Footer", "Card", "Layout",
                "MyComponent", "DataService", "EventHandler", "Logger", "Config"
            ),
            // camelCase names
            Gen.Elements(
                "myModule", "appConfig", "dataHelper", "eventBus", "stateManager"
            )
        );
        return gen.ToArbitrary();
    }

    /// <summary>
    /// Generates a namespace prefix for the embedded resource (simulates assembly namespace).
    /// </summary>
    private static Arbitrary<string> NamespacePrefixArbitrary()
    {
        var gen = Gen.Elements(
            "MyApp.Views.Home",
            "MyApp.Components.Shared",
            "TestProject.Scripts",
            "App.Modules",
            "RG.Test.Resources",
            "Company.Product.Views",
            "WebApp.Client.Components"
        );
        return gen.ToArbitrary();
    }

    /// <summary>
    /// Generates all possible resource combination scenarios.
    /// </summary>
    private static Arbitrary<ResourceCombination> CombinationArbitrary()
    {
        return Gen.Elements(
            ResourceCombination.MinJsOnly,
            ResourceCombination.JsOnly,
            ResourceCombination.ExactOnly,
            ResourceCombination.MinJsAndJs,
            ResourceCombination.MinJsAndExact,
            ResourceCombination.JsAndExact,
            ResourceCombination.AllThree
        ).ToArbitrary();
    }

    /// <summary>
    /// Creates a MockAssembly with specific embedded resource names based on the combination.
    /// Each resource contains a marker string identifying which variant it is.
    /// </summary>
    private static MockAssembly CreateMockAssembly(string namespacePrefix, string baseName, ResourceCombination combination)
    {
        var resources = new Dictionary<string, byte[]>();

        if (combination is ResourceCombination.MinJsOnly or ResourceCombination.MinJsAndJs
            or ResourceCombination.MinJsAndExact or ResourceCombination.AllThree)
        {
            var name = $"{namespacePrefix}.{baseName}.min.js";
            resources[name] = System.Text.Encoding.UTF8.GetBytes($"// minified: {name}");
        }

        if (combination is ResourceCombination.JsOnly or ResourceCombination.MinJsAndJs
            or ResourceCombination.JsAndExact or ResourceCombination.AllThree)
        {
            var name = $"{namespacePrefix}.{baseName}.js";
            resources[name] = System.Text.Encoding.UTF8.GetBytes($"// js: {name}");
        }

        if (combination is ResourceCombination.ExactOnly or ResourceCombination.MinJsAndExact
            or ResourceCombination.JsAndExact or ResourceCombination.AllThree)
        {
            var name = $"{namespacePrefix}.{baseName}";
            resources[name] = System.Text.Encoding.UTF8.GetBytes($"// exact: {name}");
        }

        return new MockAssembly(resources);
    }

    /// <summary>
    /// Determines which resource name should be resolved based on the priority rules.
    /// Priority: .min.js > .js > exact name
    /// </summary>
    private static string GetExpectedResolvedResourceName(string namespacePrefix, string baseName, ResourceCombination combination)
    {
        return combination switch
        {
            ResourceCombination.MinJsOnly => $"{namespacePrefix}.{baseName}.min.js",
            ResourceCombination.JsOnly => $"{namespacePrefix}.{baseName}.js",
            ResourceCombination.ExactOnly => $"{namespacePrefix}.{baseName}",
            ResourceCombination.MinJsAndJs => $"{namespacePrefix}.{baseName}.min.js",
            ResourceCombination.MinJsAndExact => $"{namespacePrefix}.{baseName}.min.js",
            ResourceCombination.JsAndExact => $"{namespacePrefix}.{baseName}.js",
            ResourceCombination.AllThree => $"{namespacePrefix}.{baseName}.min.js",
            _ => throw new ArgumentOutOfRangeException()
        };
    }

    /// <summary>
    /// Gets the expected content prefix for the resolved resource based on priority.
    /// </summary>
    private static string GetExpectedContentPrefix(ResourceCombination combination)
    {
        return combination switch
        {
            ResourceCombination.MinJsOnly => "// minified:",
            ResourceCombination.JsOnly => "// js:",
            ResourceCombination.ExactOnly => "// exact:",
            ResourceCombination.MinJsAndJs => "// minified:",
            ResourceCombination.MinJsAndExact => "// minified:",
            ResourceCombination.JsAndExact => "// js:",
            ResourceCombination.AllThree => "// minified:",
            _ => throw new ArgumentOutOfRangeException()
        };
    }

    /// <summary>
    /// Property: Resolution follows strict priority order across all resource combinations.
    /// When .min.js exists, it is always preferred. When only .js exists (no .min.js), .js is preferred.
    /// When only exact name exists, it is returned.
    /// </summary>
    [Property(MaxTest = 100)]
    public Property Resolution_FollowsStrictPriority_MinJs_Over_Js_Over_Exact()
    {
        return Prop.ForAll(
            BaseNameArbitrary(),
            NamespacePrefixArbitrary(),
            CombinationArbitrary(),
            (string baseName, string namespacePrefix, ResourceCombination combination) =>
            {
                var assembly = CreateMockAssembly(namespacePrefix, baseName, combination);

                // Call the resolver with the base name
                Stream? result = EmbeddedResourceResolver.ResolveJavaScriptResourceStream(assembly, baseName);

                if (result == null)
                {
                    return false.Label($"Expected a resource to be resolved for baseName='{baseName}' " +
                                       $"with combination={combination}, but got null");
                }

                // Read the content to determine which resource was resolved
                using var reader = new StreamReader(result);
                string content = reader.ReadToEnd();

                // Verify the resolved resource matches the expected priority
                string expectedResourceName = GetExpectedResolvedResourceName(namespacePrefix, baseName, combination);
                string expectedPrefix = GetExpectedContentPrefix(combination);

                return content.StartsWith(expectedPrefix)
                    .Label($"Resolution priority violated. " +
                           $"BaseName='{baseName}', Combination={combination}, " +
                           $"Expected resource: '{expectedResourceName}', " +
                           $"Expected content prefix: '{expectedPrefix}', " +
                           $"Actual content: '{content}'");
            });
    }

    /// <summary>
    /// Property: When all three variants exist (.min.js, .js, exact), .min.js is always chosen
    /// regardless of the base name or namespace prefix.
    /// </summary>
    [Property(MaxTest = 100)]
    public Property Resolution_WithAllVariants_AlwaysChoosesMinJs()
    {
        return Prop.ForAll(
            BaseNameArbitrary(),
            NamespacePrefixArbitrary(),
            (string baseName, string namespacePrefix) =>
            {
                var assembly = CreateMockAssembly(namespacePrefix, baseName, ResourceCombination.AllThree);

                Stream? result = EmbeddedResourceResolver.ResolveJavaScriptResourceStream(assembly, baseName);

                if (result == null)
                {
                    return false.Label($"Expected .min.js resource for baseName='{baseName}', got null");
                }

                using var reader = new StreamReader(result);
                string content = reader.ReadToEnd();

                string expectedResourceName = $"{namespacePrefix}.{baseName}.min.js";

                return content.Contains(expectedResourceName)
                    .Label($"With all three variants, .min.js should be chosen. " +
                           $"BaseName='{baseName}', " +
                           $"Expected resource name in content: '{expectedResourceName}', " +
                           $"Actual content: '{content}'");
            });
    }

    /// <summary>
    /// Property: When .min.js is absent but .js and exact exist, .js is chosen over exact.
    /// </summary>
    [Property(MaxTest = 100)]
    public Property Resolution_WithoutMinJs_ChoosesJsOverExact()
    {
        return Prop.ForAll(
            BaseNameArbitrary(),
            NamespacePrefixArbitrary(),
            (string baseName, string namespacePrefix) =>
            {
                var assembly = CreateMockAssembly(namespacePrefix, baseName, ResourceCombination.JsAndExact);

                Stream? result = EmbeddedResourceResolver.ResolveJavaScriptResourceStream(assembly, baseName);

                if (result == null)
                {
                    return false.Label($"Expected .js resource for baseName='{baseName}', got null");
                }

                using var reader = new StreamReader(result);
                string content = reader.ReadToEnd();

                string expectedResourceName = $"{namespacePrefix}.{baseName}.js";

                return content.Contains(expectedResourceName)
                    .Label($"Without .min.js, .js should be chosen over exact. " +
                           $"BaseName='{baseName}', " +
                           $"Expected resource name in content: '{expectedResourceName}', " +
                           $"Actual content: '{content}'");
            });
    }

    /// <summary>
    /// Property: When only exact name exists (no .min.js or .js), exact name is returned.
    /// </summary>
    [Property(MaxTest = 100)]
    public Property Resolution_WithOnlyExactName_ReturnsExact()
    {
        return Prop.ForAll(
            BaseNameArbitrary(),
            NamespacePrefixArbitrary(),
            (string baseName, string namespacePrefix) =>
            {
                var assembly = CreateMockAssembly(namespacePrefix, baseName, ResourceCombination.ExactOnly);

                Stream? result = EmbeddedResourceResolver.ResolveJavaScriptResourceStream(assembly, baseName);

                if (result == null)
                {
                    return false.Label($"Expected exact resource for baseName='{baseName}', got null");
                }

                using var reader = new StreamReader(result);
                string content = reader.ReadToEnd();

                string expectedResourceName = $"{namespacePrefix}.{baseName}";

                return content.Contains(expectedResourceName)
                    .Label($"With only exact name, exact should be returned. " +
                           $"BaseName='{baseName}', " +
                           $"Expected resource name in content: '{expectedResourceName}', " +
                           $"Actual content: '{content}'");
            });
    }

    /// <summary>
    /// Property: Resolution returns null when no matching resource exists in the assembly.
    /// </summary>
    [Property(MaxTest = 100)]
    public Property Resolution_WithNoMatchingResource_ReturnsNull()
    {
        return Prop.ForAll(
            BaseNameArbitrary(),
            NamespacePrefixArbitrary(),
            (string baseName, string namespacePrefix) =>
            {
                // Create an assembly with resources that DON'T match the baseName
                var differentName = baseName + "Different";
                var resources = new Dictionary<string, byte[]>
                {
                    [$"{namespacePrefix}.{differentName}.min.js"] =
                        System.Text.Encoding.UTF8.GetBytes("// unrelated"),
                    [$"{namespacePrefix}.{differentName}.js"] =
                        System.Text.Encoding.UTF8.GetBytes("// unrelated"),
                };
                var assembly = new MockAssembly(resources);

                // Try to resolve the original baseName - should return null
                Stream? result = EmbeddedResourceResolver.ResolveJavaScriptResourceStream(assembly, baseName);

                return (result == null)
                    .Label($"Expected null when no matching resource exists. " +
                           $"BaseName='{baseName}', available resources: [{string.Join(", ", resources.Keys)}]");
            });
    }
}
