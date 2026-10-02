# Configuration

String-keyed, layered configuration for [`com.openugd.context`](https://github.com/openugd/upm-context).

> **Unreleased 0.x, outside the OpenUGD 2.0 release.** This package is not published on OpenUPM, and
> its repository, `github.com/openugd/upm-configuration`, has not been created yet. It has known open
> defects, listed [below](#known-open-defects), and it will stay unpublished until they are fixed and it
> has its own samples. Until then its API may change in any way.

The code used to live inside `com.openugd.context`. It was moved out before context 2.0.0 so that the
container carries no configuration system: in context, a setting is a `ScriptableObject` (or any object)
registered with `AddInstance`, and that remains the recommended way. Use this package only if you need
what it adds on top: one flat map of `string` keys to `string` values, filled from JSON documents,
dictionaries and settings objects, with explicit overrides that always win, and typed binding onto
plain settings classes.

## Install

Not on OpenUPM. To try it, clone the repository next to your project and reference it as a local
package in `Packages/manifest.json`:

```json
{
  "dependencies": {
    "com.openugd.configuration": "file:../../upm-configuration"
  }
}
```

It depends on `com.openugd.context` 2.0.0 and `com.openugd.lifetime` 2.0.0.

## Usage

A settings class is plain data: a public parameterless constructor and public read/write members.

```csharp
using OpenUGD;

public sealed class ServerOptions
{
    public string Url { get; set; } = "https://localhost";
    public int TimeoutSeconds { get; set; } = 10;
}

public sealed class Api
{
    public Api(IConfiguration configuration) => Options = configuration.Get<ServerOptions>("Server");

    public ServerOptions Options { get; }
}
```

`AddConfiguration` registers a `ConfigurationManager` as the context's `IConfiguration` and returns it,
so the composition root can fill it and branch on it while it registers services:

```csharp
var builder = Context.CreateBuilder(scope);

var configuration = builder.AddConfiguration();
configuration
    .AddJson(defaultsJson)                      // providers: a later source wins over an earlier one
    .AddDictionary(commandLineValues);
configuration["Server:TimeoutSeconds"] = "30";  // an override wins over every provider

if (configuration["Features:Tooltips"] == "on")
    builder.Services.Add<Tooltips>();

builder.Services.Add<Api>();
var context = await builder.BuildAsync();
```

Keys are `:`-separated paths compared case-insensitively, so `{"Server":{"Url":"…"}}` and
`Server:Url` are the same key. An absent key reads as `null`.

## How it fits the container

It touches the container in one place, the `AddConfiguration` extension method on `ContextBuilder`.

- **Opt-in.** A context whose builder never calls it has no `IConfiguration`. A service that takes one
  then fails `BuildAsync` validation like any other missing dependency.
- **Child contexts.** A child builder that calls `AddConfiguration` starts with a copy of the pairs its
  parent's `IConfiguration` enumerates, in the provider layer, so its own overrides win and the parent is
  not changed. The copy is taken once, when it is called. A child that does not call it resolves the
  parent's `IConfiguration` itself, like any inherited service.
- **Once per builder.** A second call registers a second `IConfiguration`, and the build rejects the
  duplicate, naming both call sites.

## API

| Type | What it is |
| --- | --- |
| `IConfiguration` | The read side: an indexer and an enumerator over the effective pairs. |
| `ConfigurationManager` | The writable map. Providers underneath, indexer overrides on top. |
| `ConfigurationManagerExtensions` | Providers: `AddJson`, `AddObject`, `AddDictionary`. |
| `ConfigurationExtensions` | Reading: `TryGet`, `GetSection`, `Get<T>`, `Bind`. |
| `ContextBuilderConfigurationExtensions` | `AddConfiguration`, the container integration. |
| `ConfigurationException` | Malformed JSON, a value that cannot be converted, a settings type that cannot be created. |

## Known open defects

These are why the package is unreleased. The IDs refer to the OpenUGD audit of September 2026.

- **CX-17: `Get<T>` throws for a type without a public parameterless constructor**, which includes every
  scalar. `configuration.Get<int>("Save:Slot")` and `Get<string>(…)` throw a `ConfigurationException`.
  Read a scalar through the indexer and parse it yourself.
- **CX-18: `Bind` does not recurse into structs.** It recurses only into class members, so a member of
  struct type is not filled from nested keys such as `Size:X` and `Size:Y`.
- **CX-19: collections do not round-trip.** `AddObject` writes a list or array as `Servers:0`,
  `Servers:1`, …, but `Bind` never fills a collection member from those keys.
- **CX-24: JSON parser edge cases.** A `\u` escape whose four characters are not hexadecimal throws a
  `FormatException` instead of a `ConfigurationException`. A number only has to parse as a `double`, so
  `NaN` and `Infinity` are accepted. A document that starts with a byte-order mark is rejected as
  malformed.
- **CX-25: configuration stays writable after the build.** The manager registered as `IConfiguration` is
  the one `AddConfiguration` returned. Writes after `BuildAsync` reach every service that holds it, and a
  service can cast its `IConfiguration` back to `ConfigurationManager` and write to it.
- **CX-26: a child's configuration does not track its parent's.** Inside context, a child's automatic
  `IConfiguration` shadowed a custom one registered in its parent. Now a child that does not call
  `AddConfiguration` resolves whatever its parent registered, but a child that does call it replaces the
  parent's `IConfiguration` with a one-time copy of the pairs the parent enumerated. A custom live
  implementation is copied, not chained, and later writes to the parent never reach the child.
- **CX-27: `AddObject` on a `UnityEngine.Object` flattens engine properties too.** A `ScriptableObject`
  contributes `name` and `hideFlags` along with its own fields, because both are public read/write
  properties.

**IL2CPP and managed-code stripping.** `Bind` writes, and `AddObject` reads, public fields and properties
by reflection only, and creates nested settings objects through their parameterless constructor. The
managed linker strips members that nothing else calls. A property whose setter was stripped no longer
counts as writable, so `Bind` skips it silently and the value stays at its default; a stripped
constructor makes `Bind` throw. The binder has not been run through UnityLinker since it was moved out.
Until `Bind` fails loudly on a stripped setter, preserve your settings types in a `link.xml` under your
project's `Assets` folder, for example `<type fullname="MyGame.ServerOptions" preserve="all"/>`.

## Licence

Apache-2.0. See [LICENSE.md](LICENSE.md).
