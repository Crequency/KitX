using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace KitX_Dashboard.Models
{
    internal static class EventHandlers
    {

        internal delegate void LanguageChangedHandler();

        internal static event LanguageChangedHandler? LanguageChanged;

        /// <summary>
        /// 必要的初始化
        /// </summary>
        internal static void Init()
        {
            LanguageChanged += () => { };
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
            }
        }
    }
}
