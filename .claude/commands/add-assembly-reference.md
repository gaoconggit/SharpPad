# /add-assembly-reference - Add Missing Assembly Reference

Automatically adds missing .NET assembly references to CompletionWorkspace for dynamic code execution.

## Syntax

```
/add-assembly-reference <type_name> [assembly_name]
```

## Description

When users encounter errors like "未能在命名空间中找到类型名" (Type not found in namespace) during dynamic code execution in SharpPad, this command automatically:

1. Identifies the missing assembly based on the error message
2. Adds the appropriate `TryAddType()` call to `CompletionWorkspace.cs`
3. Adds the assembly name to the assembly list if needed
4. Rebuilds the MonacoRoslynCompletionProvider project

## Common Assembly Issues

### WebSocket Support
```
Error: 未能在命名空间"System.Net.WebSockets"中找到类型名"ClientWebSocket"
Solution: /add-assembly-reference System.Net.WebSockets.ClientWebSocket
```

### Task Parallel Library
```
Error: 未能在命名空间"System.Threading.Tasks"中找到类型名"Parallel"
Solution: /add-assembly-reference System.Threading.Tasks.Parallel
```

### HTTP Client
```
Error: 未能在命名空间"System.Net.Http"中找到类型名"HttpClient"
Solution: /add-assembly-reference System.Net.Http.HttpClient
```

### JSON Serialization
```
Error: 未能在命名空间"System.Text.Json"中找到类型名"JsonSerializer"
Solution: /add-assembly-reference System.Text.Json.JsonSerializer
```

## Error Pattern Recognition

The command recognizes common error patterns:

### Type Forward Errors
```
未能在命名空间"X.Y.Z"中找到类型名"TypeName"。
此类型已转发到程序集"AssemblyName, Version=X.X.X.X, ..."
```
**Action**: Extracts both the type name and assembly name from error message

### Missing Reference Errors
```
找不到类型或命名空间名"TypeName"
```
**Action**: Searches for the type in common .NET assemblies

## How It Works

### 1. Type Reference Addition
Adds `TryAddType()` call in `BuildDefaultAssemblyPaths()`:

```csharp
TryAddType(typeof(System.Net.WebSockets.WebSocket));
```

### 2. Assembly Name Addition
Adds assembly name to the assembly list:

```csharp
foreach (var assemblyName in new[]
{
    "System.Runtime",
    // ... existing entries
    "System.Net.WebSockets",
    "System.Net.WebSockets.Client"  // ← Added
})
```

### 3. Automatic Rebuild
Rebuilds the MonacoRoslynCompletionProvider project to apply changes.

## Examples

### Basic Usage

#### Add single type
```
/add-assembly-reference System.Net.WebSockets.ClientWebSocket
```

#### Add with explicit assembly name
```
/add-assembly-reference System.Net.WebSockets.ClientWebSocket System.Net.WebSockets.Client
```

#### Process error message directly
```
/add-assembly-reference
未能在命名空间"System.Net.WebSockets"中找到类型名"ClientWebSocket"。
此类型已转发到程序集"System.Net.WebSockets.Client, Version=9.0.0.0, ..."
```

### Batch Processing

#### Add multiple related types
```
/add-assembly-reference System.Net.WebSockets.WebSocket System.Net.WebSockets.ClientWebSocket System.Net.WebSockets.WebSocketState
```

## File Location

Target file: `MonacoRoslynCompletionProvider/CompletionWorkspace.cs`

### Section 1: Type References (Line ~40-75)
Where `TryAddType()` calls are added

### Section 2: Assembly Names (Line ~81-103)
Where assembly names are added to the list

## Validation

After adding references, the command:

1. ✅ Verifies the type exists in .NET runtime
2. ✅ Checks if assembly is available
3. ✅ Builds the project to ensure no compilation errors
4. ✅ Confirms the reference is properly cached

## Common Assembly Reference Table

| Namespace | Type | Assembly Name |
|-----------|------|---------------|
| System.Net.WebSockets | ClientWebSocket | System.Net.WebSockets.Client |
| System.Net.WebSockets | WebSocket | System.Net.WebSockets |
| System.Threading.Tasks | Parallel | System.Threading.Tasks.Parallel |
| System.IO.Compression | ZipArchive | System.IO.Compression |
| System.Text.Json | JsonDocument | System.Text.Json |
| System.Security.Cryptography | Aes | System.Security.Cryptography |
| System.Net.Sockets | TcpClient | System.Net.Sockets |
| System.Diagnostics | Stopwatch | System.Diagnostics.DiagnosticSource |

## Troubleshooting

### Type Not Found
If the type cannot be resolved:
```
Error: Type 'X.Y.Z.TypeName' not found in .NET runtime
Action: Verify the fully qualified type name
```

### Assembly Already Referenced
If the assembly is already in the list:
```
Info: Assembly 'AssemblyName' is already referenced
Action: Check if the type reference needs to be added
```

### Build Errors
If compilation fails after adding reference:
```
Error: Build failed with errors
Action: Check error messages and verify type compatibility
```

## Best Practices

### When to Use
- ✅ Dynamic code execution errors about missing types
- ✅ Type forward errors pointing to specific assemblies
- ✅ Adding support for new .NET APIs in SharpPad

### When NOT to Use
- ❌ NuGet package references (use NuGet management instead)
- ❌ Custom user assemblies (handled by NuGet system)
- ❌ Third-party libraries (use NuGet package manager)

## Technical Details

### Assembly Loading Strategy
1. **Type-based loading**: Loads assemblies through type references
2. **Name-based loading**: Loads from trusted assembly list
3. **Caching**: Uses `ConcurrentDictionary` for reference cache
4. **Lazy loading**: Assemblies loaded on-demand during compilation

### Performance Impact
- ✅ Minimal: References are cached and reused
- ✅ One-time cost: Initial loading during workspace creation
- ✅ Concurrent: Supports parallel compilation

## Related Files

- `MonacoRoslynCompletionProvider/CompletionWorkspace.cs` - Main workspace with assembly references
- `MonacoRoslynCompletionProvider/Api/CodeRunner.cs` - Code execution engine
- `SharpPad/Controllers/CompletionController.cs` - API endpoints

## Integration

This command modifies the core Roslyn workspace configuration to ensure that dynamically executed C# code has access to all necessary .NET framework assemblies, preventing runtime compilation errors.
