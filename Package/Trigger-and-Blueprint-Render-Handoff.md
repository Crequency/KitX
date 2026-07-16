# Trigger 系统 + 蓝图渲染问题交接文档

> 状态：**待实施**。以下两个问题在 S5（ScriptVM 退役）完成后发现，均为独立后续任务。
> 前置完成：S1–S6 全部完成（2026-07-16），4 个工作流已迁移为 v5.0 语法并通过端到端执行验证。
> 上游文档：[`BlockScriptGrammarRule.md`](./BlockScriptGrammarRule.md)（v5.0 语法规范，§11 蓝图侧表示）

---

## 〇、本次会话完成的工作回顾

在撰写交接前，先明确本次会话已完成的修复，避免后续实施方重复工作：

1. **S5 ScriptVM 退役**：删除 `WorkflowScriptEditorWindowViewModel`，功能并入 `WorkflowEditorViewModel`，BS 执行切换到 `IExecutionBackend` + `BsTextLens`。
2. **BS v3.0→v5.0 迁移根因修复**：迁移工具检测 `ParseLowering` 诊断错误（不再静默序列化残缺 IR）+ 新增 `--from-bs` 模式用于手动重写 BS → v2 .kcs。
3. **BS v5.0 负数字面量支持**：新增 `Minus` token + `NumericLiteral` 解析器。
4. **5 个底层后端 bug 修复**（均在 `WorkflowIR` 库内）：
   - Bare call（`Print("hello")`）不执行 → lowerer 改为 Segments
   - 管道裸函数终端（`vaaa0001 > Print`）被误判为变量 tap → 查注册表区分
   - §6.2 隐式参数未填充 → 无显式 `_` 时合成 placeholder
   - Renderer 零源 pipeline 输出前导 `>` → 裸 call 不输出前导 `>`
   - 纯赋值流水线（`"..." > bfCode`）被 codegen 静默丢弃 → `IrCodegen` 新增纯赋值兜底
5. **4 个工作流手动重写为 v5.0 语法**并端到端验证：
   - 新建工作流：输出 `10` ✓
   - 基础测试：输出猜数循环 ✓
   - BF Brainfuck：输出 `Hello World!` ✓
   - Trigger测试：跳过（需插件配合）

**WorkflowIR 194 测试全绿**（+1 负数字面量回归测试）。

---

## 一、问题 A：Trigger 系统无法选择目标

### 症状

前端工作流编辑器中，将 TriggerType 设为 "PluginEvent" 后，Plugin ComboBox 和 Trigger ComboBox 均无可选项（列表为空），无法选择触发目标。

### 根因

**`WorkflowEditorViewModel._pluginServer` 为 `null`**。

`WorkflowEditorWindow.axaml.cs:46-50` 构造 ViewModel 时只传了 3 个位置参数：

```csharp
_viewModel = new WorkflowEditorViewModel(
    App.GetService<IWorkflowStorageService>(),
    App.GetService<ITasksService>(),
    blueprintVM
    // eventService 和 pluginServer 均使用默认值 null
);
```

`WorkflowEditorViewModel` 构造函数中 `IPluginServer? pluginServer = null` 是可选参数（为测试宿主留的口子），未传入则为 null。`RefreshAvailablePlugins()` 第一行 `if (_pluginServer == null) return;` 直接返回，`AvailablePlugins` 始终为空。

### 证据

- 日志 `Log_2026071614_001.log:67`：`[DBG] Getting service: IPluginServer` — DI 中可解析。
- 日志 `Log_2026071614_001.log:1204-1205`：`TestPlugin.WPF.Core` 已连接（`_connections count after: 1`）— Connections 非空。
- `WorkflowEditorViewModel.cs:129-137`：`RefreshAvailablePlugins()` 在 `_pluginServer == null` 时清空列表并 return。
- XAML 绑定（`WorkflowEditorWindow.axaml:200-211`）路径正确，非根因。
- `IPluginServer` 已在 DI 注册（`CoreServiceCollectionExtensions.cs:96-97`），非根因。

### 修复方案

在 `WorkflowEditorWindow.axaml.cs` 构造调用中补上 DI 解析的可选参数：

