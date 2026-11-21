﻿using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text;

namespace SchemeTool
{
    class Program
    {
        static void Main(string[] args)
        {
            using (Stream stream = File.OpenRead(@".\GameData\Formats.dat"))
                GameRes.FormatCatalog.Instance.DeserializeScheme(stream);

            // Keep GARbro's complete native JSON export.
            using (Stream stream = File.Create(@".\GameData\Formats.json"))
                GameRes.FormatCatalog.Instance.SerializeSchemeJson(stream);

            // Export every registered resource scheme, not only KiriKiri XP3.
            ExportAllSchemes();
            ExportGameMap();
        }

        private static void ExportAllSchemes()
        {
            string catalogPath = @".\GameData\SchemeCatalog.csv";
            string parametersPath = @".\GameData\SchemeParameters.csv";

            using (StreamWriter catalog = new StreamWriter(catalogPath, false, new UTF8Encoding(true)))
            using (StreamWriter parameters = new StreamWriter(parametersPath, false, new UTF8Encoding(true)))
            {
                WriteCsvRow(catalog, "format_tag", "format_type", "scheme_type", "parameter_count");
                WriteCsvRow(parameters, "format_tag", "format_type", "scheme_type", "parameter_path", "value_type", "value");

                foreach (GameRes.IResource format in GameRes.FormatCatalog.Instance.Formats
                    .OrderBy(f => f.Tag, StringComparer.Ordinal))
                {
                    GameRes.ResourceScheme scheme = format.Scheme;
                    if (scheme == null)
                        continue;

                    SortedDictionary<string, ParameterValue> values = ReadObjectGraph(scheme);
                    string formatTag = format.Tag ?? "";
                    string formatType = format.GetType().FullName ?? format.GetType().Name;
                    string schemeType = scheme.GetType().FullName ?? scheme.GetType().Name;

                    WriteCsvRow(catalog, formatTag, formatType, schemeType,
                        values.Count.ToString(CultureInfo.InvariantCulture));

                    foreach (KeyValuePair<string, ParameterValue> item in values)
                    {
                        WriteCsvRow(parameters, formatTag, formatType, schemeType,
                            item.Key, item.Value.TypeName, item.Value.Value);
                    }
                }
            }

            Console.WriteLine("Exported: " + catalogPath);
            Console.WriteLine("Exported: " + parametersPath);
        }

        private static void ExportGameMap()
        {
            string path = @".\GameData\GameMap.csv";
            FieldInfo field = typeof(GameRes.FormatCatalog).GetField(
                "m_game_map", BindingFlags.Instance | BindingFlags.NonPublic);
            IDictionary<string, string> gameMap = field == null
                ? null
                : field.GetValue(GameRes.FormatCatalog.Instance) as IDictionary<string, string>;

            using (StreamWriter writer = new StreamWriter(path, false, new UTF8Encoding(true)))
            {
                WriteCsvRow(writer, "executable_or_pattern", "game_title");
                if (gameMap != null)
                {
                    foreach (KeyValuePair<string, string> item in gameMap.OrderBy(x => x.Key, StringComparer.Ordinal))
                        WriteCsvRow(writer, item.Key, item.Value);
                }
            }

            Console.WriteLine("Exported: " + path);
        }

        private sealed class ParameterValue
        {
            public string TypeName;
            public string Value;

            public ParameterValue(string typeName, string value)
            {
                TypeName = typeName;
                Value = value;
            }
        }

        private static SortedDictionary<string, ParameterValue> ReadObjectGraph(object root)
        {
            SortedDictionary<string, ParameterValue> result =
                new SortedDictionary<string, ParameterValue>(StringComparer.Ordinal);
            HashSet<object> active = new HashSet<object>(new ReferenceEqualityComparer());
            FlattenValue("", root, result, active, 0);
            return result;
        }

        private static void FlattenValue(
            string path,
            object value,
            SortedDictionary<string, ParameterValue> output,
            HashSet<object> active,
            int depth)
        {
            if (value == null)
            {
                AddValue(output, path, "null", "<null>");
                return;
            }

            Type type = value.GetType();
            string typeName = type.FullName ?? type.Name;

            if (value is byte[])
            {
                AddValue(output, path, typeName,
                    "hex:" + BitConverter.ToString((byte[])value).Replace("-", ""));
                return;
            }

            if (IsScalar(type))
            {
                AddValue(output, path, typeName, FormatScalar(value));
                return;
            }

            if (depth >= 20)
            {
                AddValue(output, path, typeName, "<maximum depth reached>");
                return;
            }

            bool trackReference = !type.IsValueType;
            if (trackReference && !active.Add(value))
            {
                AddValue(output, path, typeName, "<circular reference>");
                return;
            }

            try
            {
                // Record the concrete type of nested objects too (e.g. YpfScheme inside
                // YuRisScheme.KnownSchemes[game title]).
                if (path.Length > 0)
                    AddValue(output, path, typeName, "<object>");

                IDictionary dictionary = value as IDictionary;
                if (dictionary != null)
                {
                    int count = 0;
                    foreach (DictionaryEntry entry in dictionary)
                    {
                        if (count++ >= 10000)
                        {
                            AddValue(output, JoinPath(path, "[...]"), "limit", "<truncated after 10000 entries>");
                            break;
                        }
                        string key = EscapePathSegment(FormatScalar(entry.Key));
                        FlattenValue(JoinPath(path, "[" + key + "]"), entry.Value,
                            output, active, depth + 1);
                    }
                    if (dictionary.Count == 0 && path.Length > 0)
                        AddValue(output, path, typeName, "{}");
                    return;
                }

                IEnumerable enumerable = value as IEnumerable;
                if (enumerable != null && !(value is string))
                {
                    int index = 0;
                    foreach (object item in enumerable)
                    {
                        if (index >= 10000)
                        {
                            AddValue(output, JoinPath(path, "[...]"), "limit", "<truncated after 10000 items>");
                            break;
                        }
                        FlattenValue(JoinPath(path, "[" + index.ToString(CultureInfo.InvariantCulture) + "]"),
                            item, output, active, depth + 1);
                        index++;
                    }
                    if (index == 0 && path.Length > 0)
                        AddValue(output, path, typeName, "[]");
                    return;
                }

                // Avoid traversing implementation details of framework objects.
                if (type.Namespace != null && type.Namespace.StartsWith("System", StringComparison.Ordinal))
                {
                    AddValue(output, path, typeName, SafeToString(value));
                    return;
                }

                FlattenObjectMembers(value, path, output, active, depth + 1);
            }
            catch (Exception ex)
            {
                AddValue(output, path, typeName, "<error: " + ex.GetType().Name + ">");
            }
            finally
            {
                if (trackReference)
                    active.Remove(value);
            }
        }

