using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using System.Text;

namespace OpenUGD
{
    /// A public, writable field or property: what Bind fills and AddObject reads.
    internal readonly struct BindableMember
    {
        private readonly FieldInfo _field;
        private readonly PropertyInfo _property;

        internal readonly Type Type;
        internal readonly string Name;

        internal BindableMember(FieldInfo field, PropertyInfo property)
        {
            _field = field;
            _property = property;
            Type = field != null ? field.FieldType : property.PropertyType;
            Name = field != null ? field.Name : property.Name;
        }

        internal void SetValue(object target, object value)
        {
            if (_field != null) _field.SetValue(target, value);
            else _property.SetValue(target, value, null);
        }

        internal object GetValue(object target) =>
            _field != null ? _field.GetValue(target) : _property.GetValue(target, null);
    }

    internal static class BindableMembers
    {
        private static readonly object Gate = new object();
        private static readonly Dictionary<Type, BindableMember[]> Cache = new Dictionary<Type, BindableMember[]>();
        private static readonly BindableMember[] None = new BindableMember[0];

        /// The public, writable fields and properties a configuration section can be bound onto: instance
        /// fields that are neither readonly nor const, and instance properties with a public getter, a
        /// public setter and no index parameters. Members declared by Unity's own classes (name, hideFlags,
        /// enabled, tag... that a ScriptableObject or MonoBehaviour inherits) are engine state, not settings,
        /// and are left out; Unity's value types (Vector3, Color, Rect...) are data and bind like any struct.
        /// Cached per type.
        internal static BindableMember[] Get(Type type)
        {
            lock (Gate)
            {
                BindableMember[] members;
                if (Cache.TryGetValue(type, out members)) return members;

                List<BindableMember> found = null;

                var fields = type.GetFields(BindingFlags.Instance | BindingFlags.Public);
                for (var i = 0; i < fields.Length; i++)
                {
                    var field = fields[i];
                    if (field.IsInitOnly || field.IsLiteral || UnityTypes.IsEngineClass(field.DeclaringType)) continue;
                    (found ?? (found = new List<BindableMember>())).Add(new BindableMember(fields[i], null));
                }

                var properties = type.GetProperties(BindingFlags.Instance | BindingFlags.Public);
                for (var i = 0; i < properties.Length; i++)
                {
                    var property = properties[i];
                    if (property.GetIndexParameters().Length != 0) continue;
                    if (property.GetGetMethod() == null || property.GetSetMethod() == null) continue;
                    if (UnityTypes.IsEngineClass(property.DeclaringType)) continue;
                    (found ?? (found = new List<BindableMember>())).Add(new BindableMember(null, property));
                }

                members = found == null ? None : found.ToArray();
                Cache[type] = members;
                return members;
            }
        }
    }

    /// Unity's types, recognised by name: this assembly has no engine reference.
    internal static class UnityTypes
    {
        /// A class Unity declares (its namespace is UnityEngine or below it): Object, Component, Behaviour,
        /// MonoBehaviour, ScriptableObject... A struct there (Vector3, Color) is data, not engine state.
        internal static bool IsEngineClass(Type type)
        {
            var ns = type == null || type.IsValueType ? null : type.Namespace;
            return ns != null && (ns == "UnityEngine" || ns.StartsWith("UnityEngine.", StringComparison.Ordinal));
        }

        /// The type or one of its base types is a class Unity declares.
        internal static bool HasEngineClass(Type type)
        {
            for (var t = type; t != null; t = t.BaseType)
            {
                if (IsEngineClass(t)) return true;
            }

            return false;
        }

        /// UnityEngine.Object or a type derived from it: only Unity may create one.
        internal static bool IsEngineObject(Type type)
        {
            for (var t = type; t != null; t = t.BaseType)
            {
                if (t.FullName == "UnityEngine.Object") return true;
            }

            return false;
        }
    }

    /// The shapes configuration knows: a scalar is one value, a collection is indexed keys (Path:0, Path:1).
    internal static class Shapes
    {
        /// Stored as one string: primitives, enums, string, decimal, Guid, TimeSpan, DateTime, DateTimeOffset,
        /// Uri. A Nullable of one of these is a scalar too.
        internal static bool IsScalar(Type type)
        {
            type = Nullable.GetUnderlyingType(type) ?? type;
            return type.IsPrimitive || type.IsEnum || type == typeof(string) || type == typeof(decimal) ||
                   type == typeof(Guid) || type == typeof(TimeSpan) || type == typeof(DateTime) ||
                   type == typeof(DateTimeOffset) || type == typeof(Uri);
        }

        /// A sequence Bind can create and fill: a one-dimensional array; IEnumerable, IReadOnlyCollection,
        /// IReadOnlyList, ICollection or IList of T (filled as a List of T); or a concrete class with a public
        /// parameterless constructor that implements ICollection of exactly one T (HashSet, LinkedList, a
        /// List subclass). A dictionary is not one: its elements are key/value pairs.
        internal static bool TryGetCollectionElement(Type type, out Type element)
        {
            element = null;
            if (type == typeof(string)) return false;

            if (type.IsArray)
            {
                if (type.GetArrayRank() != 1) return false;
                element = type.GetElementType();
                return true;
            }

            if (type.IsInterface && type.IsGenericType)
            {
                var definition = type.GetGenericTypeDefinition();
                if (definition == typeof(IEnumerable<>) || definition == typeof(IReadOnlyCollection<>) ||
                    definition == typeof(IReadOnlyList<>) || definition == typeof(ICollection<>) ||
                    definition == typeof(IList<>))
                {
                    element = type.GetGenericArguments()[0];
                    return true;
                }

                return false;
            }

            if (!type.IsClass || type.IsAbstract || type.GetConstructor(Type.EmptyTypes) == null) return false;
            if (typeof(IDictionary).IsAssignableFrom(type)) return false;

            foreach (var candidate in type.GetInterfaces())
            {
                if (!candidate.IsGenericType) continue;
                var definition = candidate.GetGenericTypeDefinition();
                if (definition == typeof(IDictionary<,>) || definition == typeof(IReadOnlyDictionary<,>)) return false;
                if (definition != typeof(ICollection<>)) continue;
                if (element != null) return false; // two element types: ambiguous
                element = candidate.GetGenericArguments()[0];
            }

            return element != null;
        }

        /// A settings object Bind can fill member by member: a non-abstract class other than string that is
        /// not a sequence or a delegate, or a struct that is not a scalar. A Nullable struct counts as its
        /// struct.
        internal static bool IsComposite(Type type)
        {
            type = Nullable.GetUnderlyingType(type) ?? type;
            if (IsScalar(type) || typeof(IEnumerable).IsAssignableFrom(type)) return false;
            if (type.IsValueType) return !type.IsPointer;
            return type.IsClass && !type.IsAbstract && !typeof(Delegate).IsAssignableFrom(type);
        }
    }

    internal static class TypeNames
    {
        /// A type's name as a reader writes it: namespace-qualified, generics with angle brackets.
        internal static string Display(Type type)
        {
            if (type == null) return "<null>";
            if (!type.IsGenericType) return type.FullName ?? type.Name;

            var definition = type.GetGenericTypeDefinition().FullName ?? type.Name;
            var tick = definition.IndexOf('`');
            if (tick >= 0) definition = definition.Substring(0, tick);

            var builder = new StringBuilder(definition).Append('<');
            var arguments = type.GetGenericArguments();
            for (var i = 0; i < arguments.Length; i++)
            {
                if (i != 0) builder.Append(", ");
                builder.Append(Display(arguments[i]));
            }

            return builder.Append('>').ToString();
        }
    }
}
