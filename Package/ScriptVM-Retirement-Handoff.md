# ScriptVM 退役交接文档（S5）

> 状态：**已完成**（2026-07-13）。ScriptVM 已删除，功能并入 `WorkflowEditorViewModel`。
> 验证：WorkflowIR 193 测试 + Dashboard 5 测试全绿。
> 提交：`3443209`（Dashboard 子模块）+ `87cd816`（父仓库指针）。
> 上游计划：[`Workflow-Storage-Refactor-Handoff.md`](./Workflow-Storage-Refactor-Handoff.md)（S1–S6 总计划）

---

> ⚠️ **以下内容为实施前的规划文档，保留作为历史参考。实际实施细节见上述提交。**

---

## 〇、这份文档要回答的问题

S1–S4 + S6 已完成：`KcsFileFormat` v2（IR 作存储体）、`WorkflowStorageService`、迁移工具、`WorkflowSessionManager`、DI 注册。工作流**管理页面已恢复**（`WorkflowPageViewModel` 的 `IWorkflowStorageService`/`IWorkflowManagementService` 已注册）。

但工作流**编辑器窗口仍打不开**，因为 `WorkflowEditorWindow` 构造函数（:44-53）解析 `WorkflowScriptEditorWindowViewModel`（ScriptVM），而 ScriptVM 依赖两个已退役接口：

```
IBlockScriptService      — 旧 KitX.Workflow 库归档时移除，无新注册
IWorkflowPluginService   — 同上
```

`App.GetService<WorkflowScriptEditorWindowViewModel>()` 抛 `InvalidOperationException`（服务未注册），被 `WorkflowPageViewModel.OpenWorkflowEditorAsync`（:272）的 try-catch 抓住，弹 MessageBox。

**证据**：`Log_2026071312_001.log:107` — `[DBG] Getting service: WorkflowScriptEditorWindowViewModel`，之后无后续日志（异常被 catch，未写日志）。

**S5 的任务**：退役 `WorkflowScriptEditorWindowViewModel`，把它的 4 类功能（代码编辑 / Helper 函数管理 / 常量解析 / 执行）搬入 `WorkflowEditorViewModel`，用新架构（`BsTextLens` / `IExecutionBackend`）替代旧接口。

---

## 一、ScriptVM 完整成员清单（待搬迁）

**文件**：`KitX Dashboard/ViewModels/WorkflowScriptEditorWindowViewModel.cs`

### 构造函数依赖（:40-47）
```csharp
public WorkflowScriptEditorWindowViewModel(
    IBlockScriptService blockScriptService,        // 已退役 → 替换见 §三
    IWorkflowPluginService workflowPluginService,  // 已退役 → 替换见 §三
    ITasksService tasksService)                     // WorkflowEditorVM 已有此依赖
```

### (a) 代码编辑
| 成员 | 行 | 类型 | 搬迁说明 |
|------|-----|------|---------|
| `CodeDocument` | 351 | `internal IDocument?` | 直接搬迁（AvaloniaEdit 文档对象） |
| `MainProgramCode` | 358 | `public string?` | 直接搬迁 |
| `HelperFunctionDocument` | 367 | `internal IDocument?` | **死代码**（无读写），删除 |
| `UseBlockMode` | 421 | `public bool` | **删除**（IR-as-truth 无模式概念） |

### (b) Helper 函数管理
| 成员 | 行 | 类型 | 搬迁说明 |
|------|-----|------|---------|
| `HelperFunctions` | 372 | `ObservableCollection<HelperFunction>` | 直接搬迁 |
| `SelectedHelperFunction` | 379 | `HelperFunction?` | 直接搬迁（含 setter 副作用） |
| `IsEditingHelperFunction` | 404 | `bool`（计算属性） | 直接搬迁 |
| `Parameters` | 409 | `ObservableCollection<HelperFunctionParameter>` | 直接搬迁 |
| `AddHelperFunctionCommand` | 436 | `ReactiveCommand<Unit,Unit>?` | 改为 `[RelayCommand]`（CommunityToolkit 风格） |
| `RemoveHelperFunctionCommand` | 441 | `ReactiveCommand<HelperFunction,Unit>?` | 同上 |

### (c) 常量解析
| 成员 | 行 | 类型 | 搬迁说明 |
|------|-----|------|---------|
| `VariableConstants` | 414 | `ObservableCollection<VariableConstant>` | 直接搬迁 |
| `ParseConstantsFromCode(string)` | 181 | `void` | 后端替换见 §三 |
| `ResetConstantCommand` | 446 | `ReactiveCommand<VariableConstant,Unit>?` | 改为 `[RelayCommand]` |
| `ResetAllConstantsCommand` | 451 | `ReactiveCommand<Unit,Unit>?` | 同上 |

### (d) 执行
| 成员 | 行 | 类型 | 搬迁说明 |
|------|-----|------|---------|
| `ExecutionResult` | 337 | `string` | **改名** `ExecutionOutput`（WorkflowEditorVM 已有此属性，统一名） |
| `IsExecuting` | 345 | `bool` | WorkflowEditorVM 已有，删除 ScriptVM 版 |
| `SubmitCodes(IDocument)` | 253 | `internal void` | 后端替换见 §三 |
| `CancelExecution()` | 328 | `internal void` | 直接搬迁（CancellationTokenSource.Cancel） |
| `CancelExecutionCommand` | 431 | `ReactiveCommand<Unit,Unit>?` | 改为 `[RelayCommand]` |

### (e) 其他
| 成员 | 行 | 说明 |
|------|-----|------|
| `InitCommands()` | 122 | 创建 5 个 ReactiveCommand → 迁移后由 `[RelayCommand]` source generator 生成 |
| `InitEvents()` | 175 | 空方法，删除 |
| `InitializeDefaultHelperFunctions()` | 76 | 私有方法，搬迁（种子 HelperFuncCompare + HelperFuncAdd） |

---

## 二、消费方改动清单

### `WorkflowEditorViewModel.cs`

**构造函数改动**：
- 移除参数 `WorkflowScriptEditorWindowViewModel scriptVM`（:226）
- 移除 `public WorkflowScriptEditorWindowViewModel ScriptVM` 属性（:147）+ 字段
- 新增参数 `IExecutionBackend executionBackend`（用于 BS 执行，:223 附近）
- 新增 §一 所列的搬迁属性/方法（代码编辑/Helper/常量/执行）
- 移除构造函数里的 `ScriptVM.PropertyChanged` 转发（:233-251）——直接用本 VM 的属性

**LoadWorkflowAsync**（:285）：
- 移除所有 `ScriptVM.X` 引用（:300-336 已在 S1 改过一轮，但仍读 `ScriptVM.MainProgramCode`/`ScriptVM.HelperFunctions`）
- 改为本 VM 的 `MainProgramCode`/`HelperFunctions`

**SaveAsync**（:345）：
- 移除 `ScriptVM.MainProgramCode` 引用（:373）

**SwitchToBlockScriptAsync**（:487）：
- 移除 `ScriptVM.MainProgramCode`/`ScriptVM.UseBlockMode` 引用（:531-533）

### `WorkflowEditorWindow.axaml.cs`（~45 处引用）

所有 `_viewModel.ScriptVM.X` → `_viewModel.X`。6 个 WireUp 方法逐个重指：