        private static void FlattenObjectMembers(
            object instance,
            string path,
            SortedDictionary<string, ParameterValue> output,
            HashSet<object> active,
            int depth)
        {
            Type current = instance.GetType();
            bool foundMember = false;

            while (current != null && current != typeof(object))
            {
                FieldInfo[] fields = current.GetFields(
                    BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly);

                foreach (FieldInfo field in fields.OrderBy(f => f.Name, StringComparer.Ordinal))
                {
                    if (field.IsStatic || field.IsNotSerialized || field.Name.StartsWith("<", StringComparison.Ordinal))
                        continue;

                    string memberPath = JoinPath(path, field.Name);
                    if (output.ContainsKey(memberPath))
                        memberPath = JoinPath(path, current.Name + "." + field.Name);

                    try
                    {
                        FlattenValue(memberPath, field.GetValue(instance), output, active, depth + 1);
                    }
                    catch (Exception ex)
                    {
                        AddValue(output, memberPath, field.FieldType.FullName,
                            "<error: " + ex.GetType().Name + ">");
                    }
                    foundMember = true;
                }

                PropertyInfo[] properties = current.GetProperties(
                    BindingFlags.Instance | BindingFlags.Public | BindingFlags.DeclaredOnly);
                foreach (PropertyInfo property in properties.OrderBy(p => p.Name, StringComparer.Ordinal))
                {
                    MethodInfo getter = property.GetGetMethod();
                    if (getter == null || getter.IsStatic || property.GetIndexParameters().Length != 0)
                        continue;

                    string memberPath = JoinPath(path, property.Name);
                    if (output.ContainsKey(memberPath))
                        continue;

                    try
                    {
                        FlattenValue(memberPath, property.GetValue(instance, null), output, active, depth + 1);
                    }
                    catch (Exception ex)
                    {
                        AddValue(output, memberPath, property.PropertyType.FullName,
                            "<error: " + ex.GetType().Name + ">");
                    }
                    foundMember = true;
                }

                current = current.BaseType;
            }

            if (!foundMember && path.Length > 0)
                AddValue(output, path, instance.GetType().FullName, SafeToString(instance));
        }

        private static void AddValue(
            SortedDictionary<string, ParameterValue> output,
            string path,
            string typeName,
            string value)
        {
            if (String.IsNullOrEmpty(path))
                path = "$";
            if (!output.ContainsKey(path))
                output[path] = new ParameterValue(typeName ?? "", value ?? "");
        }

        private static bool IsScalar(Type type)
        {
            return type.IsPrimitive || type.IsEnum || type == typeof(decimal) ||
                type == typeof(string) || type == typeof(DateTime) || type == typeof(TimeSpan) ||
                type == typeof(Guid) || type == typeof(Uri) || type == typeof(Type);
        }

        private static string FormatScalar(object value)
        {
            if (value == null)
                return "<null>";
            if (value is Type)
                return ((Type)value).FullName;
            IFormattable formattable = value as IFormattable;
            return formattable != null
                ? formattable.ToString(null, CultureInfo.InvariantCulture)
                : value.ToString();
        }

        private static string SafeToString(object value)
        {
            try { return value == null ? "<null>" : value.ToString(); }
            catch { return "<ToString failed>"; }
        }

        private static string JoinPath(string parent, string child)
        {
            if (String.IsNullOrEmpty(parent))
                return child;
            if (child.StartsWith("[", StringComparison.Ordinal))
                return parent + child;
            return parent + "." + child;
        }

        private static string EscapePathSegment(string value)
        {
            return (value ?? "<null>").Replace("\\", "\\\\").Replace("]", "\\]");
        }

        private static void WriteCsvRow(TextWriter writer, params string[] values)
        {
            writer.WriteLine(String.Join(",", values.Select(EscapeCsv)));
        }

        private static string EscapeCsv(string value)
        {
            if (value == null)
                return "";
            bool quote = value.IndexOfAny(new char[] { ',', '"', '\r', '\n' }) >= 0 ||
                value.StartsWith(" ", StringComparison.Ordinal) ||
                value.EndsWith(" ", StringComparison.Ordinal);
            string escaped = value.Replace("\"", "\"\"");
            return quote ? "\"" + escaped + "\"" : escaped;
        }

        private sealed class ReferenceEqualityComparer : IEqualityComparer<object>
        {
            public new bool Equals(object x, object y) { return Object.ReferenceEquals(x, y); }
            public int GetHashCode(object obj) { return RuntimeHelpers.GetHashCode(obj); }
        }
    }
}
