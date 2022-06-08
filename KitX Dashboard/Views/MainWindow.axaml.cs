using Avalonia.Controls;
using FluentAvalonia.UI.Controls;
using System.ComponentModel;
using BasicHelper.LiteDB;
using System;
using System.Collections.Generic;

#pragma warning disable CS8602 // 解引用可能出现空引用。
#pragma warning disable CS8601 // 引用类型赋值可能为 null。

namespace KitX_Dashboard.Views
{
    public partial class MainWindow : CoreWindow
    {
        private readonly DataTable local_db_table = (Program.LocalDataBase
            .GetDataBase("Dashboard_Settings").ReturnResult as DataBase)
            .GetTable("Windows").ReturnResult as DataTable;

        public MainWindow()
        {
            local_db_table.ResetKeys(
                new string[]
                {
                    "Name", "Width", "Height", "Left", "Top"
                },
                new Type[]
                {
                    typeof(string), typeof(double), typeof(double), typeof(int), typeof(int)
                }
            );

            InitializeComponent();

            Position = new(
                PositionCameCenter((int)(((Program.LocalDataBase
                    .GetDataBase("Dashboard_Settings").ReturnResult as DataBase)
                    .GetTable("Windows").ReturnResult as DataTable)
                    .Query(1).ReturnResult as List<object>)[3], true)
            ,
                PositionCameCenter((int)(((Program.LocalDataBase
                    .GetDataBase("Dashboard_Settings").ReturnResult as DataBase)
                    .GetTable("Windows").ReturnResult as DataTable)
                    .Query(1).ReturnResult as List<object>)[4], false)
            );
        }

        /// <summary>
        /// 坐标回正
        /// </summary>
        /// <param name="input">传入的坐标</param>
        /// <param name="isLeft">是否是距左距离</param>
        /// <returns>回正的坐标</returns>
        private int PositionCameCenter(int input, bool isLeft)
        {
            if (isLeft)
                return input == -1 ? (int)(Screens.Primary.WorkingArea.Width - 1280) / 2 : input;
            else return input == -1 ? (int)(Screens.Primary.WorkingArea.Height - 720) / 2 : input;
        }

        /// <summary>
        /// 储存元数据
        /// </summary>
        private void SaveMetaData()
        {
            local_db_table.Update(1, "Width", Width);
            local_db_table.Update(1, "Height", Height);
            local_db_table.Update(1, "Left", Position.X);
            local_db_table.Update(1, "Top", Position.Y);
        }

        /// <summary>
        /// 正在关闭窗口时事件
        /// </summary>
        /// <param name="e">关闭事件参数</param>
        protected override void OnClosing(CancelEventArgs e)
        {
            SaveMetaData();
            base.OnClosing(e);
        }
    }
}

#pragma warning restore CS8601 // 引用类型赋值可能为 null。
#pragma warning restore CS8602 // 解引用可能出现空引用。
