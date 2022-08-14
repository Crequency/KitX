using System.Text.Json.Serialization;

namespace KitX.Web.Rules
{
    /// <summary>
    /// 功能体
    /// </summary>
    public class Function
    {
        [JsonInclude]
        public Dictionary<string, Dictionary<string, string>> FunctionsDisplayName { get; set; }
            = new();

        [JsonInclude]
        public Dictionary<string, ParameterList> FunctionParameters { get; set; } = new();

        /// <summary>
        /// 参数列表
        /// </summary>
        public class ParameterList
        {
            [JsonInclude]
            public List<Parameter> ForeParameters { get; set; } = new();

            [JsonInclude]
            public bool HasAppendParameters { get; set; } = false;

            [JsonInclude]
            public string AppendParameterType { get; set; } = string.Empty;

            public class Parameter
            {

                [JsonInclude]
                public string? Name { get; set; } = string.Empty;

                [JsonInclude]
                public string? Type { get; set; } = string.Empty;
            }
        }
    }
}
