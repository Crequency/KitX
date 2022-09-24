using Avalonia.Threading;
using BasicHelper.IO;
using KitX_Dashboard.Data;
using KitX_Dashboard.Views;
using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;

namespace KitX_Dashboard.Services
{
    internal class AnouncementManager
    {
        /// <summary>
        /// 异步检查新公告
        /// </summary>
        /// <returns>异步任务</returns>
        public static async Task CheckNewAnnouncements()
        {
            HttpClient client = new();  //  Http客户端
            client.DefaultRequestHeaders.Accept.Clear();    //  清除请求头部

            //  链接头部
            string linkBase = $"http://" +
                $"{Program.AppConfig.App.APIServer}" +
                $"{Program.AppConfig.App.APIPath}";

            //  获取公告列表的api链接
            string link = $"{linkBase}{GlobalInfo.Api_Get_Announcements}";

            //  公告列表
            string msg = await client.GetStringAsync(link);
            List<string>? list = JsonSerializer.Deserialize<List<string>>(msg);

            //  本地已阅列表
            List<string>? readed;
            string confPath = Path.GetFullPath(GlobalInfo.AnnouncementsJsonPath);
            if (File.Exists(confPath))
                readed = JsonSerializer.Deserialize<List<string>>(
                    await FileHelper.ReadAllAsync(confPath)
                );
            else
            {
                readed = new()
                {
                    "2022-05-02 11:54:29"
                };
            }

            //  未阅读列表
            List<DateTime> unreads = new();

            //  添加没有阅读的公告到未阅读列表
            if (list != null && readed != null)
            {
                foreach (var item in list)
                    if (!readed.Contains(item))
                        unreads.Add(DateTime.Parse(item));

                //  公告列表<发布时间, 公告内容>
                Dictionary<string, string> src = new();
                foreach (var item in unreads)
                {
                    //  获取单个公告的链接
                    string apiLink = $"{linkBase}{GlobalInfo.Api_Get_Announcement}" +
                        $"?" +
                        $"lang={Program.AppConfig.App.AppLanguage}" +
                        $"&" +
                        $"date={item:yyyy-MM-dd HH-mm}";
                    string? md = JsonSerializer.Deserialize<string>(await client.GetStringAsync(apiLink));
                    if (md != null)
                        src.Add(item.ToString("yyyy-MM-dd HH:mm"), md);
                }

                if (unreads.Count > 0)
                {
                    Dispatcher.UIThread.Post(() =>
                    {
                        var toast = new AnouncementsWindow();
                        toast.UpdateSource(src, readed);
                        toast.Show();
                    });
                }
            }

            //  结束Http客户端
            client.Dispose();
        }
    }
}
