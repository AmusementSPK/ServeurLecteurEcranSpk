using System.Collections.Concurrent;

public sealed class DailyFileLoggerProvider : ILoggerProvider
{
    private readonly DisplayServerPaths _paths;
    private readonly object _gate = new();
    private readonly ConcurrentDictionary<string, DailyFileLogger> _loggers = new();

    public DailyFileLoggerProvider(DisplayServerPaths paths)
    {
        _paths = paths;
        CleanupOldLogs();
    }

    public ILogger CreateLogger(string categoryName) =>
        _loggers.GetOrAdd(categoryName, category => new DailyFileLogger(this, category));

    internal void Write(LogLevel level, string category, EventId eventId, string message, Exception? exception)
    {
        if (level < LogLevel.Information) return;
        var now = DateTimeOffset.Now;
        var path = Path.Combine(_paths.LogsRoot, $"display-server-{now:yyyy-MM-dd}.log");
        var line = $"{now:yyyy-MM-dd HH:mm:ss.fff zzz} [{level}] {category} " +
            $"{(eventId.Id != 0 ? $"({eventId.Id}) " : "")}{message}";
        if (exception is not null) line += Environment.NewLine + exception;
        try
        {
            lock (_gate)
            {
                Directory.CreateDirectory(_paths.LogsRoot);
                File.AppendAllText(path, line + Environment.NewLine);
            }
        }
        catch { }
    }

    private void CleanupOldLogs()
    {
        try
        {
            foreach (var file in Directory.EnumerateFiles(_paths.LogsRoot, "display-server-*.log"))
                if (File.GetLastWriteTimeUtc(file) < DateTime.UtcNow.AddDays(-30)) File.Delete(file);
        }
        catch { }
    }

    public void Dispose() => _loggers.Clear();

    private sealed class DailyFileLogger : ILogger
    {
        private readonly DailyFileLoggerProvider _provider;
        private readonly string _category;
        public DailyFileLogger(DailyFileLoggerProvider provider, string category) { _provider = provider; _category = category; }
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel logLevel) => logLevel >= LogLevel.Information;
        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            if (IsEnabled(logLevel)) _provider.Write(logLevel, _category, eventId, formatter(state, exception), exception);
        }
    }
}
