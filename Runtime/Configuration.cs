using System;
using System.Collections;
using System.Collections.Generic;

namespace OpenUGD
{
    /// <summary>
    /// The read side of configuration: a flat, case-insensitive map from <c>string</c> to <c>string</c>
    /// in which an absent key reads as <c>null</c> instead of throwing.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Keys are paths.</b> Nesting is spelled out in the key itself, with <c>:</c> as the separator, so
    /// a JSON document, a settings object and a plain dictionary all flatten into the same shape:
    /// <c>Save:Slot</c>, <c>Servers:0:Url</c>. Sequence elements become their index. There is no tree here
    /// to walk — <see cref="ConfigurationExtensions.GetSection"/> only rewrites prefixes.
    /// </para>
    /// <para>
    /// <b>Absent and null are the same thing.</b> The indexer answers <c>null</c> for a key nobody wrote,
    /// for a key whose provider stored a JSON <c>null</c>, and for a key an override masked with
    /// <c>null</c>; enumeration skips all three. One rule and no third state, which is why
    /// <see cref="ConfigurationExtensions.TryGet"/> can report presence with a bare null check.
    /// </para>
    /// <para>
    /// <b>Values are raw text.</b> Nothing is parsed, trimmed or normalised on the way in or out; typing
    /// happens at the far end, in <see cref="ConfigurationExtensions.Bind"/>.
    /// </para>
    /// <para>
    /// <b>Where it comes from.</b>
    /// <see cref="ContextBuilderConfigurationExtensions.AddConfiguration"/> registers a
    /// <see cref="ConfigurationManager"/> as the context's <see cref="IConfiguration"/>, so a service may
    /// take one as a constructor dependency, and returns it — readable, and writable, before
    /// <see cref="ContextBuilder.BuildAsync"/>, so a registration can branch on a value while the graph is
    /// still being described, and read-only from the build on. A child builder that calls it layers its own
    /// values over its parent's configuration, read through at lookup time; one that does not resolves the
    /// parent's <see cref="IConfiguration"/> itself. A context nobody called it for has no configuration,
    /// and a service that needs one fails the build like any other missing dependency.
    /// </para>
    /// </remarks>
    public interface IConfiguration : IEnumerable<KeyValuePair<string, string>>
    {
        /// <summary>
        /// The value configured at <paramref name="key"/>, or <c>null</c> if nothing is. Absence is not an
        /// error: choosing a default is the caller's job, not the map's.
        /// </summary>
        /// <param name="key">
        /// The full path from the root of this view, <c>:</c>-separated — not a single segment.
        /// <see cref="ConfigurationManager"/> compares it case-insensitively and rejects <c>null</c>.
        /// </param>
        /// <value>The raw, unparsed value, or <c>null</c> when the key is absent or was stored null.</value>
        string this[string key] { get; }
    }

    /// <summary>
    /// The configuration that <see cref="ContextBuilderConfigurationExtensions.AddConfiguration"/>
    /// registers: provider values underneath, explicit overrides on top, the override winning regardless of
    /// which was written first — and, in a child context, the parent's configuration below both.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Two layers, one map.</b> The providers —
    /// <see cref="ConfigurationManagerExtensions.AddJson"/>,
    /// <see cref="ConfigurationManagerExtensions.AddObject"/> and
    /// <see cref="ConfigurationManagerExtensions.AddDictionary"/> — write the lower layer; the indexer's
    /// setter writes the upper one. That is what lets a test pin <c>configuration["Save:Slot"] = "3"</c>
    /// and still load the shipped JSON afterwards, and it is how a child context overrides a value it
    /// inherited from its parent. Within a layer the last write to a key wins, so provider call order is
    /// provider precedence.
    /// </para>
    /// <para>
    /// <b>Masking.</b> Setting a key to <c>null</c> is how you remove one: the override is recorded, hides
    /// whatever a provider supplied, and reads back as absent. There is deliberately no <c>Remove</c> —
    /// removing an override would resurrect the provider value underneath it, which is never what the
    /// caller meant.
    /// </para>
    /// <para>
    /// <b>Keys</b> are compared with <see cref="StringComparer.OrdinalIgnoreCase"/>, so <c>save:slot</c>
    /// and <c>Save:Slot</c> are one key. Within each layer the spelling first written is the one kept; for a
    /// key an override shadows, enumeration shows the override's spelling. This matters only when
    /// enumerating.
    /// </para>
    /// <para>
    /// <b>Read-only once its context is built.</b> The manager
    /// <see cref="ContextBuilderConfigurationExtensions.AddConfiguration"/> returns is frozen when
    /// <see cref="ContextBuilder.BuildAsync"/> constructs the context's <see cref="IConfiguration"/>, which is
    /// before any service that obtains it as <see cref="IConfiguration"/> — through its constructor, a
    /// factory or an <see cref="InjectAttribute"/> member — and before the boot phases. From then on the
    /// indexer's setter and every <c>Add*</c> provider throw <see cref="InvalidOperationException"/>, and
    /// casting the <see cref="IConfiguration"/> back to <see cref="ConfigurationManager"/> does not make it
    /// writable. Its own values therefore never change again: what a service reads in its constructor is
    /// what it keeps reading — except, in a child context, values read through from a parent configuration
    /// that is itself live. For values that change at run time, register your own
    /// <see cref="IConfiguration"/> implementation instead. A manager created with <c>new</c> and never
    /// handed to a builder stays writable, and so does one whose build failed or was cancelled before it
    /// constructed any service. Code that holds the manager some other way — a variable captured by an
    /// earlier registration's factory, or the manager registered again under its own type — can still write
    /// until the freeze, so hand it to services only as <see cref="IConfiguration"/>.
    /// </para>
    /// <para>
    /// <b>In a child context</b> the manager reads through to the parent's <see cref="IConfiguration"/> for
    /// every key it does not hold itself, at lookup time — so a parent whose configuration is live (a custom
    /// implementation) is tracked, not copied. A key the child holds in either layer hides the parent's,
    /// including a key the child stored as <c>null</c>, which is how a child removes an inherited value.
    /// </para>
    /// <para>
    /// <b>Thread safety.</b> Populate it on one thread before <see cref="ContextBuilder.BuildAsync"/>;
    /// concurrent writes are not safe against each other or against a read. Once frozen its own layers
    /// never change, so services may read it from any thread (a parent configuration it reads through is as
    /// thread-safe as that implementation).
    /// </para>
    /// </remarks>
    public sealed class ConfigurationManager : IConfiguration
    {
        private readonly Dictionary<string, string> _providers =
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        private readonly Dictionary<string, string> _overrides =
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        private readonly IConfiguration _parent;
        private bool _frozen;

