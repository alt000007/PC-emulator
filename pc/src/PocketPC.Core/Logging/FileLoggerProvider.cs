using Microsoft.Extensions.Logging;

namespace PocketPC.Core.Logging;

/// <summary>
/// Minimal append-only log file (rotated at 2 MB) so "it didn't work" can be diagnosed.
/// Never log secrets: pairing codes, PINs and passwords are not passed to loggers anywhere.
/// </summary>
public sealed class FileLoggerProvider(string path, LogLevel minimumLevel = LogLevel.Information) : ILoggerProvider
{
    private const long MaxBytes = 2 * 1024 * 1024;
    private readonly object _lock = new();
    private readonly LogLevel _minimumLevel = minimumLevel;

    public ILogger CreateLogger(string categoryName) => new FileLogger(this, categoryName);

    public void Dispose()
    {
    }

    private void Write(string line)
    {
        lock (_lock)
        {
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(path)!);
                if (File.Exists(path) && new FileInfo(path).Length > MaxBytes)
                {
                    File.Move(path, path + ".old", overwrite: true);
                }

                File.AppendAllText(path, line + Environment.NewLine);
            }
            catch (IOException)
            {
                // Logging must never crash the app.
            }
        }
    }

    private sealed class FileLogger(FileLoggerProvider owner, string category) : ILogger
    {
        public IDisposable? BeginScope<TState>(TState state)
            where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => logLevel >= owner._minimumLevel;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            if (!IsEnabled(logLevel))
            {
                return;
            }

            var shortCategory = category[(category.LastIndexOf('.') + 1)..];
            var line = $"{DateTimeOffset.Now:yyyy-MM-dd HH:mm:ss} [{logLevel}] {shortCategory}: {formatter(state, exception)}";
            if (exception is not null)
            {
                line += Environment.NewLine + "    " + exception.GetType().Name + ": " + exception.Message;
            }

            owner.Write(line);
        }
    }
}