| 方法 | 行范围 | ScriptVM 成员引用 |
|------|--------|------------------|
| `WireUpCodeEditor` | 122-168 | `CodeDocument`, `SelectedHelperFunction.Code`, `MainProgramCode`, `ParseConstantsFromCode`, `VariableConstants` |
| `WireUpHelperFunctions` + 4 个 handler | 205-320 | `HelperFunctions`, `AddHelperFunctionCommand`, `SelectedHelperFunction`, `RemoveHelperFunctionCommand`, `MainProgramCode` |
| `WireUpConstants` + 3 个 handler | 326-395 | `VariableConstants`, `ResetAllConstantsCommand`, `ResetConstantCommand`, `SelectedHelperFunction`, `Parameters` |
| `WireUpRunStop` + `OnRun`/`OnStop` | 399-441 | `SelectedHelperFunction.Code`, `CodeDocument`, `MainProgramCode`, `SubmitCodes`, `CancelExecution` |
| `WireUpOutput` | 446-495 | `ScriptVM.PropertyChanged`(IsExecuting) — 移除（用本 VM 的 `IsExecuting`） |
| `WireUpModeSwitch` | 519-547 | `MainProgramCode`, `ParseConstantsFromCode`, `VariableConstants` |
| `LoadWorkflowAsync` | 64-75 | `ScriptVM.MainProgramCode`（:73） |

### `WorkflowEditorWindow.axaml`（5 处绑定）

| 行 | 当前绑定 | 改为 |
|----|---------|------|
| 426 | `{Binding ScriptVM.IsEditingHelperFunction, ...}` | `{Binding IsEditingHelperFunction, ...}` |
| 439 | `{Binding ScriptVM.IsEditingHelperFunction}` | `{Binding IsEditingHelperFunction}` |
| 446 | `{Binding ScriptVM.IsEditingHelperFunction, ...}` | `{Binding IsEditingHelperFunction, ...}` |
| 475 | `{Binding ScriptVM.IsEditingHelperFunction}` | `{Binding IsEditingHelperFunction}` |
| 477 | `DataContext="{Binding ScriptVM}"` | **移除**（内层绑定直接解析到窗口 DataContext = WorkflowEditorVM） |

### `App.axaml.cs`

- 移除 `services.AddTransient<WorkflowScriptEditorWindowViewModel>();`（:93）

### 删除文件

- `KitX Dashboard/ViewModels/WorkflowScriptEditorWindowViewModel.cs`（整个文件）

---

## 三、后端替换映射（`IBlockScriptService`/`IWorkflowPluginService` → 新 API）

ScriptVM 构造函数的 3 个依赖，在 WorkflowEditorVM 里的替代：

| ScriptVM 旧调用 | 调用位置 | 替换 |
|----------------|---------|------|
| `_blockScriptService.ParseConstantsFromBlockScript(code)` | ctor:66, ParseConstantsFromCode:184 | `_bsTextLens.ParseLowering(code, helpers)` → 从 `.Ir.Constants` 构建 `List<VariableConstant>` |
| `_workflowPluginService.ParseConstantsFromCode(code)` | ctor:67, ParseConstantsFromCode:185 | 同上（合并——IR-as-truth 下无 BS/non-BS 之分） |
| `_blockScriptService.ValidateBlockScript(code)` | SubmitCodes:271 | `try { _bsTextLens.ParseLowering(...); } catch { /* 验证失败 */ }` |
| `_blockScriptService.ExecuteBlockScriptAsync(code, helpers, overrides, ct)` | SubmitCodes:295 | `var ir = _bsTextLens.Parse(code, helpers); await _executionBackend.ExecuteAsync(ir, null, ct);` |
| `_tasksService.RunTaskAsync(...)` | SubmitCodes:289 | WorkflowEditorVM 已有 `_tasksService`（FT.2 注入） |

### 常量解析适配细节

`ParseConstantsFromBlockScript` 返回 `List<VariableConstant>`。新 API `BsTextLens.ParseLowering` 返回 `LoweringResult`（含 `IrWorkflow`）。需要从 IR 的 `Constants` 字典构建 `VariableConstant` 列表：

```csharp
// 适配伪代码（放在 WorkflowEditorVM 的 ParseConstantsFromCode 里）
public void ParseConstantsFromCode(string code)
{
    try
    {
        var lowering = _bsTextLens.ParseLowering(code, HelperFunctions.ToList());
        var newConstants = lowering.Ir.Constants
            .Select(kv => new VariableConstant
            {
                Name = kv.Key,
                Type = kv.Value.Type,
                DefaultValue = kv.Value.DefaultValue,
                // 保留已有 UserValue（用户覆盖）
                UserValue = VariableConstants.FirstOrDefault(vc => vc.Name == kv.Key)?.UserValue,
            })
            .ToList();
        UpdateVariableConstants(newConstants);  // 搬迁自 ScriptVM:192
    }
    catch { /* 解析失败时保留现有常量 */ }
}
```

