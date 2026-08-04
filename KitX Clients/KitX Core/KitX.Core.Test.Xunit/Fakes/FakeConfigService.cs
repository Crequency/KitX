using KitX.Core.Configuration;
using KitX.Core.Contract.Configuration;

namespace KitX.Core.Test.Xunit.Fakes;

/// <summary>
/// 最小 IConfigService 实现：内存中的配置对象，SaveAll/Load/Reload 均为空操作，
/// 不会触碰真实配置文件。
/// </summary>
public class FakeConfigService : IConfigService
{
    public FakeConfigService(AppConfig? appConfig = null, SecurityConfig? securityConfig = null, PluginsConfig? pluginsConfig = null)
    {
        AppConfig = appConfig ?? new AppConfig();
        SecurityConfig = securityConfig ?? new SecurityConfig();
        PluginsConfig = pluginsConfig ?? new PluginsConfig();
    }

    public IAppConfig AppConfig { get; }

    public IPluginsConfig PluginsConfig { get; }

    public ISecurityConfig SecurityConfig { get; }

    public void Load() { }

    public void SaveAll() { }

    public void Reload() { }

    public event EventHandler<ConfigChangedEventArgs>? ConfigChanged;
}
