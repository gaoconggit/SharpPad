using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Newtonsoft.Json;
using System.Reflection;

namespace System
{
    public static class ObjectExtengsion
    {
        public static string ToJson(this Object value, bool format = false)
        {
            string json = JsonConvert.SerializeObject(value, format ? Formatting.Indented : Formatting.None);
            return json;
        }

        public static void Dump(this Object value, string description = null, int maxDepth = 3)
        {
            if (!string.IsNullOrEmpty(description))
            {
                Console.WriteLine($"### {description}");
                Console.WriteLine();
            }

            var output = DumpObject(value, 0, maxDepth, new HashSet<object>());
            Console.WriteLine(output);
        }

        private static string DumpObject(object value, int depth, int maxDepth, HashSet<object> visited)
        {
            // Handle null
            if (value == null)
            {
                return "*null*";
            }

            var type = value.GetType();

            // Prevent infinite recursion
            if (depth > maxDepth)
            {
                return $"*{type.Name} (max depth reached)*";
            }

            // Handle primitive types and strings
            if (type.IsPrimitive || type.IsEnum || value is string || value is decimal || value is DateTime || value is DateTimeOffset || value is TimeSpan || value is Guid)
            {
                return FormatPrimitiveValue(value);
            }

            // Prevent circular references for reference types
            if (!type.IsValueType)
            {
                if (visited.Contains(value))
                {
                    return $"*{type.Name} (circular reference)*";
                }
                visited.Add(value);
            }

            // Handle collections
            if (value is IEnumerable enumerable && !(value is string))
            {
                return DumpCollection(enumerable, depth, maxDepth, visited);
            }

            // Handle complex objects
            return DumpComplexObject(value, depth, maxDepth, visited);
        }

        private static string FormatPrimitiveValue(object value)
        {
            return value switch
            {
                null => "*null*",
                string s => $"\"{s}\"",
                char c => $"'{c}'",
                DateTime dt => dt.ToString("yyyy-MM-dd HH:mm:ss"),
                DateTimeOffset dto => dto.ToString("yyyy-MM-dd HH:mm:ss zzz"),
                TimeSpan ts => ts.ToString(),
                Guid g => g.ToString(),
                bool b => b.ToString().ToLower(),
                _ => value.ToString()
            };
        }

        private static string DumpCollection(IEnumerable enumerable, int depth, int maxDepth, HashSet<object> visited)
        {
            var sb = new StringBuilder();
            var items = enumerable.Cast<object>().ToList();

            if (items.Count == 0)
            {
                return "*empty collection*";
            }

            var collectionType = enumerable.GetType();
            sb.AppendLine($"**{collectionType.Name}** (Count: {items.Count})");
            sb.AppendLine();

            // For small collections, show items inline
            if (items.Count <= 10 && items.All(item => item == null || item.GetType().IsPrimitive || item is string || item is decimal))
            {
                sb.AppendLine("```");
                for (int i = 0; i < items.Count; i++)
                {
                    sb.AppendLine($"[{i}] {FormatPrimitiveValue(items[i])}");
                }
                sb.AppendLine("```");
            }
            else
            {
                // For larger or complex collections, show as table
                sb.AppendLine("| Index | Value |");
                sb.AppendLine("|-------|-------|");

                int displayCount = Math.Min(items.Count, 100); // Limit to 100 items
                for (int i = 0; i < displayCount; i++)
                {
                    var itemValue = DumpObject(items[i], depth + 1, maxDepth, visited);
                    // Escape pipe characters and newlines for markdown table
                    itemValue = itemValue.Replace("|", "\\|").Replace("\n", " ").Replace("\r", "");
                    if (itemValue.Length > 100)
                    {
                        itemValue = itemValue.Substring(0, 97) + "...";
                    }
                    sb.AppendLine($"| {i} | {itemValue} |");
                }

                if (items.Count > displayCount)
                {
                    sb.AppendLine($"| ... | *({items.Count - displayCount} more items)* |");
                }
            }

            return sb.ToString();
        }

        private static string DumpComplexObject(object value, int depth, int maxDepth, HashSet<object> visited)
        {
            var sb = new StringBuilder();
            var type = value.GetType();

            sb.AppendLine($"**{type.Name}**");
            sb.AppendLine();

            // Get all public properties
            var properties = type.GetProperties(BindingFlags.Public | BindingFlags.Instance)
                                .Where(p => p.CanRead && p.GetIndexParameters().Length == 0)
                                .OrderBy(p => p.Name)
                                .ToList();

            // Get all public fields
            var fields = type.GetFields(BindingFlags.Public | BindingFlags.Instance)
                            .OrderBy(f => f.Name)
                            .ToList();

            if (properties.Count == 0 && fields.Count == 0)
            {
                // Try ToString() for objects with no accessible properties
                return $"**{type.Name}**: {value.ToString()}";
            }

            sb.AppendLine("| Property | Value |");
            sb.AppendLine("|----------|-------|");

            // Dump properties
            foreach (var prop in properties)
            {
                try
                {
                    var propValue = prop.GetValue(value);
                    var formattedValue = DumpObject(propValue, depth + 1, maxDepth, visited);
                    // Escape pipe characters and limit length for table display
                    formattedValue = formattedValue.Replace("|", "\\|").Replace("\n", " ").Replace("\r", "");
                    if (formattedValue.Length > 200)
                    {
                        formattedValue = formattedValue.Substring(0, 197) + "...";
                    }
                    sb.AppendLine($"| {prop.Name} | {formattedValue} |");
                }
                catch (Exception ex)
                {
                    sb.AppendLine($"| {prop.Name} | *Error: {ex.Message}* |");
                }
            }

            // Dump fields
            foreach (var field in fields)
            {
                try
                {
                    var fieldValue = field.GetValue(value);
                    var formattedValue = DumpObject(fieldValue, depth + 1, maxDepth, visited);
                    // Escape pipe characters and limit length for table display
                    formattedValue = formattedValue.Replace("|", "\\|").Replace("\n", " ").Replace("\r", "");
                    if (formattedValue.Length > 200)
                    {
                        formattedValue = formattedValue.Substring(0, 197) + "...";
                    }
                    sb.AppendLine($"| {field.Name} | {formattedValue} |");
                }
                catch (Exception ex)
                {
                    sb.AppendLine($"| {field.Name} | *Error: {ex.Message}* |");
                }
            }

            return sb.ToString();
        }
    }
}
