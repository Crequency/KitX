using System.Collections.Generic;

#pragma warning disable CS8602 // 解引用可能出现空引用。
#pragma warning disable CA1822 // 将成员标记为 static

namespace KitX_Dashboard.ViewModels
{
    public class MainWindowViewModel : ViewModelBase
    {
        public double DB_Width
        {
            get => (double)(Helper.local_db_table.Query(1).ReturnResult as List<object>)[1];
            set => (Helper.local_db_table.Query(1).ReturnResult as List<object>)[1] = value;
        }

        public double DB_Height
        {
            get => (double)(Helper.local_db_table.Query(1).ReturnResult as List<object>)[2];
            set => (Helper.local_db_table.Query(1).ReturnResult as List<object>)[2] = value;
        }
    }
}

#pragma warning restore CA1822 // 将成员标记为 static
#pragma warning restore CS8602 // 解引用可能出现空引用。
