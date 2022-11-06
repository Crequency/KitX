using KitX_Dashboard.Data;
using Serilog;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Timers;

namespace KitX_Dashboard.Services
{
    internal class StatisticsManager
    {
        internal static Dictionary<string, double>? UseStatistics = new();

        internal static void InitEvents()
        {
            EventHandlers.UseStatisticsChanged += async () =>
            {
                string dataDir = Path.GetFullPath(GlobalInfo.DataPath);
                if (!Directory.Exists(dataDir)) Directory.CreateDirectory(dataDir);

                #region 存储使用时长数据

                string useFile = "UseCount.json";
                string usePath = Path.GetFullPath($"{dataDir}/{useFile}");
                string json = JsonSerializer.Serialize(UseStatistics);
                await File.WriteAllTextAsync(usePath, json);

                #endregion

            };
        }

        internal static async void RecoverOldStatistics()
        {
            string dataDir = Path.GetFullPath(GlobalInfo.DataPath);
            if (!Directory.Exists(dataDir)) Directory.CreateDirectory(dataDir);

            #region 恢复使用时长的数据

            try
            {
                string useFile = "UseCount.json";
                string usePath = Path.GetFullPath($"{dataDir}/{useFile}");
                if (File.Exists(usePath))
                {
                    string useCountJson = await File.ReadAllTextAsync(usePath);
                    UseStatistics = JsonSerializer.Deserialize<Dictionary<string, double>>(useCountJson);
                    if(UseStatistics != null)
                    {
                        DateTime lastDT = DateTime.Parse(UseStatistics.Keys.Last());
                        while (!lastDT.ToString("MM.dd").Equals(DateTime.Now.ToString("MM.dd")))
                        {
                            lastDT = lastDT.AddDays(1);
                            UseStatistics.Add(lastDT.ToString("MM.dd"), 0);
                        }
                    }
                }
                else
                {
                    string today = DateTime.Now.ToString("MM.dd");
                    UseStatistics?.Add(today, 0);
                    string json = JsonSerializer.Serialize(UseStatistics);
                    await File.WriteAllTextAsync(usePath, json);
                }
            }
            catch (Exception e)
            {
                Log.Warning(e.Message, e);
            }

            #endregion
        }

        internal static void BeginRecord()
        {
            #region 记录使用时长

            Timer use_timer = new()
            {
                Interval = 1000 * 60 * 0.6    //  Update per 0.6 minutes
            };
            use_timer.Elapsed += (_, _) =>
            {
                string today = DateTime.Now.ToString("MM.dd");
                if (UseStatistics != null)
                {
                    if (UseStatistics.ContainsKey(today))
                    {
                        UseStatistics[today] += 0.01;
                        UseStatistics[today] = Math.Round(UseStatistics[today], 2);
                    }
                    else
                    {
                        UseStatistics.Add(today, 0.01);
                    }
                    EventHandlers.Invoke(nameof(EventHandlers.UseStatisticsChanged));
                }
            };
            use_timer.Start();

            #endregion
        }
    }
}
