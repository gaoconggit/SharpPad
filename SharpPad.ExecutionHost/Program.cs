using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Runtime.Loader;
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

            try
            {
                if (!string.IsNullOrEmpty(options.WorkingDirectory) && Directory.Exists(options.WorkingDirectory))
                {
                    Directory.SetCurrentDirectory(options.WorkingDirectory);
                }
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"Failed to set working directory: {ex.Message}");
            }

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
        private readonly Dictionary<string, string> _managedAssemblies;
        private readonly Dictionary<string, string> _nativeLibraries;

        public ExecutionLoadContext(string? assemblyDirectory) : base(isCollectible: true)
        {
            _managedAssemblies = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            _nativeLibraries = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

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
                        RegisterAssembly(assemblyName.Name, file);
                    }
                }
                catch
                {
                    // Not a managed assembly; ignore metadata failures.
                }
            }

            if (!string.IsNullOrWhiteSpace(assemblyDirectory) && Directory.Exists(assemblyDirectory))
            {
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
                        if (!string.IsNullOrEmpty(nameWithoutExtension))
                        {
                            _nativeLibraries[nameWithoutExtension] = file;
                        }

                        if (!string.IsNullOrEmpty(fileName))
                        {
                            _nativeLibraries[fileName] = file;
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

            if (_nativeLibraries.TryGetValue(unmanagedDllName, out var directPath) && File.Exists(directPath))
            {
                return LoadUnmanagedDllFromPath(directPath);
            }

            var withExtension = unmanagedDllName + ".dll";
            if (_nativeLibraries.TryGetValue(withExtension, out var dllPath) && File.Exists(dllPath))
            {
                return LoadUnmanagedDllFromPath(dllPath);
            }

            return base.LoadUnmanagedDll(unmanagedDllName);
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

            _stdoutWriter = new StreamWriter(Console.OpenStandardOutput()) { AutoFlush = true };
            _stderrWriter = new StreamWriter(Console.OpenStandardError()) { AutoFlush = true };
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
            var line = await _inner.ReadLineAsync().ConfigureAwait(false);
            ResetPrompt();
            return line;
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
