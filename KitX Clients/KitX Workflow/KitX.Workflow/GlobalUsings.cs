// Global usings for the KitX.Workflow library.
// These are shared across all source files so that the migrated code (which originally
// relied on file-level usings under KitX.Core.Workflow) resolves common namespaces
// without each file having to repeat them.

global using KitX.Workflow.Hosting;
global using KitX.Workflow.Abstractions;
global using KitX.Workflow.Models;
global using KitX.Workflow.Models.Statements;
global using KitX.Workflow.Models.Results;
// Compilation / BlockScripting / Conversion are the three core subsystem namespaces and are
// cross-referenced bidirectionally (e.g. Compilation's CSCompiler uses BlockScripting's
// BuiltinFunctionRegistry and Conversion's ConversionPaths; BlockScripting's BlockScriptExecutor
// uses Compilation's CSCompiler). Declaring them globally avoids per-file using churn.
global using KitX.Workflow.Compilation;
global using KitX.Workflow.BlockScripting;
global using KitX.Workflow.Conversion;
global using KitX.Workflow.CFG;
// Services holds the high-level workflow service implementations (WorkflowManagementService,
// RealPluginManager, WorkflowOutput, etc.) consumed by Hosting's DI wiring and by the
// BuiltinFunctions that call into plugin/workflow services. Global so those consumers resolve
// the concrete service types without per-file using churn.
global using KitX.Workflow.Services;
