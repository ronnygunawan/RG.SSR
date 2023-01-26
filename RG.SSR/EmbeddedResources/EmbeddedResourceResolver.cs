using System.Reflection;

namespace RG.SSR.EmbeddedResources
{
    internal static class EmbeddedResourceResolver
    {
        public static Stream? ResolveJavaScriptResourceStream(Assembly assembly, string resourceName)
        {
            string[] resourceNames = assembly.GetManifestResourceNames();
            if (resourceNames.FirstOrDefault(name => name.EndsWith($".{resourceName}.min.js", StringComparison.OrdinalIgnoreCase)) is { } minifiedResourceName)
            {
                return assembly.GetManifestResourceStream(minifiedResourceName);
            }
            else if (resourceNames.FirstOrDefault(name => name.EndsWith($".{resourceName}.js", StringComparison.OrdinalIgnoreCase)) is { } resourceStreamName)
            {
                return assembly.GetManifestResourceStream(resourceStreamName);
            }
            else if (resourceNames.FirstOrDefault(name => name.EndsWith($".{resourceName}", StringComparison.OrdinalIgnoreCase)) is { } exactStreamName)
            {
                return assembly.GetManifestResourceStream(exactStreamName);
            }
            else
            {
                return null;
            }
        }
    }
}
