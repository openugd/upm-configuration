using System;
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
        /// public setter and no index parameters. Cached per type.
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
                    if (fields[i].IsInitOnly || fields[i].IsLiteral) continue;
                    (found ?? (found = new List<BindableMember>())).Add(new BindableMember(fields[i], null));
                }

                var properties = type.GetProperties(BindingFlags.Instance | BindingFlags.Public);
                for (var i = 0; i < properties.Length; i++)
                {
                    var property = properties[i];
                    if (property.GetIndexParameters().Length != 0) continue;
                    if (property.GetGetMethod() == null || property.GetSetMethod() == null) continue;
                    (found ?? (found = new List<BindableMember>())).Add(new BindableMember(null, property));
                }

                members = found == null ? None : found.ToArray();
                Cache[type] = members;
                return members;
            }
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
