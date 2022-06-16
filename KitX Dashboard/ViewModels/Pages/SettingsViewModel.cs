namespace KitX_Dashboard.ViewModels.Pages
{
    public class SettingsViewModel : ViewModelBase
    {
        public SettingsViewModel()
        {

        }

        /// <summary>
        /// 已选择标签页编号属性
        /// </summary>
        public int TabControlSelectedIndex { get; set; } = 0;
    }
}
