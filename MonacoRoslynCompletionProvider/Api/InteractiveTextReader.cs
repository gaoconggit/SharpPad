using System;
using System.Collections.Concurrent;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace MonacoRoslynCompletionProvider.Api
{
    public class InteractiveTextReader : TextReader
    {
        private readonly ConcurrentQueue<string> _inputQueue = new();
        private readonly AutoResetEvent _inputAvailable = new(false);
        private readonly Func<string, Task> _onInputRequested;
        private bool _disposed = false;

        public InteractiveTextReader(Func<string, Task> onInputRequested)
        {
            _onInputRequested = onInputRequested;
        }

        public void ProvideInput(string input)
        {
            if (!_disposed)
            {
                _inputQueue.Enqueue(input ?? "");
                _inputAvailable.Set();
            }
        }

        public override string ReadLine()
        {
            if (_disposed)
                return null;

            // 请求输入
            _ = Task.Run(async () =>
            {
                try
                {
                    await _onInputRequested?.Invoke("INPUT_REQUIRED");
                }
                catch
                {
                    // 忽略异步调用中的异常
                }
            });

            // 等待输入
            while (!_disposed)
            {
                if (_inputQueue.TryDequeue(out string input))
                {
                    return input;
                }

                // 等待新输入，超时时间10分钟
                if (!_inputAvailable.WaitOne(TimeSpan.FromMinutes(10)))
                {
                    // 超时，返回空字符串
                    return "";
                }
            }

            return null;
        }

        public override int Read()
        {
            var line = ReadLine();
            if (line == null)
                return -1;

            if (line.Length == 0)
                return '\n';

            return line[0];
        }

        public override int Read(char[] buffer, int index, int count)
        {
            var line = ReadLine();
            if (line == null)
                return 0;

            line += Environment.NewLine;
            var bytes = Encoding.UTF8.GetBytes(line);
            var bytesToCopy = Math.Min(count, bytes.Length);
            
            for (int i = 0; i < bytesToCopy; i++)
            {
                buffer[index + i] = (char)bytes[i];
            }

            return bytesToCopy;
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing && !_disposed)
            {
                _disposed = true;
                _inputAvailable.Set(); // 释放等待的线程
                _inputAvailable.Dispose();
            }
            base.Dispose(disposing);
        }
    }
}