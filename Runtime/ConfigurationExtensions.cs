using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Reflection;

namespace OpenUGD
{
    /// <summary>
    /// Reading and typed binding over <see cref="IConfiguration"/>: presence tests, prefix views, and the
    /// projection of a flat key/value map onto a plain settings object.
    /// </summary>
    /// <remarks>
    /// Everything here is written against the <see cref="IConfiguration"/> indexer and enumerator alone,
    /// so it applies equally to a <see cref="ConfigurationManager"/> still being populated during
    /// registration, to a section view of one, and to any other implementation.
    /// </remarks>
    public static class ConfigurationExtensions
    {
        /// <summary>
        /// Reads a key and reports whether it was configured, for the "use this only if it is there"
        /// branch that a bare indexer read otherwise turns into a null check at every call site.
        /// </summary>
        /// <param name="configuration">The map to read.</param>
        /// <param name="key">The full <c>:</c>-separated path.</param>
        /// <param name="value">
        /// The raw value on success, <c>null</c> on failure — so it is safe to ignore the return value and
        /// test this instead.
        /// </param>
        /// <returns>
        /// <c>true</c> if a non-null value is configured. A key stored with a <c>null</c> value is
        /// indistinguishable from a missing one and yields <c>false</c>.
        /// </returns>
        /// <exception cref="ArgumentNullException">
        /// <paramref name="configuration"/> or <paramref name="key"/> is <c>null</c>.
        /// </exception>
        public static bool TryGet(this IConfiguration configuration, string key, out string value)
        {
            if (configuration == null) throw new ArgumentNullException(nameof(configuration));
            if (key == null) throw new ArgumentNullException(nameof(key));

            value = configuration[key];
            return value != null;
        }

        /// <summary>
        /// Narrows the map to the keys under <paramref name="prefix"/>, with that prefix and its <c>:</c>
        /// stripped from every key, so a component can be written as though it owned the root.
        /// </summary>
        /// <remarks>
        /// <para>
        /// The result is a live view, not a copy: it holds the source and re-reads it, so anything written
        /// afterwards — including an override on a <see cref="ConfigurationManager"/> — shows through, and
        /// taking a section allocates one small wrapper rather than a filtered dictionary. Sections
        /// compose: <c>GetSection("A").GetSection("B")</c> sees the same keys as <c>GetSection("A:B")</c>.
        /// </para>
        /// <para>
        /// A view is read-only, because it is an <see cref="IConfiguration"/>; there is no section-scoped
        /// write. Enumeration matches the prefix case-insensitively, while the indexer inherits whatever
        /// comparison the source uses.
        /// </para>
        /// </remarks>
        /// <param name="configuration">The map to narrow.</param>
        /// <param name="prefix">
        /// The section path, without a trailing <c>:</c>. <c>null</c> or empty means "no narrowing", and
        /// the source is returned unwrapped — so the caller can get back the very instance it passed in.
        /// </param>
        /// <returns>
        /// A view over the matching keys, or <paramref name="configuration"/> itself for an empty prefix.
        /// </returns>
        /// <exception cref="ArgumentNullException">
        /// <paramref name="configuration"/> is <c>null</c>.
        /// </exception>
        public static IConfiguration GetSection(this IConfiguration configuration, string prefix)
        {
            if (configuration == null) throw new ArgumentNullException(nameof(configuration));

            return string.IsNullOrEmpty(prefix) ? configuration : new Section(configuration, prefix + ":");
        }