`IrConstant` 字段：`Name`、`Type`、`DefaultValue`、`InitialValueExpression`（见 `Ir/IrDeclarations.cs`）。

### 执行适配细节

`SubmitCodes` 搬迁后用 `IExecutionBackend`：

```csharp
internal async void SubmitCodes(IDocument doc)
{
    var codeText = doc?.Text ?? MainProgramCode ?? "";
    if (string.IsNullOrWhiteSpace(codeText)) return;

    _cancellationTokenSource?.Cancel();
    _cancellationTokenSource = new CancellationTokenSource();
    var ct = _cancellationTokenSource.Token;

    IsExecuting = true;
    ExecutionOutput = "";

    await _tasksService.RunTaskAsync(async () =>
    {
        try
        {
            var ir = _bsTextLens.Parse(codeText, HelperFunctions.ToList());
            var result = await _executionBackend.ExecuteAsync(ir, null, ct);
            // 解包结果到 ExecutionOutput（搬迁自 ScriptVM:302-310）
            ExecutionOutput = result.IsSuccess
                ? string.Join("\n", result.Output)
                : $"Error: {result.ErrorMessage}";
        }
        catch (OperationCanceledException) { /* 用户取消 */ }
        catch (Exception ex) { ExecutionOutput = $"Error: {ex.Message}"; }
        finally { IsExecuting = false; }
    }, ct, nameof(SubmitCodes));
}
```

---

## 四、实施策略（分步，每步编译验证）

**原则**：每步后 `dotnet build` 必须 0 错误。降低遗漏风险。

### 步骤 1：搬迁属性到 WorkflowEditorVM（编译通过，ScriptVM 仍在）
- 在 `WorkflowEditorViewModel` 新增 §一 (a)(b)(c) 的所有属性/字段
- 新增 `IExecutionBackend _executionBackend` 构造参数
- 新增 `InitializeDefaultHelperFunctions()` 私有方法
- **暂不删除** ScriptVM 属性——两者并存，下一步才重指消费方

### 步骤 2：搬迁方法到 WorkflowEditorVM
- `ParseConstantsFromCode`（§三 适配）
- `SubmitCodes`/`CancelExecution`（§三 适配）
- `UpdateVariableConstants`/`GetUserConstantOverrides`（搬迁）
- 删除 `ExecutionResult`，统一用 `ExecutionOutput`

### 步骤 3：改 WorkflowEditorWindow.axaml.cs（~45 处）
- 所有 `_viewModel.ScriptVM.X` → `_viewModel.X`
- 移除 `WireUpOutput` 里的 `ScriptVM.PropertyChanged` 订阅

### 步骤 4：改 WorkflowEditorWindow.axaml（5 处绑定）
- `ScriptVM.IsEditingHelperFunction` → `IsEditingHelperFunction`
- 移除 `DataContext={Binding ScriptVM}`

### 步骤 5：改 WorkflowEditorVM 构造函数 + 移除 ScriptVM
- 移除 `scriptVM` 参数 + `ScriptVM` 属性 + 字段
- 移除构造函数里的 `ScriptVM.PropertyChanged` 转发（:233-251）
- 改 `WorkflowEditorWindow` 构造函数：移除 `App.GetService<WorkflowScriptEditorWindowViewModel>()`

### 步骤 6：清理
- `App.axaml.cs` 移除 `AddTransient<WorkflowScriptEditorWindowViewModel>()`
- 删除 `WorkflowScriptEditorWindowViewModel.cs`
- 删除死代码：`HelperFunctionDocument`、`GetTypeName`

