using KitX.Web.Rules;
using System.Collections.Generic;

namespace KitX_Dashboard.Models
{
    internal class Plugin
    {
        public Plugin()
        {

        }

        internal PluginStruct? PluginDetails { get; set; }

        internal List<string>? MacAddressOfInstalledDevice { get; set; }

        internal string? RequiredLoaderName { get; set; }

        internal string? RequiredLoaderVersion { get; set; }
    }
}