        /// <summary>
        /// Reads a <typeparamref name="T"/> from the configuration: a scalar from the key
        /// <paramref name="section"/>, a collection from its indexed keys, or a settings object created and
        /// filled by <see cref="Bind"/>.
        /// </summary>
        /// <remarks>
        /// <para>
        /// <b>A scalar</b> — a primitive, an enum, <c>string</c>, <c>decimal</c>, <see cref="Guid"/>,
        /// <see cref="TimeSpan"/>, <see cref="DateTime"/>, <see cref="DateTimeOffset"/>, <see cref="Uri"/>, or
        /// a <see cref="Nullable{T}"/> of one — is the value at <paramref name="section"/>, converted as
        /// <see cref="Bind"/> converts a member: <c>Get&lt;int&gt;("Save:Slot")</c>. An absent key gives
        /// <c>default(T)</c>: <c>0</c>, <c>false</c>, <c>null</c>.
        /// </para>
        /// <para>
        /// <b>A collection</b> — a one-dimensional array, a <see cref="List{T}"/> or one of its generic
        /// interfaces, or a concrete collection class — is built from <c>section:0</c>, <c>section:1</c>, …,
        /// as <see cref="Bind"/> builds a collection member; with no such key it is empty.
        /// </para>
        /// <para>
        /// <b>Anything else</b> is a settings type: a class with a public parameterless constructor, or a
        /// struct. It is created and bound, and always returned, never <c>null</c>: a section with no keys at
        /// all yields a default-constructed object, so field initialisers stand as the defaults. A
        /// <c>UnityEngine.Object</c> cannot be created this way; create it the Unity way and call
        /// <see cref="Bind"/> on it.
        /// </para>
        /// </remarks>
        /// <typeparam name="T">A scalar, a collection, or a settings type, as above.</typeparam>
        /// <param name="configuration">The map to read.</param>
        /// <param name="section">
        /// The key of a scalar, or the path to bind a collection or settings object from, without a trailing
        /// <c>:</c>. <c>null</c> or empty binds from the root, which a scalar cannot do.
        /// </param>
        /// <returns>
        /// The value read, or for a collection or settings type a new instance on every call. A nested settings
        /// object it binds is a shallow copy of the one the initialisers put there, as <see cref="Bind"/>
        /// describes.
        /// </returns>
        /// <exception cref="ArgumentNullException">
        /// <paramref name="configuration"/> is <c>null</c>.
        /// </exception>
        /// <exception cref="ArgumentException">
        /// <typeparamref name="T"/> is a scalar and <paramref name="section"/> is <c>null</c> or empty, so there
        /// is no key to read.
        /// </exception>
        /// <exception cref="ConfigurationException">
        /// A configured value cannot be read as its type; or <typeparamref name="T"/>, or a nested settings
        /// type that has to be created, has no public parameterless constructor or is a
        /// <c>UnityEngine.Object</c>. The message names the key and the type.
        /// </exception>
        public static T Get<T>(this IConfiguration configuration, string section = null)
        {
            if (configuration == null) throw new ArgumentNullException(nameof(configuration));

            var type = typeof(T);
            var key = section ?? string.Empty;

            if (Shapes.IsScalar(type))
            {
                if (key.Length == 0)
                {
                    throw new ArgumentException(
                        "Get<" + TypeNames.Display(type) + "> reads one value, so it needs the key to read it from.",
                        nameof(section));
                }

                var raw = configuration[key];
                return raw == null ? default(T) : (T)Convert(raw, type, key);
            }

            Type element;
            if (Shapes.TryGetCollectionElement(type, out element))
            {
                object collection;
                return (T)(TryBindCollection(configuration, key, type, element, out collection)
                    ? collection
                    : NewCollection(type, element, new List<object>()));
            }

            var underlying = Nullable.GetUnderlyingType(type) ?? type;
            var target = underlying.IsValueType ? Activator.CreateInstance(underlying) : Create(underlying);
            configuration.Bind(section, target);
            return (T)target;
        }

        /// <summary>
        /// Fills the public read/write fields and properties of an existing object from the keys under
        /// <paramref name="section"/>, recursing into nested settings objects and collections.
        /// </summary>
        /// <remarks>
        /// <para>
        /// <b>What is bound.</b> Public instance fields that are neither <c>readonly</c> nor <c>const</c>,
        /// and public instance properties that have a public getter, a public setter, and no index
        /// parameters. Everything else — private, static, get-only, indexed — is invisible here, and so are
        /// the members declared by classes in the <c>UnityEngine</c> namespace or below it (<c>name</c>,
        /// <c>hideFlags</c>, <c>enabled</c>, … that a <c>ScriptableObject</c> or <c>MonoBehaviour</c>
        /// inherits), which are engine state rather than settings. Classes Unity ships in other namespaces
        /// (<c>TMPro</c>, <c>Unity.*</c>) are not recognised and bind like any class. Unity's value types
        /// (<c>Vector3</c>, <c>Color</c>, <c>Rect</c>) are data and bind like any struct. A member's key is
        /// <c>section:MemberName</c>, matched however the source compares keys (case-insensitively, for
        /// <see cref="ConfigurationManager"/>).
        /// </para>
        /// <para>
        /// <b>A missing key changes nothing.</b> The member keeps whatever it already held, so a field
        /// initialiser is the default and a partial override file is a legitimate one. It also means
        /// <see cref="Bind"/> can be called repeatedly with different sections to layer values onto one
        /// object.
        /// </para>
        /// <para>
        /// <b>Nested settings objects.</b> A member with no key of its own, whose type is a non-abstract class
        /// other than <c>string</c> or a struct (scalars and sequences aside), is bound from
        /// <c>section:MemberName:…</c> when at least one such key exists. A plain settings object the member
        /// holds is copied — a shallow <c>MemberwiseClone</c> — and the copy is bound and assigned, so the
        /// copy's unconfigured members keep the held object's values and the held object's own fields are not
        /// changed, even when it is a static default or shared with another member. Being shallow, the copy
        /// shares whatever the held object references: binding a nested object under it copies that one in
        /// turn, but state a property setter writes through to a shared inner object does change. An object
        /// that owns a resource is bound in place instead, never copied: a <c>UnityEngine.Object</c>, any other
        /// class Unity declares or derives from (<c>AnimationCurve</c>, <c>Gradient</c>; their own members are
        /// not bound anyway), and any class with a finalizer. The member's declared type decides whether it is
        /// a nested settings object at all (an interface or abstract class is not); for one that is, what it
        /// actually holds decides how it is bound, so an <c>object</c> member holding a string, an array, a
        /// delegate or a sequence is left alone. A <c>null</c>
        /// class member gets a new instance first, which needs a public parameterless constructor. A struct
        /// member is bound into a copy that is then written back, and a <see cref="Nullable{T}"/> struct that
        /// is <c>null</c> starts from <c>default</c>. With no key under it the member is left alone.
        /// </para>
        /// <para>
        /// <b>Collections.</b> A one-dimensional array, a <see cref="List{T}"/> or one of its generic
        /// interfaces (<see cref="IEnumerable{T}"/>, <see cref="IReadOnlyCollection{T}"/>,
        /// <see cref="IReadOnlyList{T}"/>, <see cref="ICollection{T}"/>, <see cref="IList{T}"/>; not the
        /// non-generic ones), or a concrete class with a public parameterless constructor that implements
        /// <see cref="ICollection{T}"/> (a <see cref="HashSet{T}"/>, say) is built from the indexed keys
        /// <c>section:MemberName:0</c>, <c>:1</c>, … — the shape
        /// <see cref="ConfigurationManagerExtensions.AddJson"/> gives a JSON array and
        /// <see cref="ConfigurationManagerExtensions.AddObject"/> gives a list. Elements follow the numeric
        /// order of the indices and skip a missing one, and each is a scalar, a settings object or a nested
        /// collection, bound by the same rules. The new collection <i>replaces</i> the member's value: a
        /// configured list is the whole list, not an addition to the default one. With no indexed key, or
        /// when no element of the element type can be read from the indexed keys (an interface element type,
        /// say), the member is left alone. A dictionary is not bound; read a map through
        /// <see cref="GetSection"/>.
        /// </para>
        /// <para>
        /// <b>A key on a member that is not a scalar</b> — plain <c>Servers</c> for a list — goes to the
        /// conversion below and fails there, loudly, rather than being ignored. A member declared
        /// <c>object</c> is the exception: it takes the raw string.
        /// </para>
        /// <para>
        /// <b>Conversion.</b> <c>string</c> is taken verbatim; an enum parses by name, case-insensitively,
        /// or by its underlying number; a <see cref="Guid"/> parses by shape, no culture involved;
        /// <see cref="TimeSpan"/>, <see cref="DateTime"/> and <see cref="DateTimeOffset"/> parse under
        /// <see cref="CultureInfo.InvariantCulture"/>, the two date types with
        /// <see cref="DateTimeStyles.RoundtripKind"/>; a <see cref="Uri"/> is built as
        /// <see cref="UriKind.RelativeOrAbsolute"/>; anything else goes through
        /// <see cref="System.Convert.ChangeType(object, Type, IFormatProvider)"/>, again invariant. Culture
        /// is pinned on purpose: a configuration file must not read differently on a device set to a
        /// comma-decimal locale.
        /// </para>
        /// <para>
        /// <b>Nullable members.</b> A <see cref="Nullable{T}"/> is converted as its underlying type.
        /// Nullability describes the member's own domain, not absence — absence is already spelled "no
        /// key", and an empty string is not a null, it is a value that fails to parse.
        /// </para>
        /// <para>
        /// <b>Failure is loud, and not transactional.</b> An unparseable value throws, quoting the value,
        /// the key and the target type, with the underlying parse exception as the inner one; members
        /// bound before it keep their new values.
        /// </para>
        /// </remarks>
        /// <param name="configuration">The map to read.</param>
        /// <param name="section">
        /// The path to bind from, without a trailing <c>:</c>; <c>null</c> or empty binds from the root.
        /// </param>
        /// <param name="target">
        /// The object to fill, mutated in place. A boxed struct is filled in its box.
        /// </param>
        /// <exception cref="ArgumentNullException">
        /// <paramref name="configuration"/> or <paramref name="target"/> is <c>null</c>.
        /// </exception>
        /// <exception cref="ConfigurationException">
        /// A configured value cannot be read as its member's type, or a nested settings type that has to be
        /// created has no public parameterless constructor or is a <c>UnityEngine.Object</c>.
        /// </exception>
        public static void Bind(this IConfiguration configuration, string section, object target)
        {
            if (configuration == null) throw new ArgumentNullException(nameof(configuration));
            if (target == null) throw new ArgumentNullException(nameof(target));

            var prefix = string.IsNullOrEmpty(section) ? string.Empty : section + ":";
            var members = BindableMembers.Get(target.GetType());

            for (var i = 0; i < members.Length; i++)
            {
                var member = members[i];
                var key = prefix + member.Name;

                object value;
                if (TryBindValue(configuration, key, member.Type, () => member.GetValue(target), out value))
                    member.SetValue(target, value);
            }
        }

