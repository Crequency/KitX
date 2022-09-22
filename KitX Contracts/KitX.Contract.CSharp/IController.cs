using KitX.Web.Rules;

namespace KitX.Contract.CSharp
{
    public interface IController
    {

        /// <summary>
        /// 进入启动模式
        /// </summary>
        void Start();

        /// <summary>
        /// 进入暂停模式
        /// </summary>
        void Pause();

        /// <summary>
        /// 进入停止模式
        /// </summary>
        void End();

        /// <summary>
        /// 获取插件功能列表
        /// </summary>
        /// <returns>功能体</returns>
        Function GetFunctions();

        /// <summary>
        /// 执行命令
        /// </summary>
        /// <param name="cmd">命令名称</param>
        /// <param name="arg">参数列表</param>
        /// <returns>结果代码</returns>
        object Execute(string cmd, object arg = null);
    }
}
