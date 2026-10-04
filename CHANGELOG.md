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
- `AddConfiguration`, an extension method on `ContextBuilder`, and the only place the package touches the
  container. Configuration is opt-in: a context has an `IConfiguration` only if its builder, or an
  ancestor's, calls `AddConfiguration` or registers its own. The method registers a new
  `ConfigurationManager` as `IConfiguration` and returns it for the composition root to fill.
- **Read-only once built.** `BuildAsync` freezes the manager `AddConfiguration` returned when it constructs
  the `IConfiguration` registration, and a later write, through the indexer or any `Add*`, throws
  `InvalidOperationException`, also through an `IConfiguration` cast back to `ConfigurationManager`.
- **Child contexts.** A child builder that calls `AddConfiguration` reads through to its parent's
  `IConfiguration` at lookup time, so a live parent configuration is tracked; a key the child holds, even as
  `null`, hides the parent's. Calling it under a parent that has already ended does not throw. A child that
  does not call it resolves its parent's `IConfiguration` itself, like any inherited service.
- **`Get<T>` reads scalars**: `Get<int>("Save:Slot")` converts the value at that key, and an absent key
  gives `default(T)`. It also reads collections, structs and settings objects.
- **`Bind` fills structs**, including `Nullable` ones and Unity's value types, and binds a nested settings
  object into a shallow copy of the instance the member holds, so the copy's unconfigured members keep their
  values and a static or shared default keeps its own fields. An object that owns a resource (a Unity
  class, or a class with a finalizer) is bound in place instead.
- **Collections round-trip**: arrays, `List<T>`, the interfaces `List<T>` implements, and concrete
  `ICollection<T>` classes are bound from `Key:0`, `Key:1`, …, the keys `AddJson` and `AddObject` write. A
  configured list replaces the member's default one.
- **`AddJson` is a strict RFC 8259 reader**: numbers follow JSON's grammar, so `NaN`, `Infinity`, `+1`
  and `01` are malformed; a raw control character in a string, whitespace other than JSON's four
  characters, and a `\u` escape without four hexadecimal digits are malformed; a leading byte-order mark is
  accepted. An empty property name is an ordinary key.
- **Unity's own members are left out**: `AddObject` and `Bind` skip members declared by classes in the
  `UnityEngine` namespace or below (`name`, `hideFlags`, …), and `Get<T>` refuses to create a
  `UnityEngine.Object`.
- **`AddObject` writes dates with the round-trip pattern** `"O"`, and floats and doubles with every digit
  they need, so fractional seconds, the kind or offset, and exact floating-point values survive the trip back
  through `Bind`, a negative zero included (Mono's default formatting prints `0.3f * 3` as `0.9`).
- `ConfigurationException`, thrown for malformed JSON (the message gives the character offset), a value
  that cannot be converted, a settings type that cannot be created (no public parameterless constructor,
  abstract, or a `UnityEngine.Object`), and a JSON document or object graph nested more than 32 levels deep.

### Known issues

- The IL2CPP setter-stripping hazard, which the README describes with its workaround.
- No `Samples~` yet.
