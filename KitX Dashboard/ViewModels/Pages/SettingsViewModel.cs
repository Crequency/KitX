namespace KitX_Dashboard.ViewModels.Pages
{
    internal class SettingsViewModel : ViewModelBase
    {
        internal SettingsViewModel()
        {

        }

        /// <summary>
        /// 已选择标签页编号属性
        /// </summary>
        internal int TabControlSelectedIndex { get; set; } = 0;
    }
}
