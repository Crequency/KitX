using KitX.Core.Contract.Configuration;

namespace KitX.Core.Configuration;

/// <summary>
/// Log configuration section
/// </summary>
public class Config_Log : ILogConf
{
    public long LogFileSingleMaxSize { get; set; } = 1024 * 1024 * 10; //  10MB

    public string LogFilePath { get; set; } = "./Log/";

    public string LogTemplate { get; set; } = "[{Timestamp:yyyy-MM-dd HH:mm:ss.fff zzz}] [{Level:u3}] {Message:lj}{NewLine}{Exception}";

    public int LogFileMaxCount { get; set; } = 50;

    public int LogFileFlushInterval { get; set; } = 30;

#if DEBUG

    public LogLevel LogLevel { get; set; } = LogLevel.Information;

#else

    public LogLevel LogLevel { get; set; } = LogLevel.Warning;

#endif
}