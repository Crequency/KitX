using KitX.Contract.CSharp;
using KitX.Web.Rules;
using System.Collections.Generic;

namespace TestPlugin.WPF.Core
{
    public class Controller : IController
    {
        private readonly MainWindow mainwin;

        public Controller(MainWindow mainwin)
        {
            this.mainwin = mainwin;
        }

        public void End()
        {
            mainwin.Close();
        }

        public void Pause()
        {
            mainwin.Hide();
        }

        public void Start()
        {
            mainwin.Show();
        }

        public Function GetFunctions()
        {
            return new Function()
            {
                FunctionsDisplayName = new Dictionary<string, Dictionary<string, string>>()
                {
                    {
                        "Default",
                        new Dictionary<string, string>()
                        {
                            { "zh-cn", "嗨嗨嗨" }
                        }
                    }
                },
                FunctionParameters = new Dictionary<string, Function.ParameterList>()
                {
                    {
                        "Default",
                        new Function.ParameterList()
                        {
                            HasAppendParameters = false,
                            AppendParameterType = "",
                            ForeParameters = new List<Function.ParameterList.Parameter>()
                            {
                                new Function.ParameterList.Parameter()
                                {
                                    DisplayName = new Dictionary<string, string>()
                                    {
                                        { "zh-cn", "哈哈哈" }
                                    },
                                    Name = "hhh",
                                    Type = "void"
                                }
                            },
                            ReturnValueType = "void"
                        }
                    }
                }
            };
        }

        public int Execute(string cmd, object? arg = null)
        {

            return 0;
        }
    }
}
