﻿using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Runtime.Remoting.Metadata.W3cXsd2001;
using System.Text;
using System.Threading.Tasks;

namespace SchemeTool
{
    class Program
    {
        static void Main(string[] args)
        {
            // Load database.
            using (Stream stream = File.OpenRead(".\\GameData\\Formats.dat"))
            {
                GameRes.FormatCatalog.Instance.DeserializeScheme(stream);
            }

#if true
            // Export the original JSON and two spreadsheet-friendly XP3 tables.
            ExportXp3Tables();

            using (Stream stream = File.Create(".\\GameData\\Formats.json"))
            {
                GameRes.FormatCatalog.Instance.SerializeSchemeJson(stream);
            }
            return;
#endif
            GameRes.Formats.KiriKiri.Xp3Opener format = GameRes.FormatCatalog.Instance.ArcFormats
                .FirstOrDefault(a => a is GameRes.Formats.KiriKiri.Xp3Opener) as GameRes.Formats.KiriKiri.Xp3Opener;

            if (format != null)
            {
                GameRes.Formats.KiriKiri.Xp3Scheme scheme = format.Scheme as GameRes.Formats.KiriKiri.Xp3Scheme;

                // Add scheme information here.

#if true
                byte[] cb = File.ReadAllBytes(@"MEM_10014628_00001000.mem");
                var cb2 = MemoryMarshal.Cast<byte, uint>(cb);
                for (int i = 0; i < cb2.Length; i++)
                    cb2[i] = ~cb2[i];
                var cs = new GameRes.Formats.KiriKiri.CxScheme
                {
                    Mask = 0x000,
                    Offset = 0x000,
                    PrologOrder = new byte[] { 0, 1, 2 },
                    OddBranchOrder = new byte[] { 0, 1, 2, 3, 4, 5 },
                    EvenBranchOrder = new byte[] { 0, 1, 2, 3, 4, 5, 6, 7 },
                    ControlBlock = cb2.ToArray()
                };
                var crypt = new GameRes.Formats.KiriKiri.HxCrypt(cs);
                crypt.RandomType = 0;
                crypt.FilterKey = 0x0000000000000000;
                crypt.NamesFile = "HxNames.lst";
                var keyA1 = SoapHexBinary.Parse("0000000000000000000000000000000000000000000000000000000000000000").Value;
                var keyA2 = SoapHexBinary.Parse("00000000000000000000000000000000").Value;
                var keyB1 = SoapHexBinary.Parse("0000000000000000000000000000000000000000000000000000000000000000").Value;
                var keyB2 = SoapHexBinary.Parse("00000000000000000000000000000000").Value;
                crypt.IndexKeyDict = new Dictionary<string, GameRes.Formats.KiriKiri.HxIndexKey>()
                {
                    { "data.xp3", new GameRes.Formats.KiriKiri.HxIndexKey { Key1 = keyA1, Key2 = keyA2 } },
                    { "update.xp3", new GameRes.Formats.KiriKiri.HxIndexKey { Key1 = keyB1, Key2 = keyB2 } },
                };
#else
                GameRes.Formats.KiriKiri.ICrypt crypt = new GameRes.Formats.KiriKiri.XorCrypt(0x00);
#endif

                // scheme.KnownSchemes.Add("game title", crypt);
            }

            var gameMap = typeof(GameRes.FormatCatalog).GetField("m_game_map", BindingFlags.Instance | BindingFlags.NonPublic)
                .GetValue(GameRes.FormatCatalog.Instance) as Dictionary<string, string>;

            if (gameMap != null)
            {
                // Add file name here.
                // gameMap.Add("game.exe", "game title");
            }

            // Save database.
            using (Stream stream = File.Create(".\\GameData\\Formats.dat"))
            {
                GameRes.FormatCatalog.Instance.SerializeScheme(stream);
            }
        }

        private static void ExportXp3Tables()
        {
            IDictionary<string, GameRes.Formats.KiriKiri.ICrypt> knownSchemes =
                GameRes.Formats.KiriKiri.Xp3Opener.KnownSchemes;
            ISet<string> noCryptTitles = GameRes.Formats.KiriKiri.Xp3Opener.NoCryptTitles;

            SortedSet<string> titles = new SortedSet<string>(StringComparer.Ordinal);
            titles.UnionWith(knownSchemes.Keys);
            titles.UnionWith(noCryptTitles);

            string schemesPath = @".\GameData\XP3-schemes.csv";
            string parametersPath = @".\GameData\XP3-parameters.csv";

            using (StreamWriter schemes = new StreamWriter(schemesPath, false, new UTF8Encoding(true)))
            using (StreamWriter parameters = new StreamWriter(parametersPath, false, new UTF8Encoding(true)))
            {
                WriteCsvRow(schemes,
                    "title", "entry_kind", "algorithm", "in_known_schemes", "in_no_crypt_titles",
                    "hash_after_crypt", "startup_tjs_not_encrypted", "obfuscated_index", "parameter_count");
                WriteCsvRow(parameters, "title", "algorithm", "parameter", "value");

                foreach (string title in titles)
                {
                    GameRes.Formats.KiriKiri.ICrypt crypt;
                    bool hasKnownScheme = knownSchemes.TryGetValue(title, out crypt);
                    bool isNoCryptTitle = noCryptTitles.Contains(title);

                    if (!hasKnownScheme && isNoCryptTitle)
                        crypt = new GameRes.Formats.KiriKiri.NoCrypt();

                    string entryKind = hasKnownScheme && isNoCryptTitle
                        ? "KnownSchemes+NoCryptTitles"
                        : hasKnownScheme ? "KnownSchemes" : "NoCryptTitles";
                    string algorithm = crypt == null ? "(null)" : crypt.GetType().Name;

                    SortedDictionary<string, string> values = crypt == null
                        ? new SortedDictionary<string, string>(StringComparer.Ordinal)
                        : ReadCryptParameters(crypt);

                    WriteCsvRow(schemes,
                        title,
                        entryKind,
                        algorithm,
                        hasKnownScheme ? "true" : "false",
                        isNoCryptTitle ? "true" : "false",
                        crypt == null ? "" : crypt.HashAfterCrypt.ToString().ToLowerInvariant(),
                        crypt == null ? "" : crypt.StartupTjsNotEncrypted.ToString().ToLowerInvariant(),
                        crypt == null ? "" : crypt.ObfuscatedIndex.ToString().ToLowerInvariant(),
                        values.Count.ToString(CultureInfo.InvariantCulture));

                    foreach (KeyValuePair<string, string> item in values)
                    {
                        WriteCsvRow(parameters, title, algorithm, item.Key, item.Value);
                    }
                }
            }

            Console.WriteLine("XP3 schemes exported: " + schemesPath);
            Console.WriteLine("XP3 parameters exported: " + parametersPath);
        }

        private static SortedDictionary<string, string> ReadCryptParameters(object crypt)
        {
            SortedDictionary<string, string> result =
                new SortedDictionary<string, string>(StringComparer.Ordinal);
            HashSet<object> active = new HashSet<object>(new ReferenceEqualityComparer());
            FlattenValue("", crypt, result, active, 0);
            return result;
        }

