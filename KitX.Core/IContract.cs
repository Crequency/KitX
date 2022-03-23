using System.ComponentModel;

namespace KitX.Core
{
    /// <summary>
    /// 本地扩展插件接口
    /// Version: [3]v1.0.0
    /// </summary>
    [Description("本地扩展插件接口 - LEPI")]
    public interface IContract
    {
        #region 获取插件的内含信息

        /// <summary>
        /// 获取插件显示名称
        /// </summary>
        /// <returns>插件显示名称</returns>
        string GetName();

        /// <summary>
        /// 获取插件版本号
        /// </summary>
        /// <returns></returns>
        string GetVersion();

        /// <summary>
        /// 获取插件出版商
        /// </summary>
        /// <returns></returns>
        string GetPublisher();

        /// <summary>
        /// 获取插件简单描述
        /// </summary>
        /// <returns></returns>
        string GetDescribe_Simple();

        /// <summary>
        /// 获取插件完整描述
        /// </summary>
        /// <returns></returns>
        string GetDescribe_Complex();

        /// <summary>
        /// 获取插件帮助链接
        /// </summary>
        /// <returns></returns>
        string GetHelpLink();

        /// <summary>
        /// 获取插件出版商主页链接
        /// </summary>
        /// <returns></returns>
        string GetHostLink();

        /// <summary>
        /// 通知插件启动
        /// </summary>
        int Start();

        /// <summary>
        /// 通知插件结束
        /// </summary>
        int Stop();

        /// <summary>
        /// 获取分类
        /// </summary>
        /// <returns>分类集合</returns>
        List<Category> GetCategories();

        /// <summary>
        /// 获取标签
        /// </summary>
        /// <returns>标签集合</returns>
        List<string> GetTag();

        /// <summary>
        /// 获取此插件支持的语言
        /// </summary>
        /// <returns>语言集合</returns>
        List<Languages> GetLanguages();

        #endregion

        #region 操作/控制/通知/设置 插件

        /// <summary>
        /// 通知插件设置主题
        /// </summary>
        /// <param name="theme"></param>
        void SetTheme(Theme theme);

        /// <summary>
        /// 设定插件的临时路径
        /// </summary>
        void SetCacheBase(string path);

        #endregion
    }

    #region 分类/语言/主题 枚举值

    /// <summary>
    /// 标签
    /// </summary>
    public enum Category
    {
        Calculate, // 计算
        Develop, // 开发
        Design, // 设计
        Hardware, // 硬件
        System, // 系统
        Tool, // 工具
        Beautify // 美化
    }

    /// <summary>
    /// 语言
    /// </summary>
    public enum Languages
    {
        zh_cn, en_us, zh_cht, ja_jp
    }

    /// <summary>
    /// 主题
    /// </summary>
    public enum Theme
    {
        Light, Dark
    }

    #endregion
}
