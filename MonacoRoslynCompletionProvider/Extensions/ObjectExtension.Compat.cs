using System;
using System.Collections;
using System.Reflection;
using System.Text;
using Newtonsoft.Json;

namespace System
{
    public static class ObjectExtension
    {
        public static string ToJson(this object value)
        {
            return JsonConvert.SerializeObject(value, Formatting.None);
        }

        public static string ToJson(this object value, bool format)
        {
            return JsonConvert.SerializeObject(value, format ? Formatting.Indented : Formatting.None);
        }

        public static void Dump(this object value)
        {
            Dump(value, null, 3);
        }

        public static void Dump(this object value, string description)
        {
            Dump(value, description, 3);
        }

        public static void Dump(this object value, string description, int maxDepth)
        {
            if (description != null && description.Length > 0)
            {
                Console.WriteLine("### " + description);
                Console.WriteLine();
            }

            string output = DumpObject(value, 0, maxDepth, new ArrayList());
            Console.WriteLine(output);
        }

        private static string DumpObject(object value, int depth, int maxDepth, ArrayList visited)
        {
            if (value == null)
            {
                return "*null*";
            }

            Type type = value.GetType();

            if (depth > maxDepth)
            {
                return "*" + type.Name + " (max depth reached)*";
            }

            if (IsPrimitiveLike(value, type))
            {
                return FormatPrimitiveValue(value);
            }

            if (!type.IsValueType)
            {
                if (visited.Contains(value))
                {
                    return "*" + type.Name + " (circular reference)*";
                }
                visited.Add(value);
            }

            if (value is IEnumerable && !(value is string))
            {
                return DumpCollection((IEnumerable)value, depth, maxDepth, visited);
            }

            return DumpComplexObject(value, depth, maxDepth, visited);
        }

        private static bool IsPrimitiveLike(object value, Type type)
        {
            return type.IsPrimitive
                || type.IsEnum
                || value is string
                || value is decimal
                || value is DateTime
                || value is DateTimeOffset
                || value is TimeSpan
                || value is Guid;
        }

        private static string FormatPrimitiveValue(object value)
        {
            if (value == null)
            {
                return "*null*";
            }

            if (value is string)
            {
                return (string)value;
            }

            if (value is char)
            {
                return "'" + value.ToString() + "'";
            }

            if (value is DateTime)
            {
                return ((DateTime)value).ToString("yyyy-MM-dd HH:mm:ss");
            }

            if (value is DateTimeOffset)
            {
                return ((DateTimeOffset)value).ToString("yyyy-MM-dd HH:mm:ss zzz");
            }

            if (value is TimeSpan)
            {
                return value.ToString();
            }

            if (value is Guid)
            {
                return value.ToString();
            }

            if (value is bool)
            {
                return ((bool)value) ? "true" : "false";
            }

            return value.ToString();
        }

        private static string DumpCollection(IEnumerable enumerable, int depth, int maxDepth, ArrayList visited)
        {
            StringBuilder sb = new StringBuilder();
            ArrayList items = new ArrayList();
            foreach (object item in enumerable)
            {
                items.Add(item);
            }

            if (items.Count == 0)
            {
                return "*empty collection*";
            }

            sb.AppendLine("**" + enumerable.GetType().Name + "** (Count: " + items.Count + ")");
            sb.AppendLine();

            int displayCount = items.Count > 100 ? 100 : items.Count;
            for (int i = 0; i < displayCount; i++)
            {
                string formatted = FormatValueInline(items[i], depth + 1, maxDepth, visited);
                sb.AppendLine("[" + i + "] " + formatted);
            }

            if (items.Count > displayCount)
            {
                sb.AppendLine("... (" + (items.Count - displayCount) + " more items)");
            }

            return sb.ToString();
        }

        private static string DumpComplexObject(object value, int depth, int maxDepth, ArrayList visited)
        {
            Type type = value.GetType();
            StringBuilder sb = new StringBuilder();
            sb.AppendLine("**" + type.Name + "**");
            sb.AppendLine();

            PropertyInfo[] properties = type.GetProperties(BindingFlags.Public | BindingFlags.Instance);
            FieldInfo[] fields = type.GetFields(BindingFlags.Public | BindingFlags.Instance);
            int count = 0;

            for (int i = 0; i < properties.Length; i++)
            {
                PropertyInfo prop = properties[i];
                if (prop.GetIndexParameters().Length > 0)
                {
                    continue;
                }

                string formatted;
                try
                {
                    object propValue = prop.GetValue(value, null);
                    formatted = FormatValueInline(propValue, depth + 1, maxDepth, visited);
                }
                catch
                {
                    formatted = "*error*";
                }

                sb.AppendLine("- " + prop.Name + ": " + formatted);
                count++;
            }

            for (int i = 0; i < fields.Length; i++)
            {
                FieldInfo field = fields[i];
                string formatted;
                try
                {
                    object fieldValue = field.GetValue(value);
                    formatted = FormatValueInline(fieldValue, depth + 1, maxDepth, visited);
                }
                catch
                {
                    formatted = "*error*";
                }

                sb.AppendLine("- " + field.Name + ": " + formatted);
                count++;
            }

            if (count == 0)
            {
                sb.AppendLine(value.ToString());
            }

            return sb.ToString();
        }

        private static string FormatValueInline(object value, int depth, int maxDepth, ArrayList visited)
        {
            if (value == null)
            {
                return "*null*";
            }

            Type type = value.GetType();

            if (depth > maxDepth)
            {
                return "*" + type.Name + " (max depth)*";
            }

            if (IsPrimitiveLike(value, type))
            {
                string formatted = FormatPrimitiveValue(value);
                return Truncate(formatted, 200);
            }

            if (!type.IsValueType && visited.Contains(value))
            {
                return "*" + type.Name + " (circular)*";
            }

            string nested = DumpObject(value, depth, maxDepth, visited);
            return IndentLines(nested);
        }

        private static string Truncate(string value, int maxLength)
        {
            if (value == null || value.Length <= maxLength)
            {
                return value ?? string.Empty;
            }

            return value.Substring(0, maxLength - 3) + "...";
        }

        private static string IndentLines(string text)
        {
            if (text == null)
            {
                return string.Empty;
            }

            string normalized = NormalizeLineEndings(text);
            return normalized.Replace("\n", "\n  ");
        }

        private static string NormalizeLineEndings(string text)
        {
            return text.Replace("\r\n", "\n").Replace("\r", "\n");
        }
    }
}