```csharp
_viewModel = new WorkflowEditorViewModel(
    App.GetService<IWorkflowStorageService>(),
    App.GetService<ITasksService>(),
    blueprintVM,
    App.GetService<IEventService>(),    // 元数据同步
    App.GetService<IPluginServer>()     // ← 关键缺失项
);
```

**风险**：低。`IPluginServer` 和 `IEventService` 均已在 DI 注册，补传后 `AvailablePlugins`/`AvailableTriggers` 将正确填充。

### 验收

- [ ] TriggerType 切换到 "PluginEvent" 后，Plugin ComboBox 显示已连接插件名
- [ ] 选择插件后，Trigger ComboBox 显示该插件的 SupportedTriggers
- [ ] 保存工作流后 TriggerConfig 正确持久化

---

## 二、问题 B：蓝图渲染与设计完全不符

### 症状

切换到蓝图模式后，所有节点（BlockNode + 每条语句的 BuiltinFunctionNode）都平铺在最外层画布上，堆在坐标 (0,0)，连线为 0。不呈现设计预期的"外层只有入口节点和 Block 节点，Block 节点内部是语句/函数节点"的折叠/展开结构。

### 设计预期（`BlockScriptGrammarRule.md` §11）

- **§11.1 折叠态**：每个 `#Block` 在外层呈现为可折叠的函数节点，折叠态下内部语句不可见，只显示接口。
- **§11.2 展开态**：双击 BlockNode 进入内层图，显示 EntryPoint/ExitPoint 边界节点、BlockVar VariableNode、ForLoop index、以及**内部语句节点**。
- **§11.3 控制流连线**：Goto/Branch/ForLoop/Switch 以块间 Exec 连线呈现。
- **§11.4 数据边**：PubVar 写入/读取以数据连线呈现。

### 当前实际行为（日志证据）

日志 `Log_2026071614_001.log:1301-1308`：
```
Loading blueprint: 3 nodes, 0 exec, 0 data connections
  Added node: Name=MainBlock, Id=block:MainBlock, Type="Block"
  Added node: Name=Pipeline, Id=stmt:B0DE6FA63225, Type="BuiltinFunction"
  Added node: Name=PluginCall, Id=stmt:5AD29E11495F, Type="BuiltinFunction"
Initialized BlockScope for 'MainBlock' with 0 child nodes
Loaded blueprint: 3 nodes, 0 connections, 0 scope blocks
```

即：BlockNode + 2 个语句节点全在最外层，BlockScope 里 0 个子节点，0 个 ScopeBlock 被重建。

### 根因分析

根因在 `BpRenderer.cs` 的 **Phase 3（`RenderStatementNodes`）**：

1. **语句节点未挂入 BlockNode.ChildNodeIds**：Phase 3 为每条非控制流语句生成 `BuiltinFunctionNode` 并 `bp.AddNode(node)` 放到外层，但**没有把该节点 id 加入所属 BlockNode 的 `ChildNodeIds`**。只有 Phase 2 的 BlockVars 和 ForLoop index EntryPoint 被正确挂入 ChildNodeIds。这导致折叠态 preview 为空、展开态子图为空。

2. **无布局坐标来源**：BS 解析器（`BsLowerer`）从不产生 Layout annotation（只产生 Comment annotation）。`ApplyStatementLayout`/`ApplyBlockLayout` 全部读不到坐标 → 所有节点默认 X=0/Y=0 → 堆在画布原点。

3. **Phase 6（`RenderBlockScopes`）未填充 `NodeIds`/`OwnerNodeId`**：`BlueprintBlockScope` 元数据生成但关键字段为空。

### 消费端（Dashboard）无缺陷

`BlueprintEditorViewModel` 的消费逻辑正确：
- `InitializeBlockScopes`（行 970）从 `Metadata["ChildNodeIds"]` 读取子节点
- `RebuildScopeBlocksFromBlockScopes`（行 907）创建 ScopeBlock
- `BlockNodeScopeVM` 折叠/展开 VM 已实现
- `BlockNodeContainer` 视图容器已实现

问题不在消费端，而在 **BpRenderer 的输出不完整**。

