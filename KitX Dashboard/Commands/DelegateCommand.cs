using System;
using System.Windows.Input;

#pragma warning disable CS8618 // 在退出构造函数时，不可为 null 的字段必须包含非 null 值。请考虑声明为可以为 null。
#pragma warning disable CS8767 // 参数类型中引用类型的为 Null 性与隐式实现的成员不匹配(可能是由于为 Null 性特性)。

namespace KitX_Dashboard.Commands
{
    public class DelegateCommand : ICommand
    {
        public event EventHandler? CanExecuteChanged;

        public bool CanExecute(object parameter) => CanExecuteFunc == null || CanExecuteFunc(parameter);

        public void Execute(object parameter) => ExecuteAction(parameter);

        public Action<object> ExecuteAction { get; set; }

        public Func<object, bool> CanExecuteFunc { get; set; }

        public DelegateCommand()
        {

        }

        public DelegateCommand(Action<object> executeAction) => ExecuteAction = executeAction;
    }
}

#pragma warning restore CS8767 // 参数类型中引用类型的为 Null 性与隐式实现的成员不匹配(可能是由于为 Null 性特性)。
#pragma warning restore CS8618 // 在退出构造函数时，不可为 null 的字段必须包含非 null 值。请考虑声明为可以为 null。
