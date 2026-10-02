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
  as `IConfiguration` and returns it. A child builder that calls it starts with a copy of its parent's
  values.
- `ConfigurationException`, thrown for malformed JSON, a value that cannot be converted, a settings type
  without a public parameterless constructor, and an object graph nested more than 32 levels deep.

### Changed from the code as it was inside com.openugd.context

- **Configuration is opt-in.** `ContextBuilder.Configuration` is gone, and a context no longer registers
  an `IConfiguration` automatically. Call `AddConfiguration`, which returns the manager to fill.
- **A child that does not call `AddConfiguration` resolves its parent's `IConfiguration`** instead of
  receiving a copy of it.
- **Failures throw `ConfigurationException`** instead of `ContextException`.

### Known issues

- Open defects CX-17, CX-18, CX-19, CX-24, CX-25, CX-26 and CX-27, and the IL2CPP setter-stripping
  hazard. The README describes each of them.
- No `Samples~` yet.
