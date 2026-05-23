using Microsoft.ClearScript;
using Microsoft.ClearScript.JavaScript;
using RG.SSR.EmbeddedResources;
using System.Collections.Concurrent;
using System.Reflection;

namespace RG.SSR.JavaScript
{
    internal sealed class ModuleLoader : DocumentLoader
    {
        private readonly ConcurrentDictionary<string, Document> _moduleByName = new();
        private readonly ConcurrentDictionary<string, Document> _customModules = new();
        private Assembly? _currentComponentAssembly;
        private readonly object _assemblyContextLock = new();

        private const string PreactModuleSource = @"
const noop = () => {};
export function createElement(tag, props, ...children) { return { tag, props, children }; }
export function useState(initialState) { return [initialState, noop]; }
export function useEffect() {}
export function useContext() { return undefined; }
export function useReducer(reducer, initialState) { return [initialState, noop]; }
export function useCallback(callback) { return callback; }
export function useMemo(factory) { return factory(); }
export function useRef(initialValue) { return { current: initialValue }; }
";

        private const string PreactHooksModuleSource = @"
const noop = () => {};
export function useState(initialState) { return [initialState, noop]; }
export function useEffect() {}
export function useReducer(reducer, initialState) { return [initialState, noop]; }
export function useCallback(callback) { return callback; }
export function useMemo(factory) { return factory(); }
export function useRef(initialValue) { return { current: initialValue }; }
export function useContext() { return undefined; }
";

        private const string ReactModuleSource = @"
const noop = () => {};
export function createElement(tag, props, ...children) { return { tag, props, children }; }
export function useState(initialState) { return [initialState, noop]; }
export function useEffect() {}
export function useContext() { return undefined; }
export function useReducer(reducer, initialState) { return [initialState, noop]; }
export function useCallback(callback) { return callback; }
export function useMemo(factory) { return factory(); }
export function useRef(initialValue) { return { current: initialValue }; }
";

        public ModuleLoader()
        {
            RegisterFrameworkModule("preact", PreactModuleSource);
            RegisterFrameworkModule("preact/hooks", PreactHooksModuleSource);
            RegisterFrameworkModule("react", ReactModuleSource);
        }

        private void RegisterFrameworkModule(string specifier, string source)
        {
            _moduleByName.TryAdd(
                key: specifier,
                value: new StringDocument(
                    info: new DocumentInfo(specifier)
                    {
                        Category = ModuleCategory.Standard
                    },
                    contents: source
                )
            );
        }

        public string GetOrAddModule(string name, Func<(string Code, DocumentCategory Category)> valueFactory)
        {
            Document document = _moduleByName.GetOrAdd(
                key: name,
                valueFactory: _ =>
                {
                    (string code, DocumentCategory category) = valueFactory.Invoke();

                    return new StringDocument(
                        info: new DocumentInfo(name)
                        {
                            Category = category
                        },
                        contents: code
                    );
                }
            );

            using StreamReader reader = new(document.Contents);
            return reader.ReadToEnd();
        }

        public void RegisterModule(string specifier, string sourceCode)
        {
            if (string.IsNullOrEmpty(specifier))
            {
                throw new ArgumentException("Value cannot be null or empty.", nameof(specifier));
            }

            if (specifier.Length > 256)
            {
                throw new ArgumentException("Specifier must not exceed 256 characters.", nameof(specifier));
            }

            if (string.IsNullOrEmpty(sourceCode))
            {
                throw new ArgumentException("Value cannot be null or empty.", nameof(sourceCode));
            }

            _customModules.TryAdd(
                key: specifier,
                value: new StringDocument(
                    info: new DocumentInfo(specifier)
                    {
                        Category = ModuleCategory.Standard
                    },
                    contents: sourceCode
                )
            );
        }

        public void SetComponentAssembly(Assembly assembly)
        {
            lock (_assemblyContextLock)
            {
                _currentComponentAssembly = assembly;
            }
        }

        public void ClearComponentAssembly()
        {
            lock (_assemblyContextLock)
            {
                _currentComponentAssembly = null;
            }
        }

        public override async Task<Document> LoadDocumentAsync(
            DocumentSettings settings,
            DocumentInfo? sourceInfo,
            string specifier,
            DocumentCategory category,
            DocumentContextCallback contextCallback
        )
        {
            // 1. Check framework modules
            if (_moduleByName.TryGetValue(
                key: specifier,
                out Document? module
            ))
            {
                return module;
            }

            // 2. Check custom registered modules
            if (_customModules.TryGetValue(
                key: specifier,
                out Document? customModule
            ))
            {
                return customModule;
            }

            // 3. For relative specifiers, resolve against embedded resources
            if (specifier.StartsWith("./") || specifier.StartsWith("../"))
            {
                Assembly? assembly;
                lock (_assemblyContextLock)
                {
                    assembly = _currentComponentAssembly;
                }

                if (assembly != null)
                {
                    string filename = Path.GetFileName(specifier);
                    // Strip .js extension for resource resolution if present
                    string resourceName = filename.EndsWith(".js", StringComparison.OrdinalIgnoreCase)
                        ? filename[..^3]
                        : filename;

                    Stream? resourceStream = EmbeddedResourceResolver.ResolveJavaScriptResourceStream(assembly, resourceName);
                    if (resourceStream != null)
                    {
                        using (resourceStream)
                        using (StreamReader reader = new(resourceStream))
                        {
                            string contents = await reader.ReadToEndAsync();
                            return new StringDocument(
                                info: new DocumentInfo(specifier)
                                {
                                    Category = ModuleCategory.Standard
                                },
                                contents: contents
                            );
                        }
                    }
                }
            }

            // 4. Delegate to ClearScript default loader
            try
            {
                return await Default.LoadDocumentAsync(
                    settings,
                    sourceInfo,
                    specifier,
                    category,
                    contextCallback
                );
            }
            catch
            {
                // 5. If default also fails, throw FileNotFoundException
                Assembly? assembly;
                lock (_assemblyContextLock)
                {
                    assembly = _currentComponentAssembly;
                }

                string assemblyName = assembly?.FullName ?? "unknown";
                throw new FileNotFoundException(
                    $"Could not resolve module specifier '{specifier}' in assembly '{assemblyName}'."
                );
            }
        }
    }
}