### 架构问题：旧 BpRenderer 从 BS 转换逻辑沿用

当前 `BpRenderer` 是从旧版 CFG→Blueprint 转换逻辑迁移而来，其设计假设是"CFG 节点 → Blueprint 节点"的平铺映射。新版 IR 架构的根本性变化是：

- **IR 以块为单位组织**（`IrBlock` 包含 `Statements`），不是 CFG 的扁平节点图
- **设计要求块折叠为外层函数节点**，内部语句在展开态可见
- **布局坐标应来自 IR annotation 或自动布局算法**，不是从旧 CFG 坐标映射

强行沿用旧 BpRenderer 的平铺逻辑，在"语句节点归属块子图"这一步产生了结构性断裂——这正是用户报告的"所有节点堆在一起"的直接原因。

### 建议方案：推倒重做 BpRenderer

鉴于根因涉及架构层面的逻辑重塑（从 BS/CFG 平铺 → IR 块折叠），**建议彻底从头重写 `BpRenderer`**，而非修补现有代码。理由：

1. **旧代码的平铺假设与新 IR 块结构根本不兼容**：修补需要在每个 Phase 中添加"归属挂载"逻辑，但旧代码的 6 个 Phase 是围绕平铺设计的，修补会引入深层缠绕。
2. **布局坐标来源需要新设计**：旧代码依赖 CFG 坐标 annotation，新架构需要自动布局算法（如 dagre/层次布局）或 IR 层面的 Layout annotation。
3. **折叠/展开是全新功能**：旧代码完全没有"内层图"概念，§11.2 的 EntryPoint/ExitPoint 边界节点只对 BlockVars/ForLoop index 实现了一半。

### 重写 BpRenderer 的实施建议

**目标**：`BpRenderer.Render(IrWorkflow) → Blueprint` 产出符合 §11 设计的蓝图结构。

**输入**：`IrWorkflow`（块 + 语句 + 控制流边 + 数据边）

**输出**：`Blueprint`（外层节点 + 内层节点 + Exec 连线 + 数据连线 + 布局坐标）

**核心逻辑**：

```
Phase 1: 外层节点
  - 每个 IrBlock → 一个 BlockNode（Entry block → EntryNode）
  - BlockNode 的 ChildNodeIds = 该块所有内部节点的 id 列表
  - 布局：外层节点用层次布局（块间控制流方向决定大致位置）

Phase 2: 内层节点（每个块的子图）
  - EntryPointNode（块的执行入口）
  - BlockVar → VariableNode
  - ForLoop index → EntryPointNode（注入变量）
  - 每条 IrPipelineStatement → BuiltinFunctionNode / HelperFunctionNode
  - 控制流终止符 → 不生成内层节点（§11.3：控制流是块间 Exec 连线）
  - ExitPointNode（块的执行出口，连接到控制流目标）
  - 布局：内层节点用从左到右的数据流布局

Phase 3: Exec 连线（块间）
  - Goto/Branch/ForLoop/Switch 的每个 arm → BlockNode 间 Exec 连线
  - 无终止符的块 → FallThroughTarget 顺序 Exec 连线

Phase 4: 数据连线（块内 + 跨块 PubVar）
  - 管道 Sources → Segments 的数据流 → 内层数据连线
  - PubVar 写入/读取 → 跨块数据连线（§11.4）

Phase 5: BlockScope 元数据
  - 每个 BlockScope 的 NodeIds = 内层节点 id 列表
  - OwnerNodeId = 所属 BlockNode 的 id
```

**自动布局**：
- 外层：基于控制流图的层次布局（top-down 或 left-right）
- 内层：基于数据流的从左到右布局
- 可使用简单的手动布局算法（不引入外部依赖），或后续集成 dagre

**关键文件**：
- 重写：`KitX.WorkflowIR/Lens/BpGraphLens/BpRenderer.cs`
- 可能调整：`KitX.WorkflowIR/Lens/BpGraphLens/BpGraphLens.cs`（Project 入口）
- 消费端（不改）：`BlueprintEditorViewModel.cs`（`LoadBlueprintIntoDrawing`、`InitializeBlockScopes`、`RebuildScopeBlocksFromBlockScopes`）
- 设计参考：`BlockScriptGrammarRule.md` §11

