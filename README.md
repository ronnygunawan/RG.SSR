# RG.SSR

Server-side rendering for React and Preact components in ASP.NET Core, powered by ClearScript V8. No Node.js required.

Components are compiled into your .NET assembly as embedded resources and evaluated on the server to produce HTML.

## Features

- React & Preact support with the same API
- ES module support — `import`/`export` with transitive dependency resolution
- All JavaScript lives as embedded resources in the assembly
- Auto-detection of module syntax — plain scripts and ES modules coexist
- Static islands or hydrated interactive components
- Custom module registration for shared utilities

## Install

```bash
dotnet add package RG.SSR
```

> **Note:** NuGet package is not yet published. For now, use a project reference:
>
> ```xml
> <ProjectReference Include="path/to/RG.SSR/RG.SSR.csproj" />
> ```

## Usage

### Register services

```csharp
builder.Services.AddServerSideRendering(options =>
{
    options.React.InlineLibrary = true;
});
```

### Write a component

**Plain script:**

```javascript
// Views/Home/Index.min.js
var Index = function() {
    return React.createElement("h1", null, "Welcome");
};
```

**ES module:**

```javascript
// Views/Home/Greeting.js
import { createElement } from 'react';
import { formatGreeting } from './formatting.js';

export default function Greeting(props) {
    return createElement('h1', null, formatGreeting(props.name));
}
```

### Embed as resources

```xml
<ItemGroup>
    <EmbeddedResource Include="Views/**/*.min.js" />
    <EmbeddedResource Include="Views/**/Greeting.js" />
    <EmbeddedResource Include="Views/**/Shared/*.js" />
</ItemGroup>
```

### Render in Razor

```razor
@using RG.SSR
@inject ServerSideRenderer SSR

@* Static island — server-rendered HTML only, no client-side JavaScript *@
@SSR.RenderReact("Index", isStatic: true)

@* Hydrated component — server-rendered HTML + hydration script for interactivity *@
@SSR.RenderReact("Counter")

@* With props *@
@SSR.RenderReact("Greeting", new { Name = "World" }, isStatic: true)
```

### Static vs Hydrated

- `isStatic: true` — renders the component on the server and emits only HTML. No JavaScript is sent to the client. Use this for content that doesn't need interactivity.
- `isStatic: false` (default) — renders on the server, then emits a hydration script so the component becomes interactive in the browser.

## ES Module Resolution

When a component uses `import`/`export` syntax, the renderer evaluates it as an ES module. Import specifiers are resolved in this order:

1. **Framework modules** — `'react'`, `'preact'`, `'preact/hooks'` (pre-registered SSR mocks)
2. **Custom modules** — bare specifiers registered via `ModuleLoader.RegisterModule`
3. **Embedded resources** — relative specifiers (`./`, `../`) matched by suffix (`.min.js` > `.js` > exact)
4. **ClearScript default loader** — fallback

### Custom modules

```csharp
var moduleLoader = app.Services.GetRequiredService<ModuleLoader>();
moduleLoader.RegisterModule("shared/utils", "export function capitalize(s) { return s[0].toUpperCase() + s.slice(1); }");
```

```javascript
import { capitalize } from 'shared/utils';
```

## Samples

Both sample projects include plain-script and ES module components:

```bash
dotnet run --project Samples/ReactHooks
dotnet run --project Samples/Preact
```

Navigate to `/Home/Greeting` to see the ES module demo.

## License

MIT
