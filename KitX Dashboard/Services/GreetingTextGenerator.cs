using System;

namespace KitX_Dashboard.Services
{
    internal class GreetingTextGenerator
    {
        public GreetingTextGenerator()
        {

        }

        internal static int PreviousIndex = 0;
        internal static Random random = new();

        internal static string? GetKey()
        {
            string key = $"Text_Greeting_%Step%_%Index%";
            int time = DateTime.Now.Hour;
            if (time >= 6 && time < 12)
                key = key.Replace("%Step%", "Morning")
                    .Replace("%Index%", GenerateRandomIndex(Step.Morning).ToString());
            else if (time >= 12 && time < 14)
                key = key.Replace("%Step%", "Noon")
                    .Replace("%Index%", GenerateRandomIndex(Step.Noon).ToString());
            else if (time >= 14 && time < 18)
                key = key.Replace("%Step%", "AfterNoon")
                    .Replace("%Index%", GenerateRandomIndex(Step.AfterNoon).ToString());
            else if (time >= 18 && time < 24)
                key = key.Replace("%Step%", "Evening")
                    .Replace("%Index%", GenerateRandomIndex(Step.Evening).ToString());
            else key = key.Replace("%Step%", "Night")
                    .Replace("%Index%", GenerateRandomIndex(Step.Night).ToString());
            return key;
        }

        internal enum Step
        {
            Morning, Noon, AfterNoon, Evening, Night
        }

        internal static int GenerateRandomIndex(Step step)
        {
            int result = PreviousIndex;
            while (result == PreviousIndex)
            {
                switch (step)
                {
                    case Step.Morning:
                        result = random.Next(1, Program.Config.Windows
                            .MainWindow.GreetingTextCount_Morning + 1);
                        break;
                    case Step.Noon:
                        result = random.Next(1, Program.Config.Windows
                            .MainWindow.GreetingTextCount_Noon + 1);
                        break;
                    case Step.AfterNoon:
                        result = random.Next(1, Program.Config.Windows
                            .MainWindow.GreetingTextCount_AfterNoon + 1);
                        break;
                    case Step.Evening:
                        result = random.Next(1, Program.Config.Windows
                            .MainWindow.GreetingTextCount_Evening + 1);
                        break;
                    case Step.Night:
                        result = random.Next(1, Program.Config.Windows
                            .MainWindow.GreetingTextCount_Night + 1);
                        break;
                }
            }
            PreviousIndex = result;
            return result;
        }
    }
}

//
//               .                                                                
//               |\                                                               
//               | \                                                              
//               |  \                                                             
//               |  |                                                             
//               |  |)                                                            
//               |  | )                                                           
//               |  |_)                                                           
//              /|  / /                                                           
//             /_| /_/|                                                           
//             | | | ||                                                           
//             | | | ||                                                           
//             | | | ||\                                                          
//            /| | | || \                                                         
//           / | | | ||  \                                                        
//          /  | | | ||   \                                                       
//         /   | | | ||    \                                                      
//        /    | | | ||     \                                                     
//       /     | | | ||      \                                                    
//       \  ___| | | ||___   /                                                    
//        --   | | | ||\  ---                                                     
//            /| | | |\  \                                                        
//           / \_/ \_/ \___|                                                      
//          /  |     |  \                                                         
//          \__|     |__/ -cs  
//