        /// <summary>
        /// Creates an empty, writable manager with no parent.
        /// </summary>
        public ConfigurationManager()
        {
        }

        internal ConfigurationManager(IConfiguration parent)
        {
            _parent = parent;
        }

        /// <summary>
        /// Reads the effective value at <paramref name="key"/> — override first, then provider, then the
        /// parent configuration of a child context — or writes an override that shadows whatever any provider
        /// supplies for that key, before or after this call.
        /// </summary>
        /// <remarks>
        /// The setter never touches the provider layer, so a value written here survives a later
        /// <c>Add*</c> of the same key. Writing <c>null</c> is legal and masks the provider value.
        /// </remarks>
        /// <param name="key">The full <c>:</c>-separated path. Compared case-insensitively.</param>
        /// <value>
        /// On read: the override if one was set — including a <c>null</c> one — otherwise the provider
        /// value if one was set, again including <c>null</c>, otherwise the parent's value in a child
        /// context, otherwise <c>null</c>. The string is exactly what was stored.
        /// </value>
        /// <exception cref="ArgumentNullException">
        /// <paramref name="key"/> is <c>null</c>, on the getter and on the setter alike.
        /// </exception>
        /// <exception cref="InvalidOperationException">
        /// On the setter: the manager is read-only because its context has been built.
        /// </exception>
        public string this[string key]
        {
            get
            {
                if (key == null) throw new ArgumentNullException(nameof(key));
                string value;
                if (_overrides.TryGetValue(key, out value)) return value;
                if (_providers.TryGetValue(key, out value)) return value;
                return _parent != null ? _parent[key] : null;
            }
            set
            {
                if (key == null) throw new ArgumentNullException(nameof(key));
                ThrowIfFrozen();
                _overrides[key] = value;
            }
        }

        /// <summary>
        /// Walks every key that currently has a value: provider entries no override shadows, then the
        /// overrides, then — in a child context — the parent's pairs whose key the child does not hold. Keys
        /// stored with a <c>null</c> value are skipped, so a key appears at most once and no pair ever
        /// carries a null value.
        /// </summary>
        /// <remarks>
        /// Lazy, and a live view rather than a snapshot — nothing is copied, and each pair is produced as
        /// you step. Do not write while a walk is in progress: the walk is up to three passes — the
        /// providers, the overrides, then in a child the parent's pairs — and a write during one has a
        /// different effect in each (it may throw <see cref="InvalidOperationException"/>, be picked up by a
        /// later pass, or hide a parent pair without being yielded). Materialise the sequence first if you
        /// mean to write while reading. Order within each layer is the underlying dictionary's, and is not a
        /// guarantee.
        /// </remarks>
        /// <returns>An enumerator over the effective key/value pairs.</returns>
        public IEnumerator<KeyValuePair<string, string>> GetEnumerator()
        {
            foreach (var pair in _providers)
            {
                if (pair.Value != null && !_overrides.ContainsKey(pair.Key)) yield return pair;
            }

            foreach (var pair in _overrides)
            {
                if (pair.Value != null) yield return pair;
            }

            if (_parent == null) yield break;

            foreach (var pair in _parent)
            {
                if (pair.Value != null && !_overrides.ContainsKey(pair.Key) && !_providers.ContainsKey(pair.Key))
                    yield return pair;
            }
        }

        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

        internal void SetProviderValue(string key, string value)
        {
            ThrowIfFrozen();
            _providers[key] = value;
        }

        /// Called while the owning context is built: from then on nothing can write to this manager.
        internal void Freeze() => _frozen = true;

        internal void ThrowIfFrozen()
        {
            if (!_frozen) return;

            throw new InvalidOperationException(
                "This configuration is read-only: its context has been built. Write configuration before " +
                "BuildAsync, or register your own IConfiguration implementation for values that change at run " +
                "time.");
        }
    }
}
