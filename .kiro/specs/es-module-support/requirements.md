# Requirements Document

## Introduction

This document specifies the requirements for adding ES module (ESM) support to the RG.SSR library. Currently, RG.SSR renders React and Preact components server-side using ClearScript's V8 engine, but only supports single-file scripts evaluated as plain JavaScript. This feature enables components to use ES module `import`/`export` syntax, allowing components to depend on shared modules, utility functions, and other components.

## Glossary

- **Module_Loader**: The ClearScript DocumentLoader subclass responsible for resolving and loading ES modules by specifier name
- **JavaScript_Engine**: The ClearScript V8ScriptEngine wrapper that executes JavaScript for server-side rendering
- **Component_Module**: A JavaScript ES module file that exports a React or Preact component function as its default export
- **Dependency_Module**: A JavaScript ES module that is imported by a Component_Module, providing shared utilities, sub-components, or helper functions
- **Module_Specifier**: The string identifier used in an `import` statement to reference another module (e.g., `"./utils.js"`, `"shared/Button"`)
- **SSR_Script**: The internal JavaScript that mocks React/Preact APIs and provides the `render()` function for converting virtual DOM to HTML
- **Renderer**: The C# class (ReactRenderer or PreactRenderer) that orchestrates script assembly and invocation of the JavaScript_Engine
- **Embedded_Resource**: A JavaScript file compiled into a .NET assembly as a manifest resource, resolved by resource name

## Non-Goals

- **Importing from node_modules**: This feature does not support resolving bare module specifiers by traversing a `node_modules` directory on the filesystem. All importable modules must be either embedded resources in a .NET assembly, registered programmatically via the Module_Loader API, or pre-registered framework modules (preact, react). Node.js-style module resolution is out of scope.

## Requirements

### Requirement 1: Evaluate Component as ES Module

**User Story:** As a developer, I want my component files to use ES module `export` syntax, so that I can structure components using standard JavaScript module conventions.

#### Acceptance Criteria

1. WHEN a Component_Module uses `export default` syntax, THE JavaScript_Engine SHALL evaluate the file as an ES module and invoke the default export as the component function for rendering
2. WHEN a Component_Module uses a named `export` syntax, THE JavaScript_Engine SHALL evaluate the file as an ES module and invoke the named export matching the component name as the component function for rendering
3. IF a Component_Module does not contain a default export or a named export matching the component name, THEN THE JavaScript_Engine SHALL throw an error indicating that no valid component export was found
4. THE Renderer SHALL produce identical HTML output whether a component is defined as a plain script evaluated via script execution or as an ES module evaluated via module loading, given the same component function and props

### Requirement 2: Resolve Import Specifiers

**User Story:** As a developer, I want to use `import` statements in my component files, so that I can split code across multiple modules.

#### Acceptance Criteria

1. WHEN a Component_Module contains an `import` statement with a relative Module_Specifier (starting with `./` or `../`), THE Module_Loader SHALL resolve the specifier to the corresponding Embedded_Resource in the component assembly by matching the specifier's filename against resource names using suffix matching, preferring `.min.js` over `.js` over an exact name match
2. WHEN a Component_Module contains an `import` statement with a bare Module_Specifier (not starting with `./`, `../`, or `/`), THE Module_Loader SHALL resolve the specifier to a module registered under that exact name in the Module_Loader cache
3. IF a Module_Specifier cannot be resolved to either a cached module or an Embedded_Resource, THEN THE Module_Loader SHALL throw an error indicating the unresolved specifier string and the name of the requesting module that contained the import statement
4. IF a relative Module_Specifier does not match any Embedded_Resource in the component assembly, THEN THE Module_Loader SHALL delegate resolution to the ClearScript default document loader before raising an error

### Requirement 3: Register Framework Modules

**User Story:** As a developer, I want to import `preact`, `preact/hooks`, or `react` by name in my component modules, so that I can use standard import patterns for framework APIs.

#### Acceptance Criteria

1. WHEN a Component_Module imports the specifier `"preact"`, THE Module_Loader SHALL return a module that exports `createElement`, `useState`, `useEffect`, `useContext`, `useReducer`, `useCallback`, `useMemo`, and `useRef` as named exports
2. WHEN a Component_Module imports the specifier `"preact/hooks"`, THE Module_Loader SHALL return a module that exports `useState`, `useEffect`, `useReducer`, `useCallback`, `useMemo`, `useRef`, and `useContext` as named exports
3. WHEN a Component_Module imports the specifier `"react"`, THE Module_Loader SHALL return a module that exports `createElement`, `useState`, `useEffect`, `useContext`, `useReducer`, `useCallback`, `useMemo`, and `useRef` as named exports
4. WHILE the Module_Loader is executing in the SSR context, THE Module_Loader SHALL provide mock implementations where `createElement` returns a virtual DOM object, `useState` returns a tuple of the initial state value and a no-op function, `useEffect` performs no operation, `useReducer` returns a tuple of the initial state and a no-op function, `useCallback` returns the callback argument unchanged, `useMemo` invokes and returns the result of the factory argument, and `useRef` returns an object with a `current` property set to the initial value
5. IF a Component_Module imports a specifier that is not registered as a framework module, THEN THE Module_Loader SHALL delegate resolution to the default ClearScript document loader

### Requirement 4: Support Transitive Dependencies

**User Story:** As a developer, I want my imported modules to also use `import` statements, so that I can build a dependency tree of shared modules.

#### Acceptance Criteria

1. WHEN a Dependency_Module itself contains `import` statements, THE Module_Loader SHALL recursively resolve those imports using the same resolution rules applied to top-level imports in the Component_Module
2. THE JavaScript_Engine SHALL evaluate a module dependency graph of at least 3 levels deep (Component_Module → Dependency_Module → Dependency_Module) and produce the same rendered output as if all code were inlined in a single module
3. WHEN two or more modules import the same Dependency_Module by the same specifier, THE Module_Loader SHALL return the same module instance such that object references exported from that module are identical across all importers
4. IF the Module_Loader cannot resolve a transitive import specifier to a registered or loadable module, THEN THE Module_Loader SHALL throw an exception indicating the unresolved specifier name and the requesting module's identifier
5. IF a module dependency graph contains a circular reference (Module A imports Module B which imports Module A), THEN THE JavaScript_Engine SHALL resolve the cycle according to ES module specification semantics without entering an infinite loop

### Requirement 5: Register Custom Modules Programmatically

**User Story:** As a library consumer, I want to register custom modules by name so that my components can import shared utilities without relative paths.

#### Acceptance Criteria

1. THE Module_Loader SHALL expose a method that accepts a module specifier name (non-empty string, maximum 256 characters) and source code (non-empty string) and registers the source as an ES module associated with that specifier
2. WHEN a registered module specifier is used in an `import` statement, THE Module_Loader SHALL resolve the import to the registered module source code and load it as an ES module
3. WHEN a module is registered with the same specifier name as an existing registration, THE Module_Loader SHALL silently ignore the subsequent registration and retain the first registered version (immutable registration)
4. IF the specifier name is null or empty, or the source code is null or empty, THEN THE Module_Loader SHALL throw an argument exception indicating which parameter is invalid

### Requirement 6: Backward Compatibility with Plain Scripts

**User Story:** As an existing user of RG.SSR, I want my current single-file components (without import/export) to continue working unchanged, so that I can adopt ES modules incrementally.

#### Acceptance Criteria

