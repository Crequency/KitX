using System;
using System.ComponentModel.Composition;

namespace KitX.Contract.CSharp
{
    [InheritedExport]
    public interface IIdentityInterface
    {
        /// <summary>
        /// 获取插件名称
        /// </summary>
        /// <returns>插件名称</returns>
        string GetName();

        /// <summary>
        /// 获取插件版本
        /// </summary>
        /// <returns>插件版本</returns>
        string GetVersion();

        /// <summary>
        /// 获取显示名称
        /// </summary>
        /// <returns>显示名称</returns>
        string GetDisplayName();

        /// <summary>
        /// 获取作者名称
        /// </summary>
        /// <returns>作者名称</returns>
        string GetAuthorName();

        /// <summary>
        /// 获取发行者名称
        /// </summary>
        /// <returns>发行者名称</returns>
        string GetPublisherName();

        /// <summary>
        /// 获取作者链接
        /// </summary>
        /// <returns>作者链接</returns>
        string GetAuthorLink();

        /// <summary>
        /// 获取发行者链接
        /// </summary>
        /// <returns>发行者链接</returns>
        string GetPublisherLink();

        /// <summary>
        /// 获取简单描述
        /// </summary>
        /// <returns>简单描述</returns>
        string GetSimpleDescription();

        /// <summary>
        /// 获取复杂描述
        /// </summary>
        /// <returns>复杂描述</returns>
        string GetComplexDescription();

        /// <summary>
        /// 获取 MarkDown 语法的完整介绍
        /// </summary>
        /// <returns>完整介绍</returns>
        string GetTotalDescriptionInMarkdown();

        /// <summary>
        /// 获取 Base64 编码的图标
        /// </summary>
        /// <returns>Base64 编码的图标</returns>
        string GetIconInBase64();

        /// <summary>
        /// 获取发行日期
        /// </summary>
        /// <returns>发行日期</returns>
        DateTime GetPublishDate();

        /// <summary>
        /// 获取最近更新日期
        /// </summary>
        /// <returns>最近更新日期</returns>
        DateTime GetLastUpdateDate();

        /// <summary>
        /// 获取控制器
        /// </summary>
        /// <returns>控制器</returns>
        IController GetController();

        /// <summary>
        /// 指示是否是市场版本
        /// </summary>
        /// <returns>是否是市场版本</returns>
        bool IsMarketVersion();

        /// <summary>
        /// 获取市场版本插件协议
        /// </summary>
        /// <returns>市场版本插件协议</returns>
        IMarketPluginContract GetMarketPluginContract();
    }
}