        private static void FlattenValue(
            string path,
            object value,
            SortedDictionary<string, string> output,
            HashSet<object> active,
            int depth)
        {
            if (value == null)
            {
                if (path.Length > 0)
                    output[path] = "<null>";
                return;
            }

            Type type = value.GetType();

            if (value is byte[])
            {
                output[path] = "hex:" + BitConverter.ToString((byte[])value).Replace("-", "");
                return;
            }

            if (IsScalar(type))
            {
                if (path.Length > 0)
                    output[path] = FormatScalar(value);
                return;
            }

            if (depth >= 16)
            {
                output[path] = "<maximum depth reached>";
                return;
            }

            bool trackReference = !type.IsValueType;
            if (trackReference && !active.Add(value))
            {
                output[path] = "<circular reference>";
                return;
            }

            try
            {
                IDictionary dictionary = value as IDictionary;
                if (dictionary != null)
                {
                    if (dictionary.Count == 0 && path.Length > 0)
                        output[path] = "{}";

                    int count = 0;
                    foreach (DictionaryEntry entry in dictionary)
                    {
                        if (count++ >= 4096)
                        {
                            output[JoinPath(path, "...")] = "<truncated after 4096 entries>";
                            break;
                        }

                        string key = EscapePathSegment(FormatScalar(entry.Key));
                        FlattenValue(JoinPath(path, "[" + key + "]"), entry.Value, output, active, depth + 1);
                    }
                    return;
                }

                IEnumerable enumerable = value as IEnumerable;
                if (enumerable != null && !(value is string))
                {
                    int index = 0;
                    foreach (object item in enumerable)
                    {
                        if (index >= 4096)
                        {
                            output[JoinPath(path, "[...]")] = "<truncated after 4096 items>";
                            break;
                        }
                        FlattenValue(JoinPath(path, "[" + index.ToString(CultureInfo.InvariantCulture) + "]"),
                            item, output, active, depth + 1);
                        index++;
                    }
                    if (index == 0 && path.Length > 0)
                        output[path] = "[]";
                    return;
                }

                // Avoid traversing framework internals (Lazy<T>, streams, reflection objects, etc.).
                if (type.Namespace != null && type.Namespace.StartsWith("System", StringComparison.Ordinal))
                {
                    if (path.Length > 0)
                        output[path] = SafeToString(value);
                    return;
                }

                FlattenObjectMembers(value, path, output, active, depth + 1);
            }
            catch (Exception ex)
            {
                if (path.Length > 0)
                    output[path] = "<error: " + ex.GetType().Name + ">";
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
            SortedDictionary<string, string> output,
            HashSet<object> active,
            int depth)
        {
            Type current = instance.GetType();
            bool foundMember = false;

            // Include private fields as well as fields declared by base classes. This is where
            // many algorithm-specific constants and key arrays are stored.
            while (current != null && current != typeof(object))
            {
                FieldInfo[] fields = current.GetFields(
                    BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly);

                foreach (FieldInfo field in fields.OrderBy(f => f.Name, StringComparer.Ordinal))
                {
                    if (field.IsStatic || field.IsNotSerialized || field.Name.StartsWith("<", StringComparison.Ordinal))
                        continue;

                    string memberName = field.Name;
                    string memberPath = JoinPath(path, memberName);
                    if (output.ContainsKey(memberPath))
                        memberPath = JoinPath(path, current.Name + "." + memberName);

                    try
                    {
                        FlattenValue(memberPath, field.GetValue(instance), output, active, depth + 1);
                    }
                    catch (Exception ex)
                    {
                        output[memberPath] = "<error: " + ex.GetType().Name + ">";
                    }
                    foundMember = true;
                }

                // Include readable public properties (for example HashAfterCrypt and settings
                // exposed through properties instead of fields).
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
                        output[memberPath] = "<error: " + ex.GetType().Name + ">";
                    }
                    foundMember = true;
                }

                current = current.BaseType;
            }

            if (!foundMember && path.Length > 0)
                output[path] = SafeToString(instance);
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

            Type type = value.GetType();
            if (type == typeof(Uri))
                return ((Uri)value).ToString();
            if (type == typeof(Type))
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
            return value.Replace("\\", "\\\\").Replace("]", "\\]");
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
            public new bool Equals(object x, object y)
            {
                return Object.ReferenceEquals(x, y);
            }

            public int GetHashCode(object obj)
            {
                return RuntimeHelpers.GetHashCode(obj);
            }
        }
    }
}
