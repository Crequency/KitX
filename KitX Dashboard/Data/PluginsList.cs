using KitX_Dashboard.Models;
using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace KitX_Dashboard.Data
{
    public class PluginsList
    {
        [JsonInclude]
        public List<Plugin> Plugins = new();
    }
}
