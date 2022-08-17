using System.Collections.Generic;

namespace KitX.Web.Rules
{
    /// <summary>
    /// 功能体
    /// </summary>
    public class Function
    {
        public Dictionary<string, Dictionary<string, string>> FunctionsDisplayName { get; set; }
            = new Dictionary<string, Dictionary<string, string>>();

        public Dictionary<string, ParameterList> FunctionParameters { get; set; }
            = new Dictionary<string, ParameterList>();

        /// <summary>
        /// 参数列表
        /// </summary>
        public class ParameterList
        {
            public List<Parameter> ForeParameters { get; set; } = new List<Parameter>();

            public bool HasAppendParameters { get; set; } = false;

            public string AppendParameterType { get; set; } = string.Empty;

            public class Parameter
            {
                public string Name { get; set; } = string.Empty;

                public string Type { get; set; } = string.Empty;
            }
        }
    }
}