        /// Reads the value of type `type` at `key`: the key's own value, else (for a collection or a settings
        /// object) the keys under it. False when there is nothing to read, so the caller leaves its value
        /// alone. `current` supplies the value a settings object is bound into, read only when needed.
        private static bool TryBindValue(IConfiguration configuration, string key, Type type, Func<object> current,
            out object value)
        {
            var raw = configuration[key];
            if (raw != null)
            {
                value = Convert(raw, type, key);
                return true;
            }

            value = null;
            var underlying = Nullable.GetUnderlyingType(type) ?? type;
            if (Shapes.IsScalar(underlying)) return false;

            Type element;
            if (Shapes.TryGetCollectionElement(underlying, out element))
                return TryBindCollection(configuration, key, underlying, element, out value);

            if (!Shapes.IsComposite(underlying) || !HasChildren(configuration, key)) return false;

            var instance = current == null ? null : current();
            if (instance == null)
            {
                instance = underlying.IsValueType ? Activator.CreateInstance(underlying) : Create(underlying);
            }
            else if (!underlying.IsValueType)
            {
                // Decide by what the member holds, not by how it is declared: an object member may hold a string.
                var held = instance.GetType();
                if (!Shapes.IsComposite(held)) return false;

                // A held instance may be shared (a static default, two members), so a plain settings object is
                // bound into a copy. Never copy what owns a resource: a class with a finalizer, or anything
                // Unity's (AnimationCurve, Gradient and GUIStyle hold a native pointer their finalizer frees).
                if (CanCopy(held)) instance = ShallowCopy(instance);
            }

            configuration.Bind(key, instance);
            value = instance;
            return true;
        }

        private static bool CanCopy(Type type)
        {
            if (UnityTypes.IsEngineObject(type) || UnityTypes.HasEngineClass(type)) return false;

            // A finalizer is an override of Object.Finalize(), declared on the type or a base class. A helper
            // such as Finalize(bool) or Finalize<T>(), or a Finalize() declared with 'new', is not one. The
            // methods are enumerated rather than looked up by name, which can be ambiguous.
            const BindingFlags declared =
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly;
            for (var t = type; t != null && t != typeof(object); t = t.BaseType)
            {
                foreach (var method in t.GetMethods(declared))
                {
                    if (method.Name != "Finalize" || method.IsGenericMethodDefinition ||
                        method.GetParameters().Length != 0)
                    {
                        continue;
                    }

                    if (method.GetBaseDefinition().DeclaringType == typeof(object)) return false;
                }
            }

            return true;
        }

        private static readonly double NegativeZero = BitConverter.Int64BitsToDouble(long.MinValue);

        private static readonly MethodInfo CloneMethod =
            typeof(object).GetMethod("MemberwiseClone", BindingFlags.Instance | BindingFlags.NonPublic);

        private static object ShallowCopy(object instance) => CloneMethod.Invoke(instance, null);

        private static bool TryBindCollection(IConfiguration configuration, string key, Type type, Type element,
            out object value)
        {
            value = null;
            var head = key.Length == 0 ? string.Empty : key + ":";
            var indices = new SortedSet<int>();

            foreach (var pair in configuration)
            {
                if (!pair.Key.StartsWith(head, StringComparison.OrdinalIgnoreCase)) continue;

                var end = pair.Key.IndexOf(':', head.Length);
                var segment = end < 0
                    ? pair.Key.Substring(head.Length)
                    : pair.Key.Substring(head.Length, end - head.Length);
                int index;
                if (int.TryParse(segment, NumberStyles.None, CultureInfo.InvariantCulture, out index) &&
                    segment == index.ToString(CultureInfo.InvariantCulture))
                {
                    indices.Add(index);
                }
            }

            if (indices.Count == 0) return false;

            var items = new List<object>(indices.Count);
            foreach (var index in indices)
            {
                object item;
                if (TryBindValue(configuration, head + index.ToString(CultureInfo.InvariantCulture), element, null,
                        out item))
                {
                    items.Add(item);
                }
            }

            if (items.Count == 0) return false; // indexed keys, but no element of this type could be read from them

            value = NewCollection(type, element, items);
            return true;
        }

