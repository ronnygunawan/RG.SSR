# RG.SSR

> ⚠️ **Experimental project.** The core rendering pipeline is functional and tested, but the API surface, configuration options, and hydration behaviour are still evolving. Do not use in production without thorough evaluation.

Server-side rendering for React and Preact components in ASP.NET Core, powered by [ClearScript V8](https://github.com/microsoft/ClearScript). **No Node.js required.**

Components are compiled into your .NET assembly as embedded resources and evaluated entirely within the .NET process to produce HTML.

## Why RG.SSR instead of a Node-based SSR setup?

| | RG.SSR | Node.js SSR (e.g. Next.js) |
|---|---|---|
| Runtime dependency | None — V8 is embedded via ClearScript | Node.js must be installed and running |
| Deployment | Single self-contained .NET binary | Separate Node process or sidecar |
| Interop | Direct in-process call | HTTP/IPC round-trip |
| Ecosystem maturity | Experimental | Production-grade |

RG.SSR is a good fit when you want React or Preact server-rendering inside an existing ASP.NET Core application without introducing a Node.js runtime dependency. It is **not** a replacement for a full Next.js / Remix setup if you need features like file-system routing, streaming SSR, or a mature ecosystem.

## Features

- React & Preact support with the same API
- ES module support — `import`/`export` with transitive dependency resolution
- All JavaScript lives as embedded resources in the assembly
- Auto-detection of module syntax — plain scripts and ES modules coexist
- Static islands (server-rendered HTML only) or hydrated interactive components
- Custom module registration for shared utilities

## Install

> **Note:** The NuGet package has not yet been published. Use a project reference until it is available:

```xml
<ItemGroup>
    <ProjectReference Include="path/to/RG.SSR/RG.SSR.csproj" />
</ItemGroup>
```

Once the package is on NuGet the command will be:

```bash
dotnet add package RG.SSR
```

## Getting started in ASP.NET Core

### 1. Register services

```csharp
// Program.cs
using RG.SSR;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddControllersWithViews();
builder.Services.AddServerSideRendering(options =>
{
    // React — set to false if you want to serve react/react-dom from a CDN instead
    options.React.InlineLibrary = true;

    // Preact — uncomment when using the Preact sample
    // options.Preact.InlineLibrary = true;
    // options.Preact.ReactCompat = false;
});

var app = builder.Build();
// ... rest of pipeline ...
app.Run();
```

### 2. Write a component

Components can be written in plain JavaScript or **JSX**. When using JSX, add [Web Compiler 2022](https://marketplace.visualstudio.com/items?itemName=MadsKristensen.WebCompiler2022) to the project and configure it to compile `.jsx` → `.js` via `compilerconfig.json`. The compiled `.js` files are what get embedded as assembly resources.

**Plain script (global variable pattern) — JSX:**

```jsx
// Views/Home/Index.jsx  →  compiled to Index.js by Web Compiler 2022
const Index = () => {
    return <h1>Welcome</h1>;
};
```

**Plain script — without JSX:**

```javascript
// Views/Home/Index.min.js
var Index = function() {
    return React.createElement("h1", null, "Welcome");
};
```

**ES module — JSX:**

```jsx
// Views/Home/Greeting.jsx  →  compiled to Greeting.js by Web Compiler 2022
import { createElement } from 'react';
import { formatGreeting } from './Shared/formatting.js';

export default function Greeting({ name }) {
    return <h1>{formatGreeting(name)}</h1>;
}
```

**ES module — without JSX:**

```javascript
// Views/Home/Greeting.js
import { createElement } from 'react';
import { formatGreeting } from './Shared/formatting.js';

export default function Greeting(props) {
    return createElement('h1', null, formatGreeting(props.name));
}
```

### 3. Embed the files as assembly resources

```xml
<!-- MyApp.csproj -->
<ItemGroup>
    <!-- Plain scripts (minified) -->
    <EmbeddedResource Include="Views/**/*.min.js" />
    <!-- ES modules -->
    <EmbeddedResource Include="Views/**/Greeting.js" />
    <EmbeddedResource Include="Views/**/Shared/*.js" />
</ItemGroup>
```

### 4. Inject and call the renderer in a Razor view

```razor
@using RG.SSR
@inject ServerSideRenderer SSR

@* Static island — server-rendered HTML only, no client-side JavaScript *@
@SSR.RenderReact("Index", isStatic: true)

@* Hydrated component — server-rendered HTML + hydration script for interactivity *@
@SSR.RenderReact("Counter")

@* With props *@
@SSR.RenderReact(
    componentName: "Greeting",
    props: new { Name = "World" },
    isStatic: true
)
```

For Preact, use `SSR.RenderPreact(...)` with the same signature.

## React and Preact

Both frameworks are supported through the same `ServerSideRenderer` API. The renderer inlines the framework library into the page by default (`InlineLibrary = true`). The framework used is determined by which render method you call:

| Method | Framework |
|---|---|
| `SSR.RenderReact(...)` | React (UMD bundle) |
| `SSR.RenderPreact(...)` | Preact (UMD bundle) |

**Framework SSR mocks.** During server-side evaluation, `'react'`, `'preact'`, and `'preact/hooks'` import specifiers are intercepted and replaced with lightweight SSR mocks that implement just enough of the API to produce a virtual DOM tree (`createElement`, `useState`, `useEffect`, etc.). The full client-side libraries are only used in the browser.

### Static vs Hydrated rendering

| | `isStatic: true` | `isStatic: false` (default) |
|---|---|---|
| HTML emitted | ✅ | ✅ |
| Hydration script | ❌ | ✅ |
| Client JS required | No | Yes |

- Use `isStatic: true` for content that never needs interactivity (e.g. marketing copy, SEO content).
- Use `isStatic: false` (default) for interactive components. The generated hydration script imports `./{ComponentName}.js` in the browser — ensure that module is publicly served at that URL.

## Embedded JS resources and ES modules

### How embedded resources work

JavaScript files are compiled into the .NET assembly as embedded resources and loaded at runtime. The component name passed to `RenderReact`/`RenderPreact` is matched against resource names using a priority search:

1. `*.{name}.min.js` — minified file preferred
2. `*.{name}.js` — unminified fallback
3. `*.{name}` — exact suffix match

No file-system access occurs at render time; everything is read from the assembly manifest.

### ES module resolution order

When a component uses `import`/`export` syntax it is evaluated as a standard ES module. Import specifiers are resolved in this order:

1. **Framework modules** — `'react'`, `'preact'`, `'preact/hooks'` (built-in SSR mocks)
2. **Custom modules** — bare specifiers registered via `ModuleLoader.RegisterModule`
3. **Embedded resources** — relative specifiers (`./`, `../`) resolved against the calling assembly's embedded resources using the same priority search above
4. **ClearScript default loader** — fallback for any remaining specifiers

### Registering a custom module

```csharp
// Program.cs (after app is built)
var moduleLoader = app.Services.GetRequiredService<ModuleLoader>();
moduleLoader.RegisterModule(
    "shared/utils",
    "export function capitalize(s) { return s[0].toUpperCase() + s.slice(1); }"
);
```

```javascript
// Any component or shared module
import { capitalize } from 'shared/utils';
```

## Samples and tests

### Running the samples

```bash
# React Hooks sample
dotnet run --project Samples/ReactHooks

# Preact sample
dotnet run --project Samples/Preact
```

Navigate to `/Home/Index` for the plain-script demo and `/Home/Greeting` for the ES module demo.

> **Note:** The sample projects depend on `BuildWebCompiler2022` for CSS/JS bundling. The tool requires Visual Studio or the .NET CLI build tools. Running `dotnet build` on the full solution may fail in environments where the tool is not installed. Build and run samples individually using the commands above.

### Running the tests

```bash
dotnet test RG.SSR.Tests/RG.SSR.Tests.csproj
```

The test project contains unit tests for the module loader and integration tests for the full React/Preact render pipeline.

## Status

| Area | Status |
|---|---|
| React plain-script rendering | ✅ Functional |
| Preact plain-script rendering | ✅ Functional |
| ES module support | ✅ Functional |
| Transitive ES module imports | ✅ Functional |
| Static island rendering | ✅ Functional |
| Hydrated component rendering | ⚠️ Experimental |
| NuGet package | ❌ Not yet published |
| Production hardening | ❌ Not production-ready |

## License

MIT
