# Configuration

[![Tests](https://github.com/openugd/upm-tools/actions/workflows/ci.yml/badge.svg)](https://github.com/openugd/upm-tools/actions/workflows/ci.yml)

String-keyed, layered configuration for [`com.openugd.context`](https://github.com/openugd/upm-context).

> **Unreleased 0.x, outside the OpenUGD 2.0 release.** This package is not published on OpenUPM yet, and
> stays unpublished until it has its own samples. Until then its API may change in any way.

The code used to live inside `com.openugd.context`. It was moved out before context 2.0.0 so that the
container carries no configuration system: in context, a setting is a `ScriptableObject` (or any object)
registered with `AddInstance`, and that remains the recommended way. Use this package only if you need
what it adds on top: one flat map of `string` keys to `string` values, filled from JSON documents,
dictionaries and settings objects, with explicit overrides that always win, and typed binding onto
plain settings classes.

## Install

Not on OpenUPM. To try it, install it from git. It depends on `com.openugd.context` 2.0.0 and
`com.openugd.lifetime` 2.0.0, which are on OpenUPM, so add the OpenUPM registry for the `com.openugd`
scope as well. In `Packages/manifest.json`:

```json
{
  "scopedRegistries": [
    {
      "name": "package.openupm.com",
      "url": "https://package.openupm.com",
      "scopes": ["com.openugd"]
    }
  ],
  "dependencies": {
    "com.openugd.configuration": "https://github.com/openugd/upm-configuration.git"
  }
}
```

There are no tags yet, so the git URL follows `main`. A local clone works the same way:
`"com.openugd.configuration": "file:../../upm-configuration"` (the path is relative to `Packages/`).

## Requirements

- Unity 6000.0 or newer. Tested with 6000.0.41f1.
- [`com.openugd.context`](https://github.com/openugd/upm-context) 2.0.0 and
  [`com.openugd.lifetime`](https://github.com/openugd/upm-lifetime) 2.0.0, from OpenUPM through the scoped
  registry shown above.
- Nothing else. The runtime assembly is compiled with `noEngineReferences`; only the tests reference
  `UnityEngine`, and only the tests need the Unity Test Framework.

## Usage

A settings class is plain data: a public parameterless constructor and public read/write members. Members
can be scalars, nested settings classes, structs (Unity's `Vector3` and `Color` included), and lists or
arrays of any of these. A nested settings object is bound into a shallow copy of the one the member holds,
so a static default or an instance two members share keeps its own values.

```csharp
using System.Collections.Generic;
using OpenUGD;

public struct Backoff
{
    public int FirstSeconds;
    public int MaxSeconds;
}

public sealed class ServerOptions
{
    public string Url { get; set; } = "https://localhost";
    public int TimeoutSeconds { get; set; } = 10;
    public Backoff Retry;                     // Server:Retry:FirstSeconds, Server:Retry:MaxSeconds
    public List<string> Mirrors { get; set; } // Server:Mirrors:0, Server:Mirrors:1, ...
        = new List<string>();
}

public sealed class Api
{
    public Api(IConfiguration configuration)
    {
        Options = configuration.Get<ServerOptions>("Server");
        Slot = configuration.Get<int>("Save:Slot");   // one value: 0 when the key is absent
    }

    public ServerOptions Options { get; }
    public int Slot { get; }
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
`Server:Url` are the same key. An absent key reads as `null`. A JSON array, or a list passed to
`AddObject`, becomes indexed keys (`Server:Mirrors:0`, `Server:Mirrors:1`), and `Bind` reads them back into
an array or list member. A configured list replaces the member's default list rather than adding to it.

`AddJson` is a strict JSON reader (RFC 8259): no comments or trailing commas, and numbers only in JSON's
own grammar. Every malformation is a `ConfigurationException` that gives the character offset.

## How it fits the container

It touches the container in one place, the `AddConfiguration` extension method on `ContextBuilder`.

- **Opt-in.** Nothing registers an `IConfiguration` automatically: a context has one only if its builder,
  or an ancestor's, calls `AddConfiguration` or registers its own. Without one, a service that takes an
  `IConfiguration` fails `BuildAsync` validation like any other missing dependency.
- **Read-only once built.** The manager it returns is writable until `BuildAsync` constructs the
  `IConfiguration` registration, which happens before any service obtains it and before the boot phases.
  After that a write throws `InvalidOperationException`, even through an `IConfiguration` cast back to
  `ConfigurationManager`, so a service keeps reading the values it read in its constructor (in a child, a
  live parent's changes still show through). Hand the manager to services only as `IConfiguration`. For
  values that change at run time, register your own `IConfiguration`.
- **Child contexts.** A child builder that calls `AddConfiguration` gets a manager that reads through to
  its parent's `IConfiguration` for every key it does not hold, at lookup time. Its own values win, a key it
  sets to `null` hides the parent's, and the parent is not changed. Nothing is copied, so a parent whose
  `IConfiguration` is live is tracked. A child that does not call it resolves the parent's
  `IConfiguration` itself, like any inherited service.
- **Once per builder.** A second call registers a second `IConfiguration`, and the build rejects the
  duplicate, naming both call sites.

## API

All types are in the `OpenUGD` namespace.

| Type | What it is |
| --- | --- |
| `IConfiguration` | The read side: an indexer and an enumerator over the effective pairs. |
| `ConfigurationManager` | The writable map. Providers underneath, indexer overrides on top. |
| `ConfigurationManagerExtensions` | Providers: `AddJson`, `AddObject`, `AddDictionary`. |
| `ConfigurationExtensions` | Reading: `TryGet`, `GetSection`, `Get<T>` (a scalar, a collection or a settings object), `Bind`. |
| `ContextBuilderConfigurationExtensions` | `AddConfiguration`, the container integration. |
| `ConfigurationException` | Malformed JSON, a value that cannot be converted, a settings type that cannot be created (including a `UnityEngine.Object`). |

## Limitations

- **What binds.** Public read/write fields and properties. Get-only members, private members and members
  declared by classes in the `UnityEngine` namespace or below (`name`, `hideFlags`, `enabled`, …) are not
  bound and not written by `AddObject`. Unity's value types (`Vector3`, `Color`, `Rect`) bind like any
  struct, and classes Unity ships in other namespaces (`TMPro`, `Unity.*`) like any class. A dictionary
  member is not bound; read a map with `GetSection` and enumerate it.
- **Copies are shallow.** A nested settings object is bound into a `MemberwiseClone` of the one the member
  holds. What that object references is shared, so state a property writes through to an inner object can
  still change. An object that owns a resource (a `UnityEngine.Object`, another Unity class such as
  `AnimationCurve`, or any class with a finalizer) is bound in place, never copied.
- **Overlapping members.** Some types expose the same state through several settable members, for example
  `Rect`, `RectInt`, `Bounds` and `BoundsInt` (`x`, `position`, `min`, `center`, …), `Quaternion`
  (`eulerAngles` writes `x`…`w`) and `Resolution` (the obsolete `refreshRate` overwrites `refreshRateRatio`
  with whole hertz). `AddObject` writes all of them, and `Bind` applies them one after another (fields
  before properties), so the last one applied wins. Configure such a member through one set of keys, and do
  not use `AddObject` for its defaults.
- **Unity objects.** `Get<T>` never creates a `UnityEngine.Object`. Create a `ScriptableObject` or a
  component the Unity way and call `Bind` on it. Only its public members are bound, not its private
  `[SerializeField]` fields.
- **IL2CPP and managed-code stripping.** `Bind` writes, and `AddObject` reads, public fields and properties
  by reflection only, and creates nested settings objects through their parameterless constructor. The
  managed linker strips members that nothing else calls. A property whose setter was stripped no longer
  counts as writable, so `Bind` skips it silently and the value stays at its default; a stripped
  constructor makes `Bind` throw. The binder has not been run through UnityLinker since it was moved out.
  Preserve your settings types in a `link.xml` under your project's `Assets` folder. Its `<assembly>`
  element names the assembly that declares them: the name of their assembly definition, or
  `Assembly-CSharp` for scripts outside one. List every settings type the binder reaches, not only the
  root: a nested settings class or struct, and one used as the element type of a list or array, needs its
  own entry. A type's `fullname` includes its namespace (`MyGame.ServerOptions` for a type declared in
  `namespace MyGame`). For the Usage example's types, which are declared in no namespace:

  ```xml
  <linker>
    <assembly fullname="Assembly-CSharp">
      <type fullname="ServerOptions" preserve="all"/>
      <type fullname="Backoff" preserve="all"/>
    </assembly>
  </linker>
  ```

## Running the tests

The package's tests are an EditMode assembly, `com.openugd.configuration.tests`. List the package under
`testables` in `Packages/manifest.json`, next to the scoped registry from [Install](#install); your project
needs `com.unity.test-framework`, which new projects already have:

```json
{
  "dependencies": {
    "com.openugd.configuration": "https://github.com/openugd/upm-configuration.git"
  },
  "testables": [
    "com.openugd.configuration"
  ]
}
```

Then open *Window > General > Test Runner* and run the EditMode tests. The 5 tests in the category
`RequiresUnity` (`UnityObjectTests`) need the editor; the rest are plain .NET and use no engine API.

The checks also run in public CI: [openugd/upm-tools](https://github.com/openugd/upm-tools/actions/workflows/ci.yml)
compiles this package and the complete examples in this README (those that declare a type) against Unity
6000.0's assemblies and runs its engine-free tests on every change there and every Monday. It does not run
the `RequiresUnity` tests; run those in the editor.

## Versioning

The package follows [Semantic Versioning](https://semver.org/spec/v2.0.0.html). While its major version is
0, any version may change the API. The changes in each version are listed in [CHANGELOG.md](CHANGELOG.md).

## Contributing

Report a bug or an idea at
[github.com/openugd/upm-configuration/issues](https://github.com/openugd/upm-configuration/issues): include
the Unity version, the package version (for a git install, the commit) and, for an exception, the full
message. To work on the package, clone it, reference the clone from a Unity 6 project
(`"com.openugd.configuration": "file:../path/to/upm-configuration"` in `Packages/manifest.json`), add
`com.openugd.configuration` to `testables`, and run its tests in the Test Runner. The project also needs
`com.openugd.context` and `com.openugd.lifetime`: keep the scoped registry from [Install](#install), or
reference clones of [upm-context](https://github.com/openugd/upm-context) and
[upm-lifetime](https://github.com/openugd/upm-lifetime) the same way. The checks CI runs are scripts in
[openugd/upm-tools](https://github.com/openugd/upm-tools); its README shows how to run them locally.

## Licence

Apache-2.0. See [LICENSE.md](LICENSE.md).
