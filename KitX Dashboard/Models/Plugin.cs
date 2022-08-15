using KitX.Web.Rules;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

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
