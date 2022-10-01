#pragma warning disable CS8602 // 解引用可能出现空引用。


namespace KitX_Dashboard.Services
{
    internal static class EventHandlers
    {

        internal delegate void LanguageChangedHandler();

        internal delegate void GreetingTextIntervalUpdatedHandler();

        internal delegate void ConfigSettingsChangedHandler();

        internal delegate void MicaOpacityChangedHandler();

        internal delegate void PluginsListChangedHandler();

        internal delegate void DevelopSettingsChangedHandler();

        internal delegate void LogConfigUpdatedHandler();

        internal static event LanguageChangedHandler? LanguageChanged;

        internal static event GreetingTextIntervalUpdatedHandler? GreetingTextIntervalUpdated;

        internal static event ConfigSettingsChangedHandler? ConfigSettingsChanged;

        internal static event MicaOpacityChangedHandler? MicaOpacityChanged;

        internal static event PluginsListChangedHandler? PluginsListChanged;

        internal static event DevelopSettingsChangedHandler? DevelopSettingsChanged;

        internal static event LogConfigUpdatedHandler? LogConfigUpdated;


        /// <summary>
        /// 必要的初始化
        /// </summary>
        internal static void Init()
        {
            LanguageChanged += () => { };
            GreetingTextIntervalUpdated += () => { };
            ConfigSettingsChanged += () => { };
            MicaOpacityChanged += () => { };
            PluginsListChanged += () => { };
            DevelopSettingsChanged += () => { };
            LogConfigUpdated += () => { };
        }

        /// <summary>
        /// 执行全局事件
        /// </summary>
        /// <param name="eventName">事件名称</param>
        internal static void Invoke(string eventName)
        {
            switch (eventName)
            {
                case nameof(LanguageChanged):
                    LanguageChanged();
                    break;
                case nameof(GreetingTextIntervalUpdated):
                    GreetingTextIntervalUpdated();
                    break;
                case nameof(ConfigSettingsChanged):
                    ConfigSettingsChanged();
                    break;
                case nameof(MicaOpacityChanged):
                    MicaOpacityChanged();
                    break;
                case nameof(PluginsListChanged):
                    PluginsListChanged();
                    break;
                case nameof(DevelopSettingsChanged):
                    DevelopSettingsChanged();
                    break;
                case nameof(LogConfigUpdated):
                    LogConfigUpdated();
                    break;
            }
        }
    }
}

#pragma warning restore CS8602 // 解引用可能出现空引用。

//
//                            ====
//                            !!!!
//       ==========================
//     %%%%%%%%%%%%%%%%%%%%%%%%%%%%%%
//   %%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%
// %%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%
//   ||      _____          _____    ||
//   ||      | | |          | | |    ||
//   ||      |-|-|          |-|-|    ||
//   ||      #####          #####    ||
//   ||                              ||
//   ||      _____   ____   _____    ||
//   ||      | | |   @@@@   | | |    ||
//   ||      |-|-|   @@@@   |-|-|    ||
//   ||      #####   @@*@   #####    ||
//   ||              @@@@            ||
// ******************____****************
// **************************************
//
