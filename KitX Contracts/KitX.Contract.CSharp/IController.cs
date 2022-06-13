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
    }
}
