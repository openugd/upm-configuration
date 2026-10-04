using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace OpenUGD
{
    /// <summary>
    /// The provider side of <see cref="ConfigurationManager"/>: three ways to flatten a source into
    /// <c>:</c>-separated keys — a dictionary, a JSON document, or a settings object.
    /// </summary>
    /// <remarks>
    /// <para>
    /// All three write the <i>provider</i> layer, so an explicit <c>configuration["Key"] = "value"</c> set
    /// through the indexer keeps winning however late a provider runs. Between themselves the last writer
    /// of a key wins, which makes call order the precedence order: defaults first, environment last.
    /// </para>
    /// <para>
    /// Each returns the same <see cref="ConfigurationManager"/>, so several sources chain in one
    /// expression.
    /// </para>
    /// <para>
    /// <b>None of this is transactional.</b> Keys are written as the source is walked, so a malformed
    /// document or an over-deep graph leaves everything read up to the failure already in the map. Load
    /// into a throwaway <see cref="ConfigurationManager"/> first if a half-applied source would be worse
    /// than no source at all.
    /// </para>
    /// </remarks>
    public static class ConfigurationManagerExtensions
    {
        private const int MaxDepth = 32;

        /// <summary>
        /// Copies key/value pairs in as they are, optionally under a prefix — the provider for values you
        /// already hold as strings: a command line, environment variables, a remote-config payload.
        /// </summary>
        /// <remarks>
        /// Keys are taken verbatim, so any nesting must already be spelled with <c>:</c>. A <c>null</c>
        /// value is stored and reads back as absent, which is how one source can mask a default written by
        /// an earlier one. Duplicate keys within <paramref name="values"/> resolve to the last enumerated.
        /// </remarks>
        /// <param name="configuration">The manager to write into.</param>
        /// <param name="values">The pairs to copy. Enumerated exactly once, and not retained.</param>
        /// <param name="prefix">
        /// Prepended to each key as <c>prefix:key</c>; <c>null</c> or empty writes the keys unprefixed.
        /// </param>
        /// <returns><paramref name="configuration"/>, for chaining.</returns>
        /// <exception cref="ArgumentNullException">
        /// <paramref name="configuration"/> or <paramref name="values"/> is <c>null</c>.
        /// </exception>
        /// <exception cref="ArgumentException">A pair has a <c>null</c> key.</exception>
        /// <exception cref="InvalidOperationException">
        /// The manager is read-only because its context has been built.
        /// </exception>
        public static ConfigurationManager AddDictionary(this ConfigurationManager configuration,
            IEnumerable<KeyValuePair<string, string>> values, string prefix = null)
        {
            if (configuration == null) throw new ArgumentNullException(nameof(configuration));
            if (values == null) throw new ArgumentNullException(nameof(values));
            configuration.ThrowIfFrozen();

            foreach (var pair in values)
            {
                if (pair.Key == null) throw new ArgumentException("A configuration key cannot be null.", nameof(values));
                configuration.SetProviderValue(Join(prefix ?? string.Empty, pair.Key), pair.Value);
            }

            return configuration;
        }

        /// <summary>
        /// Parses a JSON document and flattens it into keys: objects contribute their property names,
        /// arrays their indices, and only the leaves become values.
        /// </summary>
        /// <remarks>
        /// <para>
        /// <c>{"Save":{"Slot":3,"Tags":["a","b"]}}</c> becomes <c>Save:Slot</c> = <c>3</c>,
        /// <c>Save:Tags:0</c> = <c>a</c>, <c>Save:Tags:1</c> = <c>b</c>. An empty property name is a segment
        /// like any other: <c>{"":1}</c> is the key <c>""</c>. Scalars are stored as their source
        /// text — a number keeps its exact spelling, unrounded and unnormalised — and are interpreted only
        /// when something binds them. Strings are unescaped. An empty object or array contributes no key
        /// at all, not even an empty one.
        /// </para>
        /// <para>
        /// A JSON <c>null</c> is stored as <c>null</c>, so it reads back as absent rather than as an empty
        /// string: the same single rule for "not configured", with no third state.
        /// </para>
        /// <para>
        /// <b>A small hand-written parser</b>, so that the package takes no dependency on a JSON library,
        /// and a strict one (RFC 8259): no comments, no trailing commas, no unquoted or single-quoted property
        /// names, numbers only in JSON's own grammar (so <c>+1</c>, <c>01</c>, <c>.5</c>, <c>NaN</c> and
        /// <c>Infinity</c> are malformed), no raw control characters inside a string, and only space, tab,
        /// line feed and carriage return as whitespace. It rejects any content after the top-level value, and
        /// refuses a document nested deeper than 32 levels. A leading byte-order mark (U+FEFF), which a text
        /// read without decoding it — a <c>TextAsset</c>'s text, say — can still carry, is skipped.
        /// </para>
        /// <para>
        /// A scalar at the root has no name of its own, so it needs <paramref name="prefix"/> to give it
        /// one; an object or array at the root does not.
        /// </para>
        /// </remarks>
        /// <param name="configuration">The manager to write into.</param>
        /// <param name="json">The document text, in full. An empty string is malformed, not empty.</param>
        /// <param name="prefix">
        /// Prepended to every key as <c>prefix:...</c>, which is how two files load into separate sections;
        /// <c>null</c> or empty roots the document at the top level.
        /// </param>
        /// <returns><paramref name="configuration"/>, for chaining.</returns>
        /// <exception cref="ArgumentNullException">
        /// <paramref name="configuration"/> or <paramref name="json"/> is <c>null</c>.
        /// </exception>
        /// <exception cref="ConfigurationException">
        /// The document is malformed, nested too deeply, carries content after the top-level value, or is a
        /// bare scalar with no <paramref name="prefix"/> to name it. The message carries the character
        /// offset and what was expected there. Keys parsed before the failure are already written.
        /// </exception>
        /// <exception cref="InvalidOperationException">
        /// The manager is read-only because its context has been built.
        /// </exception>
        public static ConfigurationManager AddJson(this ConfigurationManager configuration, string json,
            string prefix = null)
        {
            if (configuration == null) throw new ArgumentNullException(nameof(configuration));
            if (json == null) throw new ArgumentNullException(nameof(json));
            configuration.ThrowIfFrozen();

            var index = json.Length != 0 && json[0] == '\uFEFF' ? 1 : 0;
            // The JSON walk marks "no path yet" with null, so that an empty property name ("") is a key like
            // any other rather than the root.
            ReadValue(configuration, json, ref index, string.IsNullOrEmpty(prefix) ? null : prefix, 0);
            SkipWhitespace(json, ref index);
            if (index != json.Length) throw Malformed(index, "unexpected trailing content");

            return configuration;
        }

        /// <summary>
        /// Flattens an object graph into keys by walking its public read/write members — the inverse of
        /// <see cref="ConfigurationExtensions.Bind"/>, for defaults expressed as a C# object rather than as
        /// a file.
        /// </summary>
        /// <remarks>
        /// <para>
        /// <b>What is walked</b> is exactly what <see cref="ConfigurationExtensions.Bind"/> would fill:
        /// public instance fields that are neither <c>readonly</c> nor <c>const</c>, and public instance
        /// properties with a public getter, a public setter and no index parameters. A get-only property
        /// is not written out, so a computed value cannot leak into configuration and come back as data.
        /// Members declared by classes in the <c>UnityEngine</c> namespace or below it are not walked either,
        /// so a <c>ScriptableObject</c> contributes its own public fields and properties but not <c>name</c>
        /// or <c>hideFlags</c>; Unity's value types (<c>Vector3</c>, <c>Color</c>) are walked like any struct,
        /// and classes Unity ships in other namespaces (<c>TMPro</c>) like any class.
        /// </para>
        /// <para>
        /// <b>Shape.</b> Members nest as <c>Path:Member</c>, a struct's as well as a class's; anything
        /// <see cref="IEnumerable"/> — an array, a list — nests as <c>Path:0</c>, <c>Path:1</c>, which
        /// <see cref="ConfigurationExtensions.Bind"/> reads back into an array or list member. Scalars
        /// (primitives, enums, <c>string</c>, <c>decimal</c>, <see cref="Guid"/>, <see cref="TimeSpan"/>,
        /// <see cref="DateTime"/>, <see cref="DateTimeOffset"/>, <see cref="Uri"/>) are formatted under
        /// <see cref="CultureInfo.InvariantCulture"/>, and a <c>bool</c> as <c>true</c>/<c>false</c> rather
        /// than .NET's <c>True</c>/<c>False</c>, a <see cref="DateTime"/> or <see cref="DateTimeOffset"/> in the
        /// round-trip pattern <c>"O"</c>, and a <c>float</c> or <c>double</c> with every digit it needs, so that
        /// what is written back round-trips through <see cref="ConfigurationExtensions.Bind"/>, a negative zero
        /// included. A type whose settable members overlap is written out through every one of them, and they
        /// are bound back one after another — public fields before properties, a class's own members before
        /// the ones it inherits — so the last one applied wins: for example Unity's <c>Rect</c>,
        /// <c>RectInt</c>, <c>Bounds</c> and <c>BoundsInt</c>, whose <c>x</c>, <c>position</c>, <c>min</c> and
        /// <c>center</c> write the same state, <c>Quaternion</c>, whose <c>eulerAngles</c> writes its
        /// <c>x</c>…<c>w</c>, and <c>Resolution</c>, whose obsolete <c>refreshRate</c> overwrites
        /// <c>refreshRateRatio</c> with a whole number of hertz. Configure such a member through one set of keys,
        /// and do not use <c>AddObject</c> for its defaults.
        /// </para>
        /// <para>
        /// <b>A null member writes nothing.</b> It contributes no key, so it neither creates an entry nor
        /// clears one an earlier provider wrote. Mask a value deliberately through the indexer instead.
        /// </para>
        /// <para>
        /// <b>No cycle detection.</b> The walk is bounded by depth alone, so a graph that references itself
        /// hits the 32-level limit and throws rather than hanging. Configuration is a flat boundary format:
        /// pass a settings object, not a live model.
        /// </para>
        /// <para>
        /// A <see cref="Dictionary{TKey,TValue}"/> is a sequence of
        /// <see cref="KeyValuePair{TKey,TValue}"/> whose members are read-only, so it flattens to nothing
        /// at all. Use <see cref="AddDictionary"/> for a map.
        /// </para>
        /// </remarks>
        /// <param name="configuration">The manager to write into.</param>
        /// <param name="value">
        /// The object to flatten. May itself be a scalar, in which case <paramref name="prefix"/> is
        /// required.
        /// </param>
        /// <param name="prefix">
        /// Prepended to every key as <c>prefix:...</c>; <c>null</c> or empty puts the object's own members
        /// at the top level.
        /// </param>
        /// <returns><paramref name="configuration"/>, for chaining.</returns>
        /// <exception cref="ArgumentNullException">
        /// <paramref name="configuration"/> or <paramref name="value"/> is <c>null</c>.
        /// </exception>
        /// <exception cref="ArgumentException">
        /// <paramref name="value"/> is a scalar and no <paramref name="prefix"/> was given, so there is no
        /// key to store it under.
        /// </exception>
        /// <exception cref="ConfigurationException">
        /// The graph nests more than 32 levels deep, which in practice means it contains a cycle. The
        /// message names the path reached.
        /// </exception>
        /// <exception cref="InvalidOperationException">
        /// The manager is read-only because its context has been built.
        /// </exception>
        public static ConfigurationManager AddObject(this ConfigurationManager configuration, object value,
            string prefix = null)
        {
            if (configuration == null) throw new ArgumentNullException(nameof(configuration));
            if (value == null) throw new ArgumentNullException(nameof(value));
            configuration.ThrowIfFrozen();

            Flatten(configuration, value, prefix ?? string.Empty, 0);
            return configuration;
        }

        // ---------------------------------------------------------------- objects

        private static void Flatten(ConfigurationManager target, object value, string path, int depth)
        {
            if (value == null) return;

            var type = value.GetType();

            if (Shapes.IsScalar(type))
            {
                if (path.Length == 0)
                {
                    throw new ArgumentException(
                        "AddObject needs a prefix when the value is a scalar, otherwise it has no key.",
                        nameof(value));
                }

                target.SetProviderValue(path, Format(value));
                return;
            }

            if (depth >= MaxDepth)
            {
                throw new ConfigurationException(
                    "AddObject stopped at '" + path + "': the object graph is more than " + MaxDepth +
                    " levels deep. Configuration is a flat boundary format - pass a settings object, not " +
                    "a live object graph.");
            }

            var sequence = value as IEnumerable;
            if (sequence != null)
            {
                var index = 0;
                foreach (var item in sequence)
                {
                    Flatten(target, item, Join(path, index.ToString(CultureInfo.InvariantCulture)), depth + 1);
                    index++;
                }

                return;
            }

            var members = BindableMembers.Get(type);
            for (var i = 0; i < members.Length; i++)
            {
                Flatten(target, members[i].GetValue(value), Join(path, members[i].Name), depth + 1);
            }
        }

        private static string Format(object value)
        {
            if (value is bool) return (bool)value ? "true" : "false";
            // "O" keeps fractional seconds and the kind or offset, which the general pattern drops.
            if (value is DateTime) return ((DateTime)value).ToString("O", CultureInfo.InvariantCulture);
            if (value is DateTimeOffset) return ((DateTimeOffset)value).ToString("O", CultureInfo.InvariantCulture);
            // The default pattern drops digits on Mono (0.3f * 3 prints as 0.9); "R" does not, except for the few
            // doubles where .NET Framework's "R" is known to miss, which the check sends to "G17".
            if (value is float) return RoundTrip((float)value);
            if (value is double) return RoundTrip((double)value);

            var formattable = value as IFormattable;
            return formattable != null
                ? formattable.ToString(null, CultureInfo.InvariantCulture)
                : value.ToString();
        }

        private static string RoundTrip(float value)
        {
            if (value == 0 && BitConverter.DoubleToInt64Bits(value) < 0) return "-0"; // Mono prints -0f as "0"
            var text = value.ToString("R", CultureInfo.InvariantCulture);
            return float.Parse(text, NumberStyles.Float, CultureInfo.InvariantCulture).Equals(value)
                ? text
                : value.ToString("G9", CultureInfo.InvariantCulture);
        }

        private static string RoundTrip(double value)
        {
            if (value == 0 && BitConverter.DoubleToInt64Bits(value) < 0) return "-0"; // Mono prints -0.0 as "0"
            var text = value.ToString("R", CultureInfo.InvariantCulture);
            return double.Parse(text, NumberStyles.Float, CultureInfo.InvariantCulture).Equals(value)
                ? text
                : value.ToString("G17", CultureInfo.InvariantCulture);
        }

        // ---------------------------------------------------------------- json

        private static void ReadValue(ConfigurationManager target, string json, ref int index, string path,
            int depth)
        {
            SkipWhitespace(json, ref index);
            if (index >= json.Length) throw Malformed(index, "unexpected end of document");
            if ((json[index] == '{' || json[index] == '[') && depth >= MaxDepth)
                throw Malformed(index, "the document is nested more than 32 levels deep");

            switch (json[index])
            {
                case '{':
                    ReadObject(target, json, ref index, path, depth);
                    return;
                case '[':
                    ReadArray(target, json, ref index, path, depth);
                    return;
                case '"':
                    target.SetProviderValue(Key(path, index), ReadString(json, ref index));
                    return;
            }

            var start = index;
            while (index < json.Length && ",}] \t\r\n".IndexOf(json[index]) < 0) index++;
            if (index == start) throw Malformed(index, "expected a value");

            var literal = json.Substring(start, index - start);
            if (literal != "null" && literal != "true" && literal != "false" && !IsNumber(literal))
                throw Malformed(start, "'" + literal + "' is not a valid JSON value");

            // A JSON null is stored as null, which reads back as absent - one rule, no third state.
            target.SetProviderValue(Key(path, start), literal == "null" ? null : literal);
        }

        private static void ReadObject(ConfigurationManager target, string json, ref int index, string path,
            int depth)
        {
            index++; // '{'
            SkipWhitespace(json, ref index);
            if (index < json.Length && json[index] == '}')
            {
                index++;
                return;
            }

            while (true)
            {
                SkipWhitespace(json, ref index);
                if (index >= json.Length || json[index] != '"') throw Malformed(index, "expected a property name");

                var name = ReadString(json, ref index);
                SkipWhitespace(json, ref index);
                if (index >= json.Length || json[index] != ':') throw Malformed(index, "expected ':'");
                index++;

                ReadValue(target, json, ref index, JsonJoin(path, name), depth + 1);
                SkipWhitespace(json, ref index);

                if (index >= json.Length) throw Malformed(index, "unexpected end of document");
                if (json[index] == ',')
                {
                    index++;
                    continue;
                }

                if (json[index] == '}')
                {
                    index++;
                    return;
                }

                throw Malformed(index, "expected ',' or '}'");
            }
        }

        private static void ReadArray(ConfigurationManager target, string json, ref int index, string path,
            int depth)
        {
            index++; // '['
            SkipWhitespace(json, ref index);
            if (index < json.Length && json[index] == ']')
            {
                index++;
                return;
            }

            var element = 0;
            while (true)
            {
                ReadValue(target, json, ref index, JsonJoin(path, element.ToString(CultureInfo.InvariantCulture)),
                    depth + 1);
                element++;
                SkipWhitespace(json, ref index);

                if (index >= json.Length) throw Malformed(index, "unexpected end of document");
                if (json[index] == ',')
                {
                    index++;
                    continue;
                }

                if (json[index] == ']')
                {
                    index++;
                    return;
                }

                throw Malformed(index, "expected ',' or ']'");
            }
        }

        private static string ReadString(string json, ref int index)
        {
            index++; // opening quote
            var builder = new StringBuilder();

            while (index < json.Length)
            {
                var c = json[index++];
                if (c == '"') return builder.ToString();

                if (c < ' ') throw Malformed(index - 1, "a control character must be escaped inside a string");
                if (c != '\\')
                {
                    builder.Append(c);
                    continue;
                }

                if (index >= json.Length) break;
                var escape = json[index++];
                switch (escape)
                {
                    case '"': builder.Append('"'); break;
                    case '\\': builder.Append('\\'); break;
                    case '/': builder.Append('/'); break;
                    case 'b': builder.Append('\b'); break;
                    case 'f': builder.Append('\f'); break;
                    case 'n': builder.Append('\n'); break;
                    case 'r': builder.Append('\r'); break;
                    case 't': builder.Append('\t'); break;
                    case 'u':
                        if (index + 4 > json.Length) throw Malformed(index, "truncated \\u escape");
                        var code = 0;
                        for (var i = 0; i < 4; i++)
                        {
                            var digit = HexDigit(json[index + i]);
                            if (digit < 0)
                                throw Malformed(index + i, "'\\u' must be followed by four hexadecimal digits");
                            code = code * 16 + digit;
                        }

                        builder.Append((char)code);
                        index += 4;
                        break;
                    default: throw Malformed(index - 1, "unknown escape '\\" + escape + "'");
                }
            }

            throw Malformed(index, "unterminated string");
        }

        private static void SkipWhitespace(string json, ref int index)
        {
            while (index < json.Length && " \t\r\n".IndexOf(json[index]) >= 0) index++;
        }

        private static int HexDigit(char c)
        {
            if (c >= '0' && c <= '9') return c - '0';
            if (c >= 'a' && c <= 'f') return c - 'a' + 10;
            if (c >= 'A' && c <= 'F') return c - 'A' + 10;
            return -1;
        }

        /// RFC 8259: -? (0 | [1-9][0-9]*) (. [0-9]+)? ([eE] [+-]? [0-9]+)?
        private static bool IsNumber(string s)
        {
            var i = 0;
            if (i < s.Length && s[i] == '-') i++;
            if (i >= s.Length) return false;
            if (s[i] == '0') i++;
            else if (s[i] >= '1' && s[i] <= '9') Digits(s, ref i);
            else return false;

            if (i < s.Length && s[i] == '.')
            {
                i++;
                if (!Digits(s, ref i)) return false;
            }

            if (i < s.Length && (s[i] == 'e' || s[i] == 'E'))
            {
                i++;
                if (i < s.Length && (s[i] == '+' || s[i] == '-')) i++;
                if (!Digits(s, ref i)) return false;
            }

            return i == s.Length;
        }

        private static bool Digits(string s, ref int i)
        {
            var start = i;
            while (i < s.Length && s[i] >= '0' && s[i] <= '9') i++;
            return i > start;
        }

        private static string Key(string path, int index)
        {
            if (path != null) return path;

            throw Malformed(index,
                "the document root is a scalar, so it has no key - pass a prefix, or use an object at the root");
        }

        private static string Join(string path, string segment) =>
            path.Length == 0 ? segment : path + ":" + segment;

        private static string JsonJoin(string path, string segment) => path == null ? segment : path + ":" + segment;

        private static ConfigurationException Malformed(int index, string reason) =>
            new ConfigurationException(
                "The JSON configuration is malformed at character " + index + ": " + reason + ".");
    }
}
