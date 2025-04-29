using NLog;

namespace RemoteSignTool.Server.Logging;

/// <summary>
/// Provides helper methods for logging.
/// </summary>
public static class LoggingHelper
{
    /// <summary>
    /// Gets the logger for the specified type.
    /// </summary>
    /// <typeparam name="T">The type to get the logger for.</typeparam>
    /// <returns>The logger instance.</returns>
    public static Logger GetLogger<T>()
    {
        return LogManager.GetLogger(typeof(T).FullName);
    }
}
