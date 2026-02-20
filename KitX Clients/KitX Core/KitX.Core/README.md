# KitX.Core

KitX Core 业务逻辑层 - 负责 KitX Client 的核心业务逻辑实现。

## 项目说明

本项目是 KitX Dashboard Core-UI 分离重构的一部分，负责实现核心业务逻辑，与 UI 层完全解耦。

## 架构设计

### 项目职责

- 实现配置管理 (Configuration)
- 实现插件管理 (Plugin Management)
- 实现设备管理 (Device Management)
- 实现安全管理 (Security)
- 实现活动记录 (Activity Logging)
- 实现统计分析 (Statistics)
- 实现工作流执行 (Workflow Execution)
- 实现事件系统 (Event System)
- 实现任务调度 (Task Scheduling)
- 实现文件监控 (File Watching)
- 实现全局热键 (Global Hotkeys)
- 实现公告服务 (Announcement Service)

### 文件夹结构

```
KitX.Core/
├── Configuration/      # 配置管理实现
├── Plugin/            # 插件管理实现
├── Device/            # 设备管理实现
├── Security/          # 安全管理实现
├── Activity/          # 活动记录实现
├── Statistics/        # 统计分析实现
├── Workflow/          # 工作流执行实现
├── Event/             # 事件系统实现
├── Task/              # 任务调度实现
├── FileWatcher/       # 文件监控实现
├── Hotkey/            # 全局热键实现
├── Announcement/      # 公告服务实现
└── DI/                # 依赖注入配置
```

## 依赖关系

### 项目引用

- `KitX.Core.Contract` - Core 服务接口定义
- `KitX.Shared.CSharp` - 共享数据模型
- `KitX.Contract.CSharp` - 插件契约接口

### NuGet 包

- `Microsoft.Extensions.DependencyInjection` (10.0.0) - 依赖注入框架

## 设计原则

1. **接口隔离**: 所有服务通过 `KitX.Core.Contract` 中定义的接口暴露功能
2. **依赖注入**: 使用 MS.DI 容器管理依赖关系
3. **事件驱动**: 通过事件向 UI 层推送状态变化
4. **无 UI 依赖**: Core 层不依赖任何 UI 框架或组件
5. **进程内调用**: 与 UI 层在同一进程内，使用 C# 接口调用

## 使用示例

### 在 Dashboard 中使用 Core 服务

```csharp
// 1. 注册 Core 服务 (在 App.axaml.cs 中)
var services = new ServiceCollection();
services.AddCoreServices();

// 2. 在 ViewModel 中注入服务
public class MainWindowViewModel : ViewModelBase
{
    private readonly IConfigService _configService;

    public MainWindowViewModel(IConfigService configService)
    {
        _configService = configService;
    }
}
```

## 后续计划

参见 [KitX-Dashboard-Core-UI分离重构计划书.md](../../../KitX-Dashboard-Core-UI分离重构计划书.md)，当前已完成阶段3, 但是阶段2中发现有3个网络服务类与UI耦合过深，无法直接迁移，因此阶段2完成度为93%，阶段3完成度为100%。具体请参照文档[阶段2-完整总结报告.md](../../../KitX%20Clients/KitX%20Core/KitX.Core/阶段2-完整总结报告.md)和[阶段3-完成总结报告.md](../../../KitX%20Clients/KitX%20Core/KitX.Core/阶段3-完成总结报告.md)。

## 相关文档

- [重构计划书](../../../KitX-Dashboard-Core-UI分离重构计划书.md)
- [接口定义项目](../../../KitX%20Standard/KitX%20Core%20Contracts/KitX.Core.Contract/README.md)

## 许可证

AGPL-3.0-only
