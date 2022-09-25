using KitX.Web.Rules;
using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace KitX_Dashboard.Models
{
    public class Plugin
    {
        [JsonInclude]
        /// <summary>
        /// 该插件的详细信息
        /// </summary>
        public PluginStruct PluginDetails { get; set; }

        [JsonInclude]
        /// <summary>
        /// 需要的加载器的详细信息
        /// </summary>
        public LoaderStruct RequiredLoaderStruct { get; set; }

        [JsonInclude]
        /// <summary>
        /// 已安装此插件的网络设备
        /// </summary>
        public List<string>? MacAddressOfInstalledDevice { get; set; }
    }
}

//      ,---,---,---,---,---,---,---,---,---,---,---,---,---,-------,
//      | ~ | 1 | 2 | 3 | 4 | 5 | 6 | 7 | 8 | 9 | 0 | [ | ] | <-    |
//      |---'-,-'-,-'-,-'-,-'-,-'-,-'-,-'-,-'-,-'-,-'-,-'-,-'-,-----|
//      | ->| | " | , | . | P | Y | F | G | C | R | L | / | = |  \  |
//      |-----',--',--',--',--',--',--',--',--',--',--',--',--'-----|
//      | Caps | A | O | E | U | I | D | H | T | N | S | - |  Enter |
//      |------'-,-'-,-'-,-'-,-'-,-'-,-'-,-'-,-'-,-'-,-'-,-'--------|
//      |        | ; | Q | J | K | X | B | M | W | V | Z |          |
//      |------,-',--'--,'---'---'---'---'---'---'-,-'---',--,------|
//      | ctrl |  | alt |                          | alt  |  | ctrl |
//      '------'  '-----'--------------------------'------'  '------'