using System.Collections.Generic;

namespace KitX.Web.Rules
{
    /// <summary>
    /// 功能体
    /// </summary>
    public class Function
    {
        /// <summary>
        /// 方法们的显示名称
        /// </summary>
        /// <inheritdoc>
        /// FunctionsDisplayName 的 Key 代表内部方法名, 后面的字典对应不同语言的方法显示名
        /// 如果客户端选择的语言文件没有对应的语言代码, 则使用内部方法名
        /// 例如:
        /// { "zh-cn", "获取最新验证码" }
        /// { "zh-cnt", "獲取最新校驗碼" }
        /// { "en-us", "Get latest verify code" }
        /// </inheritdoc>
        public Dictionary<string, Dictionary<string, string>> FunctionsDisplayName { get; set; }

        /// <summary>
        /// 方法参数列表
        /// </summary>
        public Dictionary<string, ParameterList> FunctionParameters { get; set; }

        /// <summary>
        /// 参数列表
        /// </summary>
        public class ParameterList
        {
            /// <summary>
            /// 强制参数列表
            /// </summary>
            public List<Parameter> ForeParameters { get; set; }

            /// <summary>
            /// 是否有后继重复参数
            /// </summary>
            public bool HasAppendParameters { get; set; }

            /// <summary>
            /// 追加的参数类型
            /// </summary>
            public string AppendParameterType { get; set; }

            /// <summary>
            /// 返回值类型
            /// </summary>
            public string ReturnValueType { get; set; }

            /// <summary>
            /// 单个参数结构
            /// </summary>
            public class Parameter
            {
                /// <summary>
                /// 参数名称
                /// </summary>
                public string Name { get; set; }

                /// <summary>
                /// 参数显示名称
                /// </summary>
                public Dictionary<string, string> DisplayName { get; set; }

                /// <summary>
                /// 参数类型
                /// </summary>
                /// <inheritdoc>
                /// 1.  string      字符串
                /// 2.  int32       32位整数
                /// 3.  int64       64位整数
                /// 4.  char        字符
                /// 5.  blob        二进制数据
                /// 6.  bool        布尔值
                /// </inheritdoc>
                public string Type { get; set; }
            }
        }
    }
}