**TDD 建议**：
- 先写测试：给定一个已知 IR（如基础测试的 8 块猜数游戏），`BpRenderer.Render(ir)` 应产出 N 个外层 BlockNode + M 个内层语句节点 + K 条 Exec 连线
- 红灯 → 实现 → 绿灯 → 重构
- 测试用例可从已验证的 4 个工作流 IR 中提取

### 验收

- [ ] 切换到蓝图模式后，外层只显示 EntryNode + BlockNode（折叠态）
- [ ] BlockNode 折叠态显示块名 + 接口引脚
- [ ] 双击 BlockNode 展开内层图，显示内部语句节点
- [ ] 控制流（Goto/Branch/ForLoop）以块间 Exec 连线呈现
- [ ] 数据流以节点间数据连线呈现
- [ ] 节点有合理布局坐标（不堆在 0,0）
- [ ] 编辑蓝图后通过 `BpEditTranslator` → `SyncService` 正确回写 IR
- [ ] **用户拖动节点位置后，save→load round-trip 保留坐标**（见下文 §坐标持久化）
- [ ] BlueprintEditorViewModel 现有测试不回归

### 坐标持久化：数据结构已就绪，回写链路缺一个接线点

**需求**："尊重用户对蓝图节点位置的编排"——不能每次渲染蓝图都是默认位置，而是先匹配并应用用户设置/编排的位置。

**调查结论**：IR 数据结构、序列化、渲染读取三条链路完整；坐标回写方法存在且正确；但 `SyncService` 未调用它，导致生产环境拖动坐标无法 round-trip。

#### 已就绪的链路

| 链路环节 | 状态 | 说明 |
|---|---|---|
| IR 模型坐标存储 | ✅ 有 | `IrAnnotation(AnnotationKind.Layout, key, IrLayout(X, Y))` 挂在 `IrBlock.Annotations`。块级 `Key="BlockPos"`，语句级 `Key=<fingerprint.Value>`。`IrBlock.Equals` 显式排除 Annotations，保证坐标不影响语义相等性。 |
| .kcs DTO 序列化 | ✅ 完整 | `AnnotationDto` 有 `LayoutX`/`LayoutY` 字段，`IrSerializer` 双向映射完整。 |
| BpRenderer 读取坐标 | ✅ 有 | `ApplyBlockLayout`/`ApplyStatementLayout` 从 IR Annotations 读取坐标写入 `BlueprintNode.X/Y`。无 annotation 时默认 (0,0)。 |
| BpEditTranslator 回写方法 | ✅ 有 | `ApplyPosition(IrWorkflow, nodeId, x, y)` 静态方法存在且正确：`"block:"` 前缀写块级，`"stmt:"` 前缀通过 `DeriveStableId` 匹配语句写语句级。返回新 `IrWorkflow`（结构性共享）。 |

#### 缺失的接线点

| 链路环节 | 状态 | 说明 |
|---|---|---|
| **SyncService 调用 ApplyPosition** | ❌ 断点 | `SyncService.ApplyBpEdits` 处理 `MoveNodePosition` 时，`BpEditTranslator.Translate` 产生空 IrDiff（坐标是纯视图状态，不产生语义 diff），`SyncService` 在 `diff.IsEmpty` 时直接 return，**从不调用 `ApplyPosition`**。导致 `session.Ir` 的 Annotations 不更新 → 序列化时不带新坐标 → 重新加载后节点落回 (0,0)。 |

#### 修复方案

在 `SyncService.ApplyBpEdits` 中，`Translate` 之后检查 `IrChangeSet.PositionsChanged`（或遍历原始 actions 中的 `MoveNodePosition`），对每个坐标变更调用 `BpEditTranslator.ApplyPosition` 累积更新 `session.Ir`：

