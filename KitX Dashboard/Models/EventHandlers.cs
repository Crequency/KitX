using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

#pragma warning disable CS8602 // 解引用可能出现空引用。

namespace KitX_Dashboard.Models
{
    internal static class EventHandlers
    {

        internal delegate void LanguageChangedHandler();

        internal delegate void GreetingTextIntervalUpdatedHandler();

        internal static event LanguageChangedHandler? LanguageChanged;

        internal static event GreetingTextIntervalUpdatedHandler? GreetingTextIntervalUpdated;



        /// <summary>
        /// 必要的初始化
        /// </summary>
        internal static void Init()
        {
            LanguageChanged += () => { };
            GreetingTextIntervalUpdated += () => { };
        }

        /// <summary>
        /// 执行全局事件
        /// </summary>
        /// <param name="eventName">事件名称</param>
        internal static void Invoke(string eventName)
        {
            switch (eventName)
            {
                case "LanguageChanged":
                    LanguageChanged();
                    break;
                case "GreetingTextIntervalUpdated":
                    GreetingTextIntervalUpdated();
                    break;
            }
        }
    }
}

#pragma warning restore CS8602 // 解引用可能出现空引用。
