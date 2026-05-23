# Implementation Plan: ES Module Support

## Overview

This plan implements ES module support for RG.SSR by enhancing the `ModuleLoader` to resolve embedded resources and framework modules, adding a `ModuleSyntaxDetector` to route between script and module evaluation, updating the renderers to handle ES module components (including hydration with `<script type="module">`), and exposing a public API for custom module registration. All changes maintain full backward compatibility with existing plain-script components.

## Tasks

- [x] 1. Create ModuleSyntaxDetector and enhance ModuleLoader with custom module registration
  - [x] 1.1 Create the `ModuleSyntaxDetector` static class
    - Create file `RG.SSR/JavaScript/ModuleSyntaxDetector.cs`
    - Implement `public static bool ContainsModuleSyntax(string source)` that returns `true` if the source contains `import` or `export` as actual JavaScript statements (not inside comments or string literals)
    - Use regex patterns: `import` followed by whitespace, `{`, `"`, or `'`; `export` followed by whitespace then `default`, `function`, `class`, `const`, `let`, `var`, or `{`
    - Strip single-line comments (`//...`), multi-line comments (`/* ... */`), single-quoted strings, double-quoted strings, and template literals before checking
    - _Requirements: 6.1, 6.5_

  - [x] 1.2 Write property test for ModuleSyntaxDetector (Property 2: Module Syntax Detection Accuracy)
    - **Property 2: Module Syntax Detection Accuracy**
    - **Validates: Requirements 6.1, 6.5**
    - Generate random JavaScript sources with `import`/`export` placed inside comments, strings, and as actual statements
    - Verify `ContainsModuleSyntax` returns `true` only for actual statements

  - [x] 1.3 Add `RegisterModule` method and custom module registry to `ModuleLoader`
    - Add `private readonly ConcurrentDictionary<string, Document> _customModules` field
    - Implement `public void RegisterModule(string specifier, string sourceCode)` that validates inputs (non-null, non-empty, specifier max 256 chars), throws `ArgumentException` for invalid inputs, and uses `TryAdd` for immutable first-registration semantics
    - _Requirements: 5.1, 5.3, 5.4_

  - [x] 1.4 Write property test for immutable module registration (Property 5: Immutable Module Registration)
    - **Property 5: Immutable Module Registration**
    - **Validates: Requirements 5.3**
    - Register a module with specifier S and source A, then attempt to register with S and source B
    - Verify importing S always returns source A

  - [x] 1.5 Write unit tests for `RegisterModule` validation
    - Test null specifier throws `ArgumentException`
    - Test empty specifier throws `ArgumentException`
    - Test specifier exceeding 256 characters throws `ArgumentException`
    - Test null source code throws `ArgumentException`
    - Test empty source code throws `ArgumentException`
    - Test duplicate registration is silently ignored
    - _Requirements: 5.1, 5.3, 5.4_

- [x] 2. Register framework modules and enhance ModuleLoader resolution logic
  - [x] 2.1 Pre-register framework modules (`preact`, `preact/hooks`, `react`) in `ModuleLoader` constructor
    - Create JavaScript source strings for each framework module that export mock SSR implementations: `createElement` returns `{tag, props, children}`, `useState` returns `[initialState, noop]`, `useEffect` is a noop, `useReducer` returns `[initialState, noop]`, `useCallback` returns the callback unchanged, `useMemo` invokes and returns the factory result, `useRef` returns `{current: initialValue}`
    - Register each as a `StringDocument` with module category in `_moduleByName` at construction time
    - _Requirements: 3.1, 3.2, 3.3, 3.4_

  - [x] 2.2 Write property test for SSR mock implementations (Property 10: SSR Mock Implementations)
    - **Property 10: SSR Mock Implementations**
    - **Validates: Requirements 3.4**
    - For random initial state values, verify `useState` returns `[initialState, noop]`
    - For random tag/props/children, verify `createElement` returns `{tag, props, children}`
    - For random factory functions, verify `useMemo` returns the factory result
    - For random callbacks, verify `useCallback` returns the callback unchanged

  - [x] 2.3 Enhance `LoadDocumentAsync` to resolve relative specifiers against embedded resources
    - Add `Assembly? _currentComponentAssembly` field with lock for thread safety
    - Add `SetComponentAssembly(Assembly assembly)` and `ClearComponentAssembly()` methods
    - In `LoadDocumentAsync`: check framework/custom modules first, then for relative specifiers (`./` or `../`) extract the filename and call `EmbeddedResourceResolver.ResolveJavaScriptResourceStream` against the current component assembly
    - If found, return a `StringDocument` with module category
    - If not found, delegate to `Default.LoadDocumentAsync`
    - If default also fails, throw `FileNotFoundException` with specifier and assembly name
    - _Requirements: 2.1, 2.2, 2.3, 2.4_

  - [x] 2.4 Write property test for relative specifier resolution priority (Property 3: Relative Specifier Suffix-Matching Resolution Priority)
    - **Property 3: Relative Specifier Suffix-Matching Resolution Priority**
    - **Validates: Requirements 2.1**
    - Generate assemblies with various embedded resource name combinations (`.min.js`, `.js`, exact)
    - Verify resolution follows strict priority: `.min.js` > `.js` > exact name

  - [x] 2.5 Write property test for bare specifier resolution (Property 4: Bare Specifier Resolution to Registered Module)
    - **Property 4: Bare Specifier Resolution to Registered Module**
    - **Validates: Requirements 2.2, 5.2**
    - Register modules with random specifier names and source code
    - Verify `LoadDocumentAsync` returns documents matching the registered source

  - [x] 2.6 Write property test for error reporting (Property 9: Error Reporting for Unresolvable Specifiers)
    - **Property 9: Error Reporting for Unresolvable Specifiers**
    - **Validates: Requirements 2.3, 8.1, 8.4**
    - Attempt to resolve specifiers that don't exist in any source
    - Verify the thrown exception message contains both the specifier and the assembly full name

