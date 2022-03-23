using System.ComponentModel;

#pragma warning disable IDE0044 // 添加只读修饰符
#pragma warning disable IDE0051 // 删除未使用的私有成员
#pragma warning disable CS0169

namespace KitX.Core
{
    /// <summary>
    /// Web 本地扩展插件接口
    /// Version: [3]v1.0.0
    /// </summary>
    [Description("Web 本地扩展插件接口 - WLEPI")]
    public interface IWebApp
    {
        /// <summary>
        /// 获取插件显示名称
        /// </summary>
        /// <returns>名称</returns>
        string GetName();

        /// <summary>
        /// 获取插件版本
        /// </summary>
        /// <returns>版本</returns>
        string GetVersion();

        /// <summary>
        /// 获取插件发行商名称
        /// </summary>
        /// <returns>名称</returns>
        string GetPublisher();

        /// <summary>
        /// 获取插件简单描述
        /// </summary>
        /// <returns>描述</returns>
        string GetDescribe_Simple();

        /// <summary>
        /// 获取插件完整描述
        /// </summary>
        /// <returns>描述</returns>
        string GetDescribe_Complex();

        /// <summary>
        /// 获取项目地址链接或是发行商主页链接
        /// </summary>
        /// <returns>链接</returns>
        string GetHostLink();

        /// <summary>
        /// 获取应用运行所需的运行环境
        /// </summary>
        /// <returns>所需的运行环境集合</returns>
        List<AppRunTime> GetAppRunTimes();

        /// <summary>
        /// 通知插件本地运行环境
        /// </summary>
        /// <param name="rti">运行环境</param>
        void ToastRunTimeInfo(RunTimeInfo rti);
    }

    public struct RunTimeInfo
    {
        List<string> php_versions;
        List<string> jdk_versions;
        List<string> jre_versions;
        List<string> nginx_versions;
        List<string> tomcat_versions;
        List<string> dotnet_versions;
        List<string> python_versions;
    }

    /// <summary>
    /// 运行环境
    /// </summary>
    public enum AppRunTime
    {
        php, java, nginx, tomcat, dotnet, python
    }
}

#pragma warning restore CS0169
#pragma warning restore IDE0051 // 删除未使用的私有成员
#pragma warning restore IDE0044 // 添加只读修饰符