1. WHEN a component file contains no `import` or `export` statements, THE Renderer SHALL evaluate the component using `engine.Evaluate()` without a module document category, making the component function available as a global identifier callable by `componentName()`
2. WHEN a plain script component is rendered with the same `componentAssembly`, `componentName`, `props`, and `isStatic` arguments as before the ES module feature was added, THE Renderer SHALL produce identical HTML output to the prior behavior
3. THE existing `Render` method signatures on IReactRenderer and IPreactRenderer SHALL remain unchanged: `string Render(Assembly componentAssembly, string componentName, bool isStatic)` and `string Render<TProps>(Assembly componentAssembly, string componentName, TProps props, bool isStatic)`
4. THE existing `ServerSideRendererOptions`, `ReactOptions`, and `PreactOptions` configuration classes SHALL retain all current properties with their current types and default values, requiring no changes to existing consumer configuration code
5. IF a component file contains the text `import` or `export` only within comments or string literals and not as actual JavaScript statements, THEN THE Renderer SHALL treat the file as a plain script

### Requirement 7: Client-Side Hydration with ES Modules

**User Story:** As a developer, I want components rendered with ES modules to still support client-side hydration, so that interactive components work after the initial server render.

#### Acceptance Criteria

1. WHEN a Component_Module is rendered with `isStatic` set to false, THE Renderer SHALL emit a container `<div>` with a unique ID containing the server-rendered HTML, followed by a client-side hydration `<script>` tag that invokes the framework's hydrate function targeting that container
2. IF the Component_Module contains ES module syntax (i.e., `import` or `export` statements), THEN THE Renderer SHALL emit the hydration script within a `<script type="module">` tag
3. IF the Component_Module does not contain ES module syntax, THEN THE Renderer SHALL emit the hydration script within a standard `<script defer>` tag
4. WHEN a Component_Module is rendered with `isStatic` set to true, THE Renderer SHALL return only the server-rendered HTML without emitting any hydration script or container element

### Requirement 8: Error Reporting for Module Resolution Failures

**User Story:** As a developer, I want clear error messages when module resolution fails, so that I can quickly diagnose missing or misconfigured imports.

#### Acceptance Criteria

1. IF a Module_Specifier references a file that does not exist as an Embedded_Resource, THEN THE Module_Loader SHALL throw a FileNotFoundException with a message containing the Module_Specifier value and the full name of the assembly that was searched
2. IF a Component_Module has a syntax error in an `import` statement, THEN THE JavaScript_Engine SHALL throw an exception with the 1-based line number where the error occurred and a description of the syntax violation
3. IF a circular dependency is detected between modules, THEN THE JavaScript_Engine SHALL resolve the circular reference according to ES module specification semantics, making already-evaluated exports available and leaving not-yet-evaluated exports as undefined, without throwing an exception
4. IF the Module_Loader fails to resolve a Module_Specifier that is not registered as an Embedded_Resource and the fallback document loader also cannot resolve it, THEN THE Module_Loader SHALL throw a FileNotFoundException with a message containing the unresolved Module_Specifier and the full name of the searched assembly

### Requirement 9: All Scripts as Embedded Resources

**User Story:** As a library maintainer, I want all internal JavaScript scripts to be loaded exclusively from embedded resources in the assembly, so that the library is self-contained and does not depend on filesystem paths or inline string literals for script content.

#### Acceptance Criteria

1. THE Renderer SHALL load the SSR_Script (ReactSSR.js for React, PreactSSR.js for Preact) from an Embedded_Resource in the RG.SSR assembly rather than from the filesystem or from inline string literals in C# source code
2. WHEN the JavaScript_Engine requires any internal JavaScript to execute server-side rendering, THE Renderer SHALL obtain that JavaScript exclusively by reading Embedded_Resource streams from the assembly
3. THE RG.SSR assembly SHALL include all internal JavaScript files (ReactSSR.js, PreactSSR.js, and any future internal scripts) as embedded resources declared in the project file
4. IF an expected internal SSR_Script Embedded_Resource cannot be found in the assembly at runtime, THEN THE Renderer SHALL throw an InvalidOperationException with a message identifying the missing resource name and the assembly that was searched
5. THE Renderer SHALL NOT read internal JavaScript from the filesystem using file I/O operations (e.g., File.ReadAllText, StreamReader on a file path) or construct JavaScript source from inline string concatenation in C# code