- [x] 3. Checkpoint - Ensure all tests pass
  - Ensure all tests pass, ask the user if questions arise.

- [x] 4. Enhance JavaScriptEngine and renderers for ES module evaluation
  - [x] 4.1 Add `RenderModule(string moduleCode, Assembly componentAssembly)` overload to `JavaScriptEngine`
    - Call `_moduleLoader.SetComponentAssembly(assembly)` before evaluation
    - Evaluate the module code with `DocumentInfo { Category = ModuleCategory.Standard }` (ClearScript's module category)
    - Call `_moduleLoader.ClearComponentAssembly()` in a `finally` block
    - Return the string result
    - _Requirements: 1.1, 1.2_

  - [x] 4.2 Update `ReactRenderer` to detect module syntax and render ES module components
    - In both `Render` and `Render<TProps>` methods: after loading the component script, call `ModuleSyntaxDetector.ContainsModuleSyntax`
    - If module syntax detected: construct a wrapper module that imports the SSR script (pre-registered) and invokes the component's default or named export, then calls `render()`
    - If no module syntax: use existing plain script evaluation (unchanged behavior)
    - For `isStatic=false` with module syntax: emit `<script type="module">` for hydration instead of `<script defer>`
    - For `isStatic=true`: return only rendered HTML (no script tags)
    - _Requirements: 1.1, 1.2, 1.3, 6.1, 7.1, 7.2, 7.3, 7.4_

  - [x] 4.3 Update `PreactRenderer` to detect module syntax and render ES module components
    - Same changes as ReactRenderer but for Preact: detect module syntax, construct wrapper module, switch hydration script tag type
    - For `isStatic=false` with module syntax: emit `<script type="module">` for hydration
    - For `isStatic=true`: return only rendered HTML
    - _Requirements: 1.1, 1.2, 1.3, 6.1, 7.1, 7.2, 7.3, 7.4_

  - [x] 4.4 Update `ReactRenderer.GetSsrScript()` and `PreactRenderer.GetSsrScript()` to throw `InvalidOperationException` with resource name and assembly name
    - Change the exception from `InvalidProgramException` to `InvalidOperationException`
    - Include the missing resource name (e.g., `"RG.SSR.React.Scripts.ReactSSR.js"`) and the assembly full name in the message
    - _Requirements: 9.1, 9.2, 9.4, 9.5_

  - [x] 4.5 Write property test for hydration script tag type (Property 7: Hydration Script Tag Type)
    - **Property 7: Hydration Script Tag Type**
    - **Validates: Requirements 7.2, 7.3**
    - For components with module syntax rendered with `isStatic=false`, verify output contains `<script type="module">`
    - For components without module syntax rendered with `isStatic=false`, verify output contains `<script defer>`

  - [x] 4.6 Write property test for static rendering (Property 8: Static Rendering Produces No Scripts)
    - **Property 8: Static Rendering Produces No Scripts**
    - **Validates: Requirements 7.4**
    - For any component rendered with `isStatic=true`, verify output contains no `<script` tags and no container `<div>` wrapper

  - [x] 4.7 Write property test for missing export error (Property 11: Missing Export Error)
    - **Property 11: Missing Export Error**
    - **Validates: Requirements 1.3**
    - Evaluate ES modules that have no default export and no named export matching the component name
    - Verify the thrown error message indicates no valid component export was found

  - [x] 4.8 Write property test for missing SSR script error (Property 12: Missing SSR Script Embedded Resource Error)
    - **Property 12: Missing SSR Script Embedded Resource Error**
    - **Validates: Requirements 9.4**
    - Simulate missing SSR script embedded resource
    - Verify `InvalidOperationException` is thrown with the resource name and assembly name in the message

- [x] 5. Checkpoint - Ensure all tests pass
  - Ensure all tests pass, ask the user if questions arise.

- [x] 6. Wire integration: transitive dependencies, module instance identity, and backward compatibility
  - [x] 6.1 Ensure transitive dependency resolution works end-to-end
    - Verify that `LoadDocumentAsync` is called recursively by V8 for nested imports (Component → Dependency → Dependency)
    - Ensure the assembly context remains set for the full module graph evaluation (set once, cleared after top-level evaluation completes)
    - _Requirements: 4.1, 4.2, 4.5_

  - [x] 6.2 Write property test for module instance identity (Property 6: Module Instance Identity)
    - **Property 6: Module Instance Identity**
    - **Validates: Requirements 4.3**
    - Create two modules that import the same dependency
    - Verify the exported object references are identical across both importers

  - [x] 6.3 Write property test for render equivalence (Property 1: Module Evaluation Equivalence)
    - **Property 1: Module Evaluation Equivalence**
    - **Validates: Requirements 1.4, 6.2**
    - For valid component functions, render as plain script and as ES module with `export default`
    - Verify both produce identical HTML output

  - [x] 6.4 Write integration tests for full render pipeline
    - Test 3-level dependency graph resolves and renders correctly
    - Test plain script components still produce identical output to prior behavior
    - Test all option classes retain default values (backward compatibility)
    - _Requirements: 4.2, 6.2, 6.3, 6.4_

- [x] 7. Final checkpoint - Ensure all tests pass
  - Ensure all tests pass, ask the user if questions arise.

- [x] 8. Set up CI pipeline with GitHub Actions
  - [x] 8.1 Create `.github/workflows/ci.yml` workflow file
    - Create the `.github/workflows/` directory structure and `ci.yml` file
    - Configure the workflow to trigger on `push` and `pull_request` events
    - Set up the job to run on `ubuntu-latest`
    - Use `actions/checkout@v4` to check out the repository
    - Use `actions/setup-dotnet@v4` to install the .NET SDK (version matching the project's `net7.0` target framework)
    - Add step to run `dotnet restore` for dependency resolution
    - Add step to run `dotnet build --no-restore` to compile the solution
    - Add step to run `dotnet test --no-build --verbosity normal` to execute all tests
    - _Requirements: N/A (infrastructure)_

## Notes

- Tasks marked with `*` are optional and can be skipped for faster MVP
- Each task references specific requirements for traceability
- Checkpoints ensure incremental validation
- Property tests validate universal correctness properties from the design document
- Unit tests validate specific examples and edge cases
- The design uses C# with ClearScript's V8 engine — all implementation is in C#/.NET
- FsCheck with xUnit (`FsCheck.Xunit`) is the property-based testing library specified in the design
- Circular dependency handling (Requirement 4.5, 8.3) is provided by V8's built-in ES module semantics and requires no custom code

## Task Dependency Graph

```json
{
  "waves": [
    { "id": 0, "tasks": ["1.1", "1.3"] },
    { "id": 1, "tasks": ["1.2", "1.4", "1.5", "2.1"] },
    { "id": 2, "tasks": ["2.2", "2.3"] },
    { "id": 3, "tasks": ["2.4", "2.5", "2.6", "4.1"] },
    { "id": 4, "tasks": ["4.2", "4.3", "4.4"] },
    { "id": 5, "tasks": ["4.5", "4.6", "4.7", "4.8"] },
    { "id": 6, "tasks": ["6.1"] },
    { "id": 7, "tasks": ["6.2", "6.3", "6.4"] },
    { "id": 8, "tasks": ["8.1"] }
  ]
}
```