### 步骤 7：验证
- `dotnet build` 0 错误
- 启动 Dashboard，打开工作流编辑窗口——不再弹异常 MessageBox
- BS 编辑 + Helper 函数管理 + 执行/停止功能正常

---

## 五、验收标准

1. `dotnet build KitX.sln` 0 错误
2. DI.Test Test 3：`IBlockScriptService`/`IWorkflowPluginService` 标注"已退役"（NOT REGISTERED 是预期）
3. **工作流编辑窗口可打开**（不再因 ScriptVM 解析失败而弹 MessageBox）
4. BS 代码编辑器可用（输入代码、切换 Helper 函数编辑）
5. Helper 函数增删改可用
6. 常量面板显示 + 重置可用
7. Run/Stop 执行可用（`IExecutionBackend.ExecuteAsync`）
8. WorkflowIR 193 项测试不回归
9. `KitX.Dashboard.Test.Xunit` 5 项测试不回归

---

## 六、风险与注意事项

| # | 风险 | 缓解 |
|---|------|------|
| 1 | ~45 处 code-behind 引用易遗漏 | 步骤 3 后 `grep "_viewModel.ScriptVM"` 必须零命中 |
| 2 | `ReactiveCommand` → `[RelayCommand]` 风格转换 | ScriptVM 用 ReactiveUI 的 `ReactiveCommand`；WorkflowEditorVM 用 CommunityToolkit 的 `[RelayCommand]`。命令的 XAML 绑定（`AddHelperFunctionCommand`/`RemoveHelperFunctionCommand` 等）需确认 CommunityToolkit 生成的命令名匹配 |
| 3 | 常量解析适配逻辑（IR Constants → VariableConstant） | 先简单实现（name+type+defaultValue），保留 UserValue 合并逻辑；`LoweringResult` 可能不直接暴露 Constants，需从 `_bsTextLens.Parse(code).Constants` 取 |
| 4 | `SubmitCodes` 的 `constantOverrides` 语义 | 旧 `ExecuteBlockScriptAsync` 接收用户覆盖值；新 `IExecutionBackend.ExecuteAsync` 接收 IR——需在 parse 后把用户覆盖应用到 IR 的 Constants 上再执行 |
| 5 | `IDocument` 类型依赖 AvaloniaEdit | WorkflowEditorVM 引入 `IDocument` 属性会让它依赖 AvaloniaEdit——但 `CodeDocument` 只在 code-behind 设置，VM 只存引用，不调用其方法（除 `SubmitCodes` 读 `.Text`）。可接受 |

---

## 七、参考

| 文档/文件 | 位置 | 用途 |
|----------|------|------|
| S1–S6 总计划 | [`Workflow-Storage-Refactor-Handoff.md`](./Workflow-Storage-Refactor-Handoff.md) | 存储服务重设计完整计划 |
| IR 架构 | [`IR-Architecture-v6.0.md`](./IR-Architecture-v6.0.md) §2 | IR 作为单一真相源 |
| 前端重构交接 | [`Dashboard-Frontend-Refactor-Handoff.md`](./Dashboard-Frontend-Refactor-Handoff.md) | F1-fix + FT 阶段总结 |
| ScriptVM 源文件 | `KitX Dashboard/ViewModels/WorkflowScriptEditorWindowViewModel.cs` | 待退役（456 行） |
| WorkflowEditorVM | `KitX Dashboard/ViewModels/WorkflowEditorViewModel.cs` | 接收方（当前 ~600 行） |
| WorkflowEditorWindow | `KitX Dashboard/Views/WorkflowEditorWindow.axaml(.cs)` | 45 处 code-behind + 5 处 XAML 绑定 |
| IBlockScriptService 接口 | `KitX.Core.Contract/Workflow/IWorkflowService.cs:57` | 已退役接口定义 |
| IExecutionBackend 接口 | `KitX.WorkflowIR/Backend/IExecutionBackend.cs` | 执行替代 |
| BsTextLens | `KitX.WorkflowIR/Lens/BsTextLens/BsTextLens.cs` | parse/validate/project 替代 |
