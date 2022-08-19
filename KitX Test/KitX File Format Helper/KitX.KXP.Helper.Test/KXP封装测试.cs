using KitX.Web.Rules;
using System.Text.Json;

namespace KitX.KXP.Helper.Test
{
    [TestClass]
    public class KXP封装测试
    {
        [TestMethod]
        public void 基准测试()
        {
            string baseDir = @"D:\tmp\";
            Encoder encoder = new(new()
                {
                    $"{baseDir}KitX.Contract.CSharp.dll",
                    $"{baseDir}TestPlugin.WPF.Core.deps.json",
                    $"{baseDir}TestPlugin.WPF.Core.dll"
                },
                JsonSerializer.Serialize(new LoaderStruct()
                {
                    LoaderName = "KitX.Loader.WPF.Core",
                    LoaderVersion = "v1.0.0",
                    LoaderFramework = "WPF.Core",
                    LoaderLanguage = "C#",
                    LoaderRunType = LoaderStruct.RunType.Desktop,
                    SupportOS = new()
                    {
                        OperatingSystems.Windows
                    }
                }),
                JsonSerializer.Serialize(new PluginStruct()
                {
                    AuthorLink = "AuthorLink",
                    AuthorName = "AuthorName",
                    ComplexDescription = "ComplexDescription",
                    DisplayName = "DisplayName",
                    Functions = new()
                    {
                        FunctionsDisplayName = new()
                        {
                            {
                                "FunctionA",
                                new()
                                {
                                    { "zh-cn", "某个方法" },
                                    { "zh-cnt", "某個方法" },
                                    { "en-us", "One Function" }
                                }
                            }
                        },
                        FunctionParameters = new()
                        {
                            {
                                "FunctionA",
                                new()
                                {
                                    ForeParameters = new()
                                    {
                                        new()
                                        {
                                            Name = "ParameterA",
                                            DisplayName = new()
                                            {
                                                { "zh-cn", "参数A" }
                                            },
                                            Type = "string"
                                        }
                                    },
                                    HasAppendParameters = false,
                                    ReturnValueType = "void"
                                }
                            }
                        }
                    },
                    IconInBase64 = "IconInBase64",
                    IsMarketVersion = false,
                    LastUpdateDate = DateTime.Now,
                    Name = "Name",
                    PublishDate = DateTime.Now,
                    PublisherLink = "PublisherLink",
                    PublisherName = "PublisherName",
                    SimpleDescription = "SimpleDescription",
                    Tags = new(),
                    TotalDescriptionInMarkdown = "TotalDescriptionInMarkdown",
                    Version = "Version",
                    RootStartupFileName = "RootStartupFileName"
                })
            );
            encoder.Encode(@"D:\tmp\", @"D:\tmp\", @"test");
        }
    }
}
