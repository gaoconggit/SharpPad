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
    public static class ObjectExtension
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
                string s => s,
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

            // For simple primitive collections, show items inline
            if (items.Count <= 10 && items.All(item => item == null || item.GetType().IsPrimitive || item is string || item is decimal))
            {
                sb.AppendLine("```");
                for (int i = 0; i < items.Count; i++)
                {
                    sb.AppendLine($"[{i}] {FormatPrimitiveValue(items[i])}");
                }
                sb.AppendLine("```");
                return sb.ToString();
            }

            // Check if items are complex objects with properties
            var firstNonNullItem = items.FirstOrDefault(item => item != null);
            if (firstNonNullItem == null)
            {
                return sb.ToString();
            }

            var itemType = firstNonNullItem.GetType();

            // For complex objects, create a table with properties as columns
            if (!itemType.IsPrimitive && itemType != typeof(string))
            {
                var properties = itemType.GetProperties(BindingFlags.Public | BindingFlags.Instance)
                                        .Where(p => p.CanRead && p.GetIndexParameters().Length == 0)
                                        .OrderBy(p => p.Name)
                                        .ToList();

                var fields = itemType.GetFields(BindingFlags.Public | BindingFlags.Instance)
                                    .OrderBy(f => f.Name)
                                    .ToList();

                if (properties.Count > 0 || fields.Count > 0)
                {
                    // Build table with properties as columns
                    var allMembers = new List<(string Name, Func<object, object> GetValue)>();

                    foreach (var prop in properties)
                    {
                        allMembers.Add((prop.Name, obj => {
                            try { return prop.GetValue(obj); }
                            catch { return "*error*"; }
                        }));
                    }

                    foreach (var field in fields)
                    {
                        allMembers.Add((field.Name, obj => {
                            try { return field.GetValue(obj); }
                            catch { return "*error*"; }
                        }));
                    }

                    // Create header
                    var headers = new List<string> { "Index" };
                    headers.AddRange(allMembers.Select(m => m.Name));
                    sb.AppendLine($"| {string.Join(" | ", headers)} |");

                    // Create separator
                    var separators = headers.Select(_ => "-------");
                    sb.AppendLine($"|{string.Join("|", separators)}|");

                    // Create rows
                    int displayCount = Math.Min(items.Count, 100); // Limit to 100 items
                    for (int i = 0; i < displayCount; i++)
                    {
                        var item = items[i];
                        var row = new List<string> { i.ToString() };

                        if (item == null)
                        {
                            // Fill with null for all columns
                            row.AddRange(allMembers.Select(_ => "*null*"));
                        }
                        else
                        {
                            foreach (var member in allMembers)
                            {
                                var value = member.GetValue(item);
                                var formatted = FormatValueForTableCell(value, depth, maxDepth, visited);
                                row.Add(formatted);
                            }
                        }

                        sb.AppendLine($"| {string.Join(" | ", row)} |");
                    }

                    if (items.Count > displayCount)
                    {
                        sb.AppendLine($"| ... | *({items.Count - displayCount} more items)* | |");
                    }

                    return sb.ToString();
                }
            }

            // Fallback: show as simple index-value table
            sb.AppendLine("| Index | Value |");
            sb.AppendLine("|-------|-------|");

            int fallbackDisplayCount = Math.Min(items.Count, 100);
            for (int i = 0; i < fallbackDisplayCount; i++)
            {
                var itemValue = FormatValueForTableCell(items[i], depth, maxDepth, visited);
                sb.AppendLine($"| {i} | {itemValue} |");
            }

            if (items.Count > fallbackDisplayCount)
            {
                sb.AppendLine($"| ... | *({items.Count - fallbackDisplayCount} more items)* |");
            }

            return sb.ToString();
        }

        private static string FormatValueForTableCell(object value, int depth, int maxDepth, HashSet<object> visited)
        {
            if (value == null)
            {
                return "*null*";
            }

            var type = value.GetType();

            // Prevent infinite recursion
            if (depth > maxDepth)
            {
                return $"*{type.Name} (max depth)*";
            }

            // Handle primitive types directly
            if (type.IsPrimitive || type.IsEnum || value is string || value is decimal || value is DateTime || value is DateTimeOffset || value is TimeSpan || value is Guid)
            {
                var formatted = FormatPrimitiveValue(value);
                if (formatted.Length > 50)
                {
                    formatted = formatted.Substring(0, 47) + "...";
                }
                return EscapeForTable(formatted);
            }

            // Check for circular references
            if (!type.IsValueType && visited.Contains(value))
            {
                return $"*{type.Name} (circular)*";
            }

            // Handle collections in a compact way
            if (value is IEnumerable enumerable && !(value is string))
            {
                var innerItems = enumerable.Cast<object>().ToList();

                // Show inline for very small, simple collections
                if (innerItems.Count <= 3 && innerItems.All(item => item == null || item.GetType().IsPrimitive || item is string))
                {
                    var itemsStr = string.Join(", ", innerItems.Select(FormatPrimitiveValue));
                    if (itemsStr.Length > 40)
                    {
                        itemsStr = itemsStr.Substring(0, 37) + "...";
                    }
                    return EscapeForTable($"[{innerItems.Count}] {{ {itemsStr} }}");
                }

                // For complex collections, show summary only
                return $"*{type.Name}* ({innerItems.Count})";
            }

            // Handle complex objects - show compact summary
            return $"*{type.Name}*";
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
                    var formattedValue = FormatValueForTable(propValue, depth, maxDepth, visited);
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
                    var formattedValue = FormatValueForTable(fieldValue, depth, maxDepth, visited);
                    sb.AppendLine($"| {field.Name} | {formattedValue} |");
                }
                catch (Exception ex)
                {
                    sb.AppendLine($"| {field.Name} | *Error: {ex.Message}* |");
                }
            }

            return sb.ToString();
        }

        private static string FormatValueForTable(object value, int depth, int maxDepth, HashSet<object> visited)
        {
            if (value == null)
            {
                return "*null*";
            }

            var type = value.GetType();

            // Prevent infinite recursion
            if (depth > maxDepth)
            {
                return $"*{type.Name} (max depth)*";
            }

            // Handle primitive types directly
            if (type.IsPrimitive || type.IsEnum || value is string || value is decimal || value is DateTime || value is DateTimeOffset || value is TimeSpan || value is Guid)
            {
                return EscapeForTable(FormatPrimitiveValue(value));
            }

            // Check for circular references
            if (!type.IsValueType && visited.Contains(value))
            {
                return $"*{type.Name} (circular)*";
            }

            // Handle collections in a compact way
            if (value is IEnumerable enumerable && !(value is string))
            {
                var items = enumerable.Cast<object>().ToList();

                // Show inline for simple, small collections
                if (items.Count <= 5 && items.All(item => item == null || item.GetType().IsPrimitive || item is string))
                {
                    var itemsStr = string.Join(", ", items.Select(FormatPrimitiveValue));
                    return $"[{items.Count}] {{ {EscapeForTable(itemsStr)} }}";
                }

                // For complex collections, show summary
                return $"*{type.Name}* (Count: {items.Count})";
            }

            // Handle complex objects - show summary with key properties
            var properties = type.GetProperties(BindingFlags.Public | BindingFlags.Instance)
                                .Where(p => p.CanRead && p.GetIndexParameters().Length == 0)
                                .Take(3) // Only show first 3 properties
                                .ToList();

            if (properties.Count == 0)
            {
                return $"*{type.Name}*";
            }

            var propSummary = new List<string>();
            foreach (var prop in properties)
            {
                try
                {
                    var propValue = prop.GetValue(value);
                    string propStr;

                    if (propValue == null)
                    {
                        propStr = $"{prop.Name}: null";
                    }
                    else if (propValue.GetType().IsPrimitive || propValue is string || propValue is decimal)
                    {
                        var formatted = FormatPrimitiveValue(propValue);
                        if (formatted.Length > 30)
                        {
                            formatted = formatted.Substring(0, 27) + "...";
                        }
                        propStr = $"{prop.Name}: {formatted}";
                    }
                    else
                    {
                        propStr = $"{prop.Name}: {propValue.GetType().Name}";
                    }

                    propSummary.Add(propStr);
                }
                catch
                {
                    propSummary.Add($"{prop.Name}: *error*");
                }
            }

            var summary = string.Join(", ", propSummary);
            if (summary.Length > 150)
            {
                summary = summary.Substring(0, 147) + "...";
            }

            return $"*{type.Name}* {{ {EscapeForTable(summary)} }}";
        }

        private static string EscapeForTable(string value)
        {
            if (value == null) return string.Empty;
            return value.Replace("|", "\\|").Replace("\n", " ").Replace("\r", "");
        }
    }
}
