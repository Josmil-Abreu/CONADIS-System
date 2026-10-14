using System.Collections.Concurrent;
using System.Reflection;
using log4net;
using Microsoft.Extensions.Logging;

namespace CONADIS.Integracion.Negocio.Logging;

/// <summary>
/// Puente entre el ILogger&lt;T&gt; de ASP.NET Core y Log4Net: cualquier log de la API
/// (los nuestros y los internos de ASP.NET Core) termina en el archivo y en Log4NetLog.
/// Uso en Program.cs:  builder.Logging.ClearProviders().AddProvider(new ProveedorLog4Net());
/// </summary>
public sealed class ProveedorLog4Net : ILoggerProvider
{
    private readonly ConcurrentDictionary<string, LoggerLog4Net> _loggers = new();

    public ILogger CreateLogger(string categoryName)
        => _loggers.GetOrAdd(categoryName, nombre => new LoggerLog4Net(
            LogManager.GetLogger(Assembly.GetEntryAssembly() ?? typeof(ProveedorLog4Net).Assembly, nombre)));

    public void Dispose() => _loggers.Clear();

    private sealed class LoggerLog4Net(ILog log) : ILogger
    {
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => logLevel switch
        {
            LogLevel.Trace or LogLevel.Debug => log.IsDebugEnabled,
            LogLevel.Information => log.IsInfoEnabled,
            LogLevel.Warning => log.IsWarnEnabled,
            LogLevel.Error => log.IsErrorEnabled,
            LogLevel.Critical => log.IsFatalEnabled,
            _ => false
        };

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state,
            Exception? exception, Func<TState, Exception?, string> formatter)
        {
            if (!IsEnabled(logLevel)) return;
            var mensaje = formatter(state, exception);
            if (string.IsNullOrEmpty(mensaje) && exception is null) return;

            switch (logLevel)
            {
                case LogLevel.Trace:
                case LogLevel.Debug: log.Debug(mensaje, exception); break;
                case LogLevel.Information: log.Info(mensaje, exception); break;
                case LogLevel.Warning: log.Warn(mensaje, exception); break;
                case LogLevel.Error: log.Error(mensaje, exception); break;
                case LogLevel.Critical: log.Fatal(mensaje, exception); break;
            }
        }
    }
}