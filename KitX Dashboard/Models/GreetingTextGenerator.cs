using System;

namespace KitX_Dashboard.Models
{
    internal class GreetingTextGenerator
    {
        public GreetingTextGenerator()
        {

        }

        internal static string? GetKey()
        {
            Random random = new();
            string key = $"Text_Greeting_%Step%_%Index%";
            int time = DateTime.Now.Hour;
            if (time >= 6 && time < 12)
                key = key.Replace("%Step%", "Morning").Replace("%Index%",
                    random.Next(1, Program.GlobalConfig.Config_Windows
                    .Config_MainWindow.GreetingTextCount_Morning).ToString());
            else if (time >= 12 && time < 14)
                key = key.Replace("%Step%", "Noon").Replace("%Index%",
                    random.Next(1, Program.GlobalConfig.Config_Windows
                    .Config_MainWindow.GreetingTextCount_Noon).ToString());
            else if (time >= 14 && time < 18)
                key = key.Replace("%Step%", "AfterNoon").Replace("%Index%",
                    random.Next(1, Program.GlobalConfig.Config_Windows
                    .Config_MainWindow.GreetingTextCount_AfterNoon).ToString());
            else if (time >= 18 && time < 24)
                key = key.Replace("%Step%", "Evening").Replace("%Index%",
                    random.Next(1, Program.GlobalConfig.Config_Windows
                    .Config_MainWindow.GreetingTextCount_Evening).ToString());
            else key = key.Replace("%Step%", "Night").Replace("%Index%",
                    random.Next(1, Program.GlobalConfig.Config_Windows
                    .Config_MainWindow.GreetingTextCount_Night).ToString());
            return key;
        }
    }
}