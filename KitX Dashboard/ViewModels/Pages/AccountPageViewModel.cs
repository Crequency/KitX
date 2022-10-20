using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

#pragma warning disable CS8604 // 引用类型参数可能为 null。

namespace KitX_Dashboard.ViewModels.Pages
{
    internal class AccountPageViewModel : ViewModelBase, INotifyPropertyChanged
    {
        internal double noLogin_tipHeight = 300;

        internal double NoLogin_TipHeight
        {
            get => noLogin_tipHeight;
            set
            {
                noLogin_tipHeight = value;
                PropertyChanged?.Invoke(this, new(nameof(NoLogin_TipHeight)));
            }
        }

        internal bool loginButtonVisibility = true;

        internal bool LoginButtonVisibility
        {
            get => loginButtonVisibility;
            set
            {
                loginButtonVisibility = value;
                PropertyChanged?.Invoke(this, new(nameof(LoginButtonVisibility)));
            }
        }

        internal bool logoutButtonVisibility = false;

        internal bool LogoutButtonVisibility
        {
            get => logoutButtonVisibility;
            set
            {
                logoutButtonVisibility = value;
                PropertyChanged?.Invoke(this, new(nameof(LogoutButtonVisibility)));
            }
        }

        public new event PropertyChangedEventHandler? PropertyChanged;
    }
}

#pragma warning restore CS8604 // 引用类型参数可能为 null。