        private static object NewCollection(Type type, Type element, List<object> items)
        {
            if (type.IsArray)
            {
                var array = Array.CreateInstance(element, items.Count);
                for (var i = 0; i < items.Count; i++) array.SetValue(items[i], i);
                return array;
            }

            if (type.IsInterface)
            {
                var list = (IList)Activator.CreateInstance(typeof(List<>).MakeGenericType(element));
                for (var i = 0; i < items.Count; i++) list.Add(items[i]);
                return list;
            }

            var collection = Activator.CreateInstance(type);
            var add = typeof(ICollection<>).MakeGenericType(element).GetMethod("Add");
            for (var i = 0; i < items.Count; i++) add.Invoke(collection, new[] { items[i] });
            return collection;
        }

        private static bool HasChildren(IConfiguration configuration, string key)
        {
            var head = key + ":";
            foreach (var pair in configuration)
            {
                if (pair.Key.StartsWith(head, StringComparison.OrdinalIgnoreCase)) return true;
            }

            return false;
        }

        internal static object Create(Type type)
        {
            if (UnityTypes.IsEngineObject(type))
            {
                throw new ConfigurationException(
                    "'" + TypeNames.Display(type) + "' cannot be created from configuration: it is a " +
                    "UnityEngine.Object, which only Unity can create. Create it the Unity way " +
                    "(ScriptableObject.CreateInstance, AddComponent) and fill it with Bind.");
            }

            var constructor = type.IsAbstract ? null : type.GetConstructor(Type.EmptyTypes);
            if (constructor == null)
            {
                throw new ConfigurationException(
                    "'" + TypeNames.Display(type) + "' cannot be bound from configuration: it is abstract or " +
                    "has no public parameterless constructor. Settings types are plain data.");
            }

            return constructor.Invoke(null);
        }

        private static object Convert(string raw, Type target, string key)
        {
            var underlying = Nullable.GetUnderlyingType(target) ?? target;

            try
            {
                if (underlying == typeof(string)) return raw;
                if (underlying.IsEnum) return Enum.Parse(underlying, raw, true);
                if (underlying == typeof(Guid)) return Guid.Parse(raw);
                if (underlying == typeof(TimeSpan)) return TimeSpan.Parse(raw, CultureInfo.InvariantCulture);
                if (underlying == typeof(DateTime))
                    return DateTime.Parse(raw, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind);
                if (underlying == typeof(DateTimeOffset))
                    return DateTimeOffset.Parse(raw, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind);
                if (underlying == typeof(Uri)) return new Uri(raw, UriKind.RelativeOrAbsolute);

                var result = System.Convert.ChangeType(raw, underlying, CultureInfo.InvariantCulture);
                // Mono parses "-0" as +0; keep the sign that AddObject wrote.
                if (underlying == typeof(double) && (double)result == 0 && raw.TrimStart().StartsWith("-"))
                    return NegativeZero;
                if (underlying == typeof(float) && (float)result == 0 && raw.TrimStart().StartsWith("-"))
                    return (float)NegativeZero;
                return result;
            }
            catch (Exception exception)
            {
                throw new ConfigurationException(
                    "The configuration value '" + raw + "' at key '" + key + "' cannot be read as '" +
                    TypeNames.Display(target) + "'.", exception);
            }
        }

        private sealed class Section : IConfiguration
        {
            private readonly IConfiguration _source;
            private readonly string _prefix;

            internal Section(IConfiguration source, string prefix)
            {
                _source = source;
                _prefix = prefix;
            }

            public string this[string key] => _source[_prefix + key];

            public IEnumerator<KeyValuePair<string, string>> GetEnumerator()
            {
                foreach (var pair in _source)
                {
                    if (!pair.Key.StartsWith(_prefix, StringComparison.OrdinalIgnoreCase)) continue;

                    yield return new KeyValuePair<string, string>(
                        pair.Key.Substring(_prefix.Length), pair.Value);
                }
            }

            IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
        }
    }
}
