using KitX.Core.Contract.Workflow;

namespace KitX.Workflow.Contract;

/// <summary>
/// 插件管理器接口 - BlockScripting 使用此接口调用插件方法。
/// Copy of Kscript.CSharp.Parser.Core.IPluginManager for BlockScripting use
/// without depending on the KCS parser assembly.
/// 实际实现由 RealPluginManager 提供。
/// </summary>
public interface IPluginManager
{
    /// <summary>
    /// 调用插件方法
    /// </summary>
    /// <typeparam name="T">返回值类型</typeparam>
    /// <param name="callInfo">调用信息</param>
    /// <returns>插件方法的返回值</returns>
    T Call<T>(PluginCallInfo callInfo);

    /// <summary>
    /// 调用插件方法（无返回值）
    /// </summary>
    /// <param name="callInfo">调用信息</param>
    void Call(PluginCallInfo callInfo);

    /// <summary>
    /// 检查插件是否存在
    /// </summary>
    /// <param name="pluginName">插件名称</param>
    /// <returns>插件是否存在</returns>
    bool IsPluginExists(string pluginName);

    /// <summary>
    /// 检查插件方法是否存在
    /// </summary>
    /// <param name="pluginName">插件名称</param>
    /// <param name="methodName">方法名称</param>
    /// <returns>方法是否存在</returns>
    bool IsMethodExists(string pluginName, string methodName);
}
