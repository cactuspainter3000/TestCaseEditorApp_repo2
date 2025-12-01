using Microsoft.Extensions.Logging;
using System;
using System.IO;
using System.Text;

namespace TestCaseEditorApp.Services.Logging
{
    internal class FileLogger : ILogger
    {
        private readonly string _name;
        private readonly string _filePath;
        private static readonly object _sync = new object();

        public FileLogger(string name, string filePath)
        {
            _name = name;
            _filePath = filePath;
            try { Directory.CreateDirectory(Path.GetDirectoryName(_filePath) ?? Path.GetTempPath()); } catch { }
        }

        public IDisposable? BeginScope<TState>(TState state) => null;

        public bool IsEnabled(LogLevel logLevel) => logLevel != LogLevel.None;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            if (!IsEnabled(logLevel)) return;
            try
            {
                var line = new StringBuilder();
                line.Append(DateTime.UtcNow.ToString("o"));
                line.Append('\t');
                line.Append(logLevel.ToString());
                line.Append('\t');
                line.Append(_name);
                line.Append('\t');
                line.Append(formatter(state, exception));
                if (exception != null)
                {
                    line.Append("\tEX:");
                    line.Append(exception.ToString());
                }
                line.Append(Environment.NewLine);

                lock (_sync)
                {
                    File.AppendAllText(_filePath, line.ToString(), Encoding.UTF8);
                }
            }
            catch
            {
                // swallow logging failures - logging should not crash the app
            }
        }
    }

    internal class FileLoggerProvider : ILoggerProvider
    {
        private readonly string _basePath;

        public FileLoggerProvider(string basePath)
        {
            _basePath = basePath;
            try { Directory.CreateDirectory(_basePath); } catch { }
        }

        public ILogger CreateLogger(string categoryName)
        {
            var file = Path.Combine(_basePath, "app.log");
            return new FileLogger(categoryName, file);
        }

        public void Dispose() { }
    }
}
