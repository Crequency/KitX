using Serilog.Events;

namespace KitX_Dashboard.Models
{
    internal class SurpportLogLevel
    {
        internal LogEventLevel LogEventLevel { get; set; }

        internal string? LogLevelName { get; set; }

        internal string? LogLevelDisplayName { get; set; }
    }
}
