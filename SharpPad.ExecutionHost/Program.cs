using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Runtime.Loader;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace SharpPad.ExecutionHost
{
    internal static class Program
    {
        private const string InputPromptMessage = "[INPUT REQUIRED] Please provide input: ";

        private static async Task<int> Main(string[] args)
        {
            Console.OutputEncoding = Encoding.UTF8;

            if (!ExecutionOptions.TryParse(args, out var options, out var error))
            {
                Console.Error.WriteLine(error ?? "Invalid arguments.");
                return 1;
            }

            if (!File.Exists(options.AssemblyPath))
            {
                Console.Error.WriteLine($"Assembly not found: {options.AssemblyPath}");
                return 1;
            }

            string? nativeProbeDirectory = null;
            if (!string.IsNullOrEmpty(options.WorkingDirectory) && Directory.Exists(options.WorkingDirectory))
            {
                try
                {
                    Directory.SetCurrentDirectory(options.WorkingDirectory);
                }
                catch (Exception ex)
                {
                    Console.Error.WriteLine($"Failed to set working directory: {ex.Message}");
                }

                ConfigureNativeLibraryProbing(options.WorkingDirectory);
                nativeProbeDirectory = options.WorkingDirectory;
            }

            if (string.IsNullOrEmpty(nativeProbeDirectory))
            {
                var assemblyDirectory = Path.GetDirectoryName(options.AssemblyPath);
                if (!string.IsNullOrEmpty(assemblyDirectory) && Directory.Exists(assemblyDirectory))
                {
                    nativeProbeDirectory = assemblyDirectory;
                }
            }

            RegisterNativeLibraryResolvers(nativeProbeDirectory);

            using var console = new ConsoleRedirection(InputPromptMessage);
            var loadContext = new ExecutionLoadContext(Path.GetDirectoryName(options.AssemblyPath));

            try
            {
                var assembly = loadContext.LoadFromAssemblyPath(options.AssemblyPath);
                var entryPoint = assembly.EntryPoint ?? throw new InvalidOperationException("No entry point found in the compiled assembly.");
                var executor = new EntryPointExecutor(entryPoint, options.EntryPointArguments);

                if (options.RequiresStaThread && OperatingSystem.IsWindows())
                {
                    return await executor.RunStaAsync().ConfigureAwait(false);
                }

                return await executor.ExecuteAsync().ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                var root = ex is TargetInvocationException tie && tie.InnerException != null ? tie.InnerException : ex;
                Console.Error.WriteLine(root.ToString());
                return 1;
            }
            finally
            {
                loadContext.Unload();
                for (var i = 0; i < 2; i++)
                {
                    GC.Collect();
                    GC.WaitForPendingFinalizers();
                }
            }
        }

        private static void ConfigureNativeLibraryProbing(string workingDirectory)
        {
            if (string.IsNullOrWhiteSpace(workingDirectory))
            {
                return;
            }

            EnsurePathIncludes(workingDirectory);
            TrySetEnvironmentVariable("SKIA_SHARP_NATIVE_LIBRARY_PATH", workingDirectory);
            TrySetEnvironmentVariable("HARFBUZZ_SHARP_NATIVE_LIBRARY_PATH", workingDirectory);
        }

        private static void EnsurePathIncludes(string workingDirectory)
        {
            try
            {
                var currentPath = Environment.GetEnvironmentVariable("PATH") ?? string.Empty;
                var segments = currentPath.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries);
                foreach (var segment in segments)
                {
                    if (string.Equals(segment, workingDirectory, StringComparison.OrdinalIgnoreCase))
                    {
                        return;
                    }
                }

                var updatedPath = string.IsNullOrEmpty(currentPath)
                    ? workingDirectory
                    : $"{workingDirectory}{Path.PathSeparator}{currentPath}";

                Environment.SetEnvironmentVariable("PATH", updatedPath);
            }
            catch
            {
                // Ignore failures per process PATH updates are best-effort only.
            }
        }

        private static void TrySetEnvironmentVariable(string name, string value)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(name) || string.IsNullOrWhiteSpace(value))
                {
                    return;
                }

                var current = Environment.GetEnvironmentVariable(name);
                if (string.IsNullOrWhiteSpace(current))
                {
                    Environment.SetEnvironmentVariable(name, value);
                }
            }
            catch
            {
                // Environment updates are best-effort.
            }
        }

        private static void RegisterNativeLibraryResolvers(string? workingDirectory)
        {
            if (string.IsNullOrWhiteSpace(workingDirectory))
            {
                return;
            }

            void ConfigureForAssembly(Assembly assembly)
            {
                var name = assembly?.GetName().Name;
                if (string.IsNullOrWhiteSpace(name))
                {
                    return;
                }

                if (string.Equals(name, "SkiaSharp", StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(name, "HarfBuzzSharp", StringComparison.OrdinalIgnoreCase))
                {
                    TrySetDllImportResolver(assembly!, workingDirectory);
                }
            }

            AppDomain.CurrentDomain.AssemblyLoad += (_, args) => ConfigureForAssembly(args.LoadedAssembly);

            foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
            {
                ConfigureForAssembly(assembly);
            }
        }

        private static void TrySetDllImportResolver(Assembly assembly, string workingDirectory)
        {
            try
            {
                NativeLibrary.SetDllImportResolver(assembly, (libraryName, _, _) =>
                {
                    if (TryResolveNativeLibraryHandle(workingDirectory, libraryName, out var handle))
                    {
                        return handle;
                    }

                    return IntPtr.Zero;
                });
            }
            catch (InvalidOperationException)
            {
                // Resolver already configured for this assembly.
            }
        }

        private static bool TryResolveNativeLibraryHandle(string workingDirectory, string libraryName, out IntPtr handle)
        {
            handle = IntPtr.Zero;

            if (string.IsNullOrWhiteSpace(workingDirectory) || string.IsNullOrWhiteSpace(libraryName))
            {
                return false;
            }

            foreach (var candidate in EnumerateNativeLibraryCandidates(workingDirectory, libraryName))
            {
                if (!File.Exists(candidate))
                {
                    continue;
                }

                try
                {
                    if (NativeLibrary.TryLoad(candidate, out handle))
                    {
                        return true;
                    }
                }
                catch
                {
                    // Ignore load failures; fallback to default probing behaviour.
                }
            }

            return false;
        }

        private static IEnumerable<string> EnumerateNativeLibraryCandidates(string workingDirectory, string libraryName)
        {
            if (string.IsNullOrWhiteSpace(workingDirectory) || string.IsNullOrWhiteSpace(libraryName))
            {
                yield break;
            }

            var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var trimmed = libraryName.Trim();
            if (!string.IsNullOrEmpty(trimmed))
            {
                names.Add(trimmed);
                var withoutExtension = Path.GetFileNameWithoutExtension(trimmed);
                if (!string.IsNullOrEmpty(withoutExtension))
                {
                    names.Add(withoutExtension);
                    if (!withoutExtension.StartsWith("lib", StringComparison.OrdinalIgnoreCase))
                    {
                        names.Add("lib" + withoutExtension);
                    }
                }
            }

            var extensions = GetNativeLibraryExtensions();
            var yielded = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (var name in names)
            {
                var hasExtension = !string.IsNullOrEmpty(Path.GetExtension(name));
                if (hasExtension)
                {
                    var directPath = Path.Combine(workingDirectory, name);
                    if (yielded.Add(directPath))
                    {
                        yield return directPath;
                    }
                    continue;
                }

                foreach (var extension in extensions)
                {
                    var candidate = Path.Combine(workingDirectory, name + extension);
                    if (yielded.Add(candidate))
                    {
                        yield return candidate;
                    }
                }
            }

            foreach (var file in EnumerateFilesSafe(workingDirectory))
            {
                var fileNameWithoutExtension = Path.GetFileNameWithoutExtension(file);
                if (string.IsNullOrEmpty(fileNameWithoutExtension))
                {
                    continue;
                }

                foreach (var name in names)
                {
                    if (string.IsNullOrEmpty(name))
                    {
                        continue;
                    }

                    var targetName = Path.GetFileNameWithoutExtension(name);
                    if (!string.IsNullOrEmpty(targetName) &&
                        string.Equals(fileNameWithoutExtension, targetName, StringComparison.OrdinalIgnoreCase) &&
                        yielded.Add(file))
                    {
                        yield return file;
                    }
                }
            }
        }

        private static string[] GetNativeLibraryExtensions()
        {
            if (OperatingSystem.IsWindows())
            {
                return new[] { ".dll" };
            }

            if (OperatingSystem.IsMacOS())
            {
                return new[] { ".dylib", ".so" };
            }

            return new[] { ".so", ".dll" };
        }

        private static IEnumerable<string> EnumerateFilesSafe(string directory)
        {
            if (string.IsNullOrWhiteSpace(directory))
            {
                return Array.Empty<string>();
            }

            try
            {
                return Directory.GetFiles(directory, "*", SearchOption.AllDirectories);
            }
            catch
            {
                return Array.Empty<string>();
            }
        }
    }

    internal sealed record ExecutionOptions(string AssemblyPath, string WorkingDirectory, bool RequiresStaThread, string[] EntryPointArguments)
    {
        public static bool TryParse(string[] args, out ExecutionOptions options, out string? error)
        {
            options = default!;
            error = null;

            if (args is null)
            {
                error = "Arguments cannot be null.";
                return false;
            }

            var argumentList = new List<string>();
            string? assemblyPath = null;
            string? workingDirectory = null;
            bool requiresSta = false;

            for (int i = 0; i < args.Length; i++)
            {
                var arg = args[i];
                if (string.Equals(arg, "--assembly", StringComparison.OrdinalIgnoreCase))
                {
                    if (++i >= args.Length)
                    {
                        error = "Missing value for --assembly.";
                        return false;
                    }
                    assemblyPath = args[i];
                }
                else if (string.Equals(arg, "--workingDirectory", StringComparison.OrdinalIgnoreCase))
                {
                    if (++i >= args.Length)
                    {
                        error = "Missing value for --workingDirectory.";
                        return false;
                    }
                    workingDirectory = args[i];
                }
                else if (string.Equals(arg, "--requiresSta", StringComparison.OrdinalIgnoreCase))
                {
                    if (++i >= args.Length || !bool.TryParse(args[i], out requiresSta))
                    {
                        error = "Missing or invalid value for --requiresSta.";
                        return false;
                    }
                }
                else if (string.Equals(arg, "--arg", StringComparison.OrdinalIgnoreCase))
                {
                    if (++i >= args.Length)
                    {
                        error = "Missing value for --arg.";
                        return false;
                    }
                    argumentList.Add(args[i]);
                }
                else if (!string.IsNullOrWhiteSpace(arg))
                {
                    argumentList.Add(arg);
                }
            }

            if (string.IsNullOrWhiteSpace(assemblyPath))
            {
                error = "--assembly is required.";
                return false;
            }

            if (!Path.IsPathRooted(assemblyPath))
            {
                assemblyPath = Path.GetFullPath(assemblyPath);
            }

            if (string.IsNullOrWhiteSpace(workingDirectory))
            {
                workingDirectory = Path.GetDirectoryName(assemblyPath);
            }

            if (argumentList.Count == 0)
            {
                argumentList.Add("sharpPad");
            }

            options = new ExecutionOptions(assemblyPath, workingDirectory ?? string.Empty, requiresSta, argumentList.ToArray());
            return true;
        }
    }

    internal sealed class ExecutionLoadContext : AssemblyLoadContext
    {
        private static readonly bool NativeLoadDebugEnabled =
            string.Equals(Environment.GetEnvironmentVariable("SHARPPAD_DEBUG_NATIVELOAD"), "1", StringComparison.OrdinalIgnoreCase);

        private readonly Dictionary<string, string> _managedAssemblies;
        private readonly Dictionary<string, string> _nativeLibraries;
        private readonly List<string> _nativeSearchPaths;

        private static void DebugLog(string message)
        {
            if (NativeLoadDebugEnabled && !string.IsNullOrEmpty(message))
            {
                Console.Error.WriteLine(message);
            }
        }

        public ExecutionLoadContext(string? assemblyDirectory) : base(isCollectible: true)
        {
            _managedAssemblies = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            _nativeLibraries = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            _nativeSearchPaths = new List<string>();

            void RegisterAssembly(string key, string file)
            {
                if (!string.IsNullOrEmpty(key) && !string.IsNullOrEmpty(file) && File.Exists(file))
                {
                    _managedAssemblies[key] = file;
                }
            }

            void RegisterAssemblyMetadata(string file)
            {
                if (string.IsNullOrEmpty(file) || !File.Exists(file))
                {
                    return;
                }

                var fileName = Path.GetFileName(file);
                var nameWithoutExtension = Path.GetFileNameWithoutExtension(file);
                RegisterAssembly(fileName, file);
                RegisterAssembly(nameWithoutExtension, file);

                try
                {
                    var assemblyName = AssemblyName.GetAssemblyName(file);
                    if (!string.IsNullOrEmpty(assemblyName?.FullName))
                    {
                        RegisterAssembly(assemblyName.FullName, file);
                        if (!string.IsNullOrEmpty(assemblyName.Name))
                        {
                            RegisterAssembly(assemblyName.Name, file);
                        }
                    }
                }
                catch
                {
                    // Not a managed assembly; ignore metadata failures.
                }
            }

            if (!string.IsNullOrWhiteSpace(assemblyDirectory) && Directory.Exists(assemblyDirectory))
            {
                // Add root directory to native search paths
                _nativeSearchPaths.Add(assemblyDirectory);

                foreach (var file in Directory.EnumerateFiles(assemblyDirectory, "*.*", SearchOption.AllDirectories))
                {
                    var extension = Path.GetExtension(file);
                    if (string.Equals(extension, ".dll", StringComparison.OrdinalIgnoreCase) ||
                        string.Equals(extension, ".exe", StringComparison.OrdinalIgnoreCase))
                    {
                        RegisterAssemblyMetadata(file);
                    }

                    if (string.Equals(extension, ".dll", StringComparison.OrdinalIgnoreCase) ||
                        string.Equals(extension, ".so", StringComparison.OrdinalIgnoreCase) ||
                        string.Equals(extension, ".dylib", StringComparison.OrdinalIgnoreCase))
                    {
                        var fileName = Path.GetFileName(file);
                        var nameWithoutExtension = Path.GetFileNameWithoutExtension(file);
                        var fileDirectory = Path.GetDirectoryName(file);

                        if (!string.IsNullOrEmpty(nameWithoutExtension))
                        {
                            _nativeLibraries[nameWithoutExtension] = file;
                        }

                        if (!string.IsNullOrEmpty(fileName))
                        {
                            _nativeLibraries[fileName] = file;
                        }

                        // Track unique directories containing native libraries
                        if (!string.IsNullOrEmpty(fileDirectory) && !_nativeSearchPaths.Contains(fileDirectory))
                        {
                            _nativeSearchPaths.Add(fileDirectory);
                        }
                    }
                }
            }

            foreach (var tpaPath in EnumerateTrustedPlatformAssemblies())
            {
                RegisterAssemblyMetadata(tpaPath);
            }
        }

        protected override Assembly? Load(AssemblyName assemblyName)
        {
            if (assemblyName?.Name == null)
            {
                return null;
            }

            if (!string.IsNullOrEmpty(assemblyName.FullName) && _managedAssemblies.TryGetValue(assemblyName.FullName, out var fullPath) && File.Exists(fullPath))
            {
                return LoadFromAssemblyPath(fullPath);
            }

            if (_managedAssemblies.TryGetValue(assemblyName.Name, out var path) && File.Exists(path))
            {
                return LoadFromAssemblyPath(path);
            }

            try
            {
                return AssemblyLoadContext.Default.LoadFromAssemblyName(assemblyName);
            }
            catch
            {
                return null;
            }
        }
        private static IEnumerable<string> EnumerateTrustedPlatformAssemblies()
        {
            if (AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES") is not string tpa || string.IsNullOrWhiteSpace(tpa))
            {
                yield break;
            }

            foreach (var path in tpa.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries))
            {
                var candidate = path.Trim();
                if (!string.IsNullOrEmpty(candidate))
                {
                    yield return candidate;
                }
            }
        }
        protected override IntPtr LoadUnmanagedDll(string unmanagedDllName)
        {
            if (string.IsNullOrWhiteSpace(unmanagedDllName))
            {
                return IntPtr.Zero;
            }

            DebugLog($"[DEBUG] Loading native DLL: {unmanagedDllName}");
            DebugLog($"[DEBUG] Native search paths count: {_nativeSearchPaths.Count}");
            DebugLog($"[DEBUG] Native libraries dictionary count: {_nativeLibraries.Count}");

            // Generate all possible name variations
            var nameVariations = new List<string> { unmanagedDllName };

            // Add version with .dll extension
            if (!unmanagedDllName.EndsWith(".dll", StringComparison.OrdinalIgnoreCase))
            {
                nameVariations.Add(unmanagedDllName + ".dll");
            }

            // Add version with "lib" prefix if not already present
            if (!unmanagedDllName.StartsWith("lib", StringComparison.OrdinalIgnoreCase))
            {
                nameVariations.Add("lib" + unmanagedDllName);
                nameVariations.Add("lib" + unmanagedDllName + ".dll");
            }

            DebugLog($"[DEBUG] Trying name variations: {string.Join(", ", nameVariations)}");

            // Try dictionary lookup with all name variations
            foreach (var name in nameVariations)
            {
                if (_nativeLibraries.TryGetValue(name, out var path) && File.Exists(path))
                {
                    DebugLog($"[DEBUG] Found in dictionary: {name} -> {path}");
                    try
                    {
                        if (NativeLibrary.TryLoad(path, out var handle))
                        {
                            DebugLog($"[DEBUG] Successfully loaded: {path}");
                            return handle;
                        }
                        DebugLog($"[DEBUG] NativeLibrary.TryLoad failed for: {path}");
                    }
                    catch (Exception ex)
                    {
                        DebugLog($"[DEBUG] Exception loading {path}: {ex.Message}");
                    }
                }
            }

            // Search through all native search paths with all name variations
            foreach (var searchPath in _nativeSearchPaths)
            {
                foreach (var name in nameVariations)
                {
                    var candidatePath = Path.Combine(searchPath, name);
                    if (File.Exists(candidatePath))
                    {
                        DebugLog($"[DEBUG] Found in search path: {candidatePath}");
                        try
                        {
                            if (NativeLibrary.TryLoad(candidatePath, out var handle))
                            {
                                DebugLog($"[DEBUG] Successfully loaded: {candidatePath}");
                                return handle;
                            }
                            DebugLog($"[DEBUG] NativeLibrary.TryLoad failed for: {candidatePath}");
                        }
                        catch (Exception ex)
                        {
                            DebugLog($"[DEBUG] Exception loading {candidatePath}: {ex.Message}");
                        }
                    }
                }
            }

            DebugLog($"[DEBUG] Trying base.LoadUnmanagedDll for: {unmanagedDllName}");
            try
            {
                var result = base.LoadUnmanagedDll(unmanagedDllName);
                if (result != IntPtr.Zero)
                {
                    DebugLog($"[DEBUG] base.LoadUnmanagedDll succeeded");
                }
                return result;
            }
            catch (Exception ex)
            {
                DebugLog($"[DEBUG] base.LoadUnmanagedDll failed: {ex.Message}");
                return IntPtr.Zero;
            }
        }
    }

    internal sealed class EntryPointExecutor
    {
        private readonly MethodInfo _entryPoint;
        private readonly string[] _arguments;

        public EntryPointExecutor(MethodInfo entryPoint, string[] arguments)
        {
            _entryPoint = entryPoint ?? throw new ArgumentNullException(nameof(entryPoint));
            _arguments = arguments ?? Array.Empty<string>();
        }

        public async Task<int> ExecuteAsync()
        {
            try
            {
                var result = _entryPoint.Invoke(null, BuildArguments());
                return await HandleReturnAsync(result).ConfigureAwait(false);
            }
            catch (TargetInvocationException ex) when (ex.InnerException != null)
            {
                throw ex.InnerException;
            }
        }

        public Task<int> RunStaAsync()
        {
            var tcs = new TaskCompletionSource<int>(TaskCreationOptions.RunContinuationsAsynchronously);
            var thread = new Thread(() =>
            {
                try
                {
                    var result = ExecuteAsync().GetAwaiter().GetResult();
                    tcs.TrySetResult(result);
                }
                catch (Exception ex)
                {
                    tcs.TrySetException(ex);
                }
            })
            {
                IsBackground = true,
                Name = "SharpPad.ExecutionHost.STA"
            };

            try
            {
                thread.SetApartmentState(ApartmentState.STA);
            }
            catch (PlatformNotSupportedException)
            {
                return ExecuteAsync();
            }

            thread.Start();
            return tcs.Task;
        }

        private object[] BuildArguments()
        {
            var parameters = _entryPoint.GetParameters();
            if (parameters.Length == 0)
            {
                return Array.Empty<object>();
            }

            if (parameters.Length == 1 && parameters[0].ParameterType == typeof(string[]))
            {
                return new object[] { _arguments };
            }

            throw new InvalidOperationException("Unsupported Main method signature.");
        }

        private static async Task<int> HandleReturnAsync(object? result)
        {
            switch (result)
            {
                case null:
                    return 0;
                case int exitCode:
                    return exitCode;
                case Task<int> taskWithExitCode:
                    return await taskWithExitCode.ConfigureAwait(false);
                case Task task:
                    await task.ConfigureAwait(false);
                    return 0;
                default:
                    return 0;
            }
        }
    }

    internal sealed class ConsoleRedirection : IDisposable
    {
        private readonly TextWriter _originalOut;
        private readonly TextWriter _originalError;
        private readonly TextReader _originalIn;
        private readonly StreamWriter _stdoutWriter;
        private readonly StreamWriter _stderrWriter;
        private readonly PromptingTextReader? _promptReader;

        public ConsoleRedirection(string promptMessage)
        {
            _originalOut = Console.Out;
            _originalError = Console.Error;
            _originalIn = Console.In;

            _stdoutWriter = new StreamWriter(Console.OpenStandardOutput(), Encoding.UTF8) { AutoFlush = true };
            _stderrWriter = new StreamWriter(Console.OpenStandardError(), Encoding.UTF8) { AutoFlush = true };
            Console.SetOut(_stdoutWriter);
            Console.SetError(_stderrWriter);

            _promptReader = new PromptingTextReader(_originalIn, promptMessage);
            Console.SetIn(_promptReader);
        }

        public void Dispose()
        {
            Console.SetOut(_originalOut);
            Console.SetError(_originalError);
            Console.SetIn(_originalIn);

            _promptReader?.Dispose();
            _stdoutWriter.Dispose();
            _stderrWriter.Dispose();
        }
    }

    internal sealed class PromptingTextReader : TextReader
    {
        private readonly TextReader _inner;
        private readonly string _promptMessage;
        private int _promptDisplayed;

        public PromptingTextReader(TextReader inner, string promptMessage)
        {
            _inner = inner ?? throw new ArgumentNullException(nameof(inner));
            _promptMessage = promptMessage ?? throw new ArgumentNullException(nameof(promptMessage));
        }

        public override int Peek()
        {
            EnsurePrompt();
            var value = _inner.Peek();
            if (value < 0)
            {
                ResetPrompt();
            }
            return value;
        }

        public override int Read()
        {
            EnsurePrompt();
            var value = _inner.Read();
            if (value == -1 || value == '\n')
            {
                ResetPrompt();
            }
            return value;
        }

        public override int Read(char[] buffer, int index, int count)
        {
            EnsurePrompt();
            var read = _inner.Read(buffer, index, count);
            if (read <= 0 || buffer[index + read - 1] == '\n')
            {
                ResetPrompt();
            }
            return read;
        }

        public override async Task<int> ReadAsync(char[] buffer, int index, int count)
        {
            EnsurePrompt();
            var read = await _inner.ReadAsync(buffer, index, count).ConfigureAwait(false);
            if (read <= 0 || buffer[index + read - 1] == '\n')
            {
                ResetPrompt();
            }
            return read;
        }

        public override string? ReadLine()
        {
            EnsurePrompt();
            var line = _inner.ReadLine();
            ResetPrompt();
            return line;
        }

        public override async Task<string?> ReadLineAsync()
        {
            EnsurePrompt();
            try
            {
                var line = await _inner.ReadLineAsync().ConfigureAwait(false);
                ResetPrompt();
                return line;
            }
            catch (Exception)
            {
                ResetPrompt();
                throw;
            }
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                ResetPrompt();
            }
            base.Dispose(disposing);
        }

        private void EnsurePrompt()
        {
            if (Interlocked.CompareExchange(ref _promptDisplayed, 1, 0) == 0)
            {
                Console.Out.Write(_promptMessage);
                Console.Out.Flush();
            }
        }

        private void ResetPrompt()
        {
            Interlocked.Exchange(ref _promptDisplayed, 0);
        }
    }
}
