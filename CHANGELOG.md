# Changelog

All notable changes to this project will be documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.1.0/),
and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [0.1.0] - Unreleased

Not published, and not part of the OpenUGD 2.0 release. The code was moved out of
`com.openugd.context` before context 2.0.0, so that the container carries no configuration system.

### Added

- `IConfiguration` and `ConfigurationManager`: a flat, case-insensitive `string → string` map with a
  provider layer and an override layer, where an override always wins.
- Providers `AddJson`, `AddObject` and `AddDictionary`; reading with `TryGet`, `GetSection`, `Get<T>`
  and `Bind`.
- `AddConfiguration`, an extension method on `ContextBuilder`. It registers a new `ConfigurationManager`
  as `IConfiguration` and returns it, writable until `BuildAsync` and read-only from then on. A child
  builder that calls it reads through to its parent's configuration.
- `ConfigurationException`, thrown for malformed JSON, a value that cannot be converted, a settings type
  that cannot be created (no public parameterless constructor, abstract, or a `UnityEngine.Object`), and
  an object graph nested more than 32 levels deep.

### Changed from the code as it was inside com.openugd.context

- **Configuration is opt-in.** `ContextBuilder.Configuration` is gone, and a context no longer registers
  an `IConfiguration` automatically. Call `AddConfiguration`, which returns the manager to fill.
- **A child that does not call `AddConfiguration` resolves its parent's `IConfiguration`** instead of
  receiving a copy of it.
- **Failures throw `ConfigurationException`** instead of `ContextException`.
- **`Get<T>` reads scalars** (CX-17): `Get<int>("Save:Slot")` converts the value at that key, and an
  absent key gives `default(T)`. It also reads collections and structs.
- **`Bind` fills structs** (CX-18), including `Nullable` ones and Unity's value types, and binds a nested
  settings object into a shallow copy of the instance the member holds, so its unconfigured members keep
  their values and a static or shared default keeps its own fields. An object that owns a resource (a
  Unity class, or a class with a finalizer) is bound in place instead. It used to replace the held
  instance with a new, default-constructed one.
- **Collections round-trip** (CX-19): arrays, `List<T>`, the interfaces `List<T>` implements, and concrete
  `ICollection<T>` classes are bound from `Key:0`, `Key:1`, …, the keys `AddJson` and `AddObject` write. A
  configured list replaces the member's default one.
- **`AddJson` is strict** (CX-24): a `\u` escape that is not hexadecimal is a `ConfigurationException`, not
  a `FormatException`; numbers follow JSON's grammar, so `NaN`, `Infinity`, `+1` and `01` are malformed; a
  leading byte-order mark is accepted; a raw control character in a string, and whitespace other than
  JSON's four characters, are malformed. An empty property name is an ordinary key, and the 32-level limit
  counts containers exactly.
- **Read-only once built** (CX-25): `BuildAsync` freezes the manager `AddConfiguration` returned when it
  constructs the `IConfiguration` registration, and a later write, through the indexer or any `Add*`,
  throws `InvalidOperationException`, also through an `IConfiguration` cast back to
  `ConfigurationManager`.
- **A child reads through to its parent** (CX-26) at lookup time instead of copying its pairs once, so a
  live parent configuration is tracked; a key the child holds, even as `null`, hides the parent's. A parent
  that has ended no longer makes `AddConfiguration` throw `ObjectDisposedException`.
- **Unity's own members are left out** (CX-27): `AddObject` and `Bind` skip members declared by classes
  in the `UnityEngine` namespace or below (`name`, `hideFlags`, …), and `Get<T>` refuses to create a
  `UnityEngine.Object`.
- **`AddObject` writes dates with the round-trip pattern** `"O"`, and floats and doubles with every digit
  they need, so fractional seconds, the kind or offset, and exact floating-point values survive the trip back
  through `Bind`, a negative zero included (Mono's default formatting printed `0.3f * 3` as `0.9`).

### Known issues

- The IL2CPP setter-stripping hazard, which the README describes with its workaround.
- No `Samples~` yet.
