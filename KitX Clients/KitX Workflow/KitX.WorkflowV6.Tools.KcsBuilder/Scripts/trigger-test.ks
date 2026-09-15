var {
    dynamic vaaa0001
}

// Trigger 测试（v6 迁移）：插件 Trigger 唤起 → 插件调用
// 1. 从插件获取输入
PluginCall("TestPlugin.WPF.Core", "GetInput") > JsonAsString > vaaa0001
// 2. 调用插件方法（把输入回传）
vaaa0001 > PluginCall("TestPlugin.WPF.Core", "HelloAnything", _)
Print("Trigger 测试完成")
