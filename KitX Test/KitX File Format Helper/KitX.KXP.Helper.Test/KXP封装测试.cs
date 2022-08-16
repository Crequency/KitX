using KitX.Web.Rules;
using System.Text.Json;

namespace KitX.KXP.Helper.Test
{
    [TestClass]
    public class KXP·â×°²âÊÔ
    {
        [TestMethod]
        public void »ù×¼²âÊÔ()
        {
            string baseDir = @"D:\tmp\";
            Encoder encoder = new()
            {
                Files2Include = new()
                {
                    $"{baseDir}KitX.Contract.CSharp.dll",
                    $"{baseDir}TestPlugin.WPF.Core.deps.json",
                    $"{baseDir}TestPlugin.WPF.Core.dll"
                },
                LoaderStruct = JsonSerializer.Serialize(new LoaderStruct()
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
                })
            };
            encoder.Encode(@"D:\tmp\", @"D:\tmp\", @"test");
        }
    }
}