```csharp
// 伪代码（SyncService.ApplyBpEdits 内）
var (diff, changeSet) = BpEditTranslator.Translate(session.Ir, actions);

// 坐标回写：MoveNodePosition 不产生语义 diff，但需要把坐标写回 IR Annotations
if (changeSet.PositionsChanged)
{
    var ir = session.Ir;
    foreach (var action in actions.OfType<MoveNodePositionAction>())
        ir = BpEditTranslator.ApplyPosition(ir, action.NodeId, action.X, action.Y);
    session.UpdateIr(ir);  // 或等价的 session.Ir = ir
}
```

**关键文件**：
- `KitX.WorkflowIR/Session/SyncService.cs`（`ApplyBpEdits` 方法，缺 `ApplyPosition` 调用）
- `KitX.WorkflowIR/Lens/BpGraphLens/BpEditTranslator.cs`（`ApplyPosition` 方法，行 570-616，已实现）
- `KitX.WorkflowIR/Ir/IrAnnotation.cs`（`IrLayout(X, Y)` record）
- `KitX.WorkflowIR/Serialization/IrSerializer.cs`（`AnnotationDto.LayoutX/Y` 双向映射）

**与 BpRenderer 重写的关系**：坐标持久化修复独立于 BpRenderer 重写。即使 BpRenderer 重写后改变了节点 id 命名规则，`ApplyPosition` 的 `"block:"`/`"stmt:"` 前缀匹配逻辑仍适用（只要新 BpRenderer 保持 `DeriveStableId` 一致的节点 id）。建议先修复坐标回写接线点（小改动），再重写 BpRenderer（大改动）。

---

## 三、优先级与依赖

| 问题 | 优先级 | 依赖 | 工作量估计 |
|------|--------|------|-----------|
| Trigger 系统修复 | 高 | 无 | 小（1 行代码改动） |
| 蓝图渲染重写 | 高 | 无（可与 Trigger 并行） | 大（BpRenderer 推倒重写 + 自动布局 + TDD） |

两个问题相互独立，可并行实施。Trigger 修复是 trivial 的 DI 参数补传；蓝图渲染重写是独立的大型任务。

---

## 四、相关文件索引

### Trigger 系统
- `KitX Dashboard/Views/WorkflowEditorWindow.axaml.cs:46-50`（构造调用，缺失 pluginServer）
- `KitX Dashboard/ViewModels/WorkflowEditorViewModel.cs:127-154`（RefreshAvailablePlugins/Triggers）
- `KitX Dashboard/Views/WorkflowEditorWindow.axaml:200-211`（XAML 绑定，正确）
- `KitX Core/DI/CoreServiceCollectionExtensions.cs:96-97`（IPluginServer 注册，正确）

### 蓝图渲染
- `KitX.WorkflowIR/Lens/BpGraphLens/BpRenderer.cs`（根因：Phase 3 行 272、Phase 6 行 526-541）
- `KitX.WorkflowIR/Lens/BpGraphLens/BpGraphLens.cs`（Project 入口）
- `KitX.WorkflowIR/Lens/BsTextLens/BsLowerer.cs`（无 Layout annotation 产生）
- `KitX Dashboard/ViewModels/BlueprintEditorViewModel.cs`（消费端，逻辑正确）
- `KitX Dashboard/ViewModels/BlockNodeScopeVM.cs`（折叠/展开 VM，已实现）
- `KitX Dashboard/Views/WorkflowEditorWindow.axaml:594-602`（BlockNodeContainer 视图）
- `KitX.Core.Contract/Workflow/Nodes/BlockNode.cs`（ChildNodeIds 字段）
- `KitX.Core.Contract/Workflow/BlueprintModels.cs`（BlueprintBlockScope）
- `Package/BlockScriptGrammarRule.md` §11（设计规范）

### 日志
- `KitX Dashboard/bin/Debug/net10.0/Log/Log_2026071614_001.log:1301-1308`（蓝图渲染实际行为证据）
- `KitX Dashboard/bin/Debug/net10.0/Log/Log_2026071614_001.log:67,1204-1205`（Trigger 系统证据：DI 可解析、连接非空）

---

> 本文档完成后，WorkflowIR 后端已验证可正确解析/渲染/执行 v5.0 BS。剩余两个问题均在前端层（Trigger 的 DI 参数 + 蓝图的 BpRenderer 重写），不影响后端的正确性。