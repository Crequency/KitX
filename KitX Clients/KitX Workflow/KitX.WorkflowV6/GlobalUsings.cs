// Global usings for KitX.WorkflowV6.
//
// This library is the experimental successor to KitX.WorkflowIR on the dev=v6-grammar
// branch. It implements the structured KScript grammar proposal captured in
// `Package/Archive/WorkflowV6-Docs/Structured-BS-Discussion-Notes.md`: indented
// (Python-style) blocks, removal of Goto, structured control flow
// (if/switch/forEach/while/break/continue), IR as a structured AST (not block +
// Goto), and structured C# as the compile target.
//
// Conventions inherited from KitX.WorkflowIR (the immediate predecessor):
//   • The IR is immutable (records + ImmutableArray/Dictionary).
//   • View state (canvas positions, comments) lives in Annotations, separated from
//     semantic fields so that structural equality is unaffected by view state.
//   • Builtins are discovered by reflection; v6 ships 41 builtin functions across
//     25 source files (Print/Range/Compare/Add/Sub/Mul/Div/Mod/Len/StringConcat +
//     Pause/File I/O + 7 JSON + 9 dict + 3 plugin-call + 9 service-management).
//
// Implementation status (see Package/Archive/WorkflowV6-Docs/WorkflowV6-Handoff.md
// for full reference):
//   • Phase 1-10 fully implemented (476 tests passing).
//   • KsTextLens + BpGraphLens (Project/Reverse) fully implemented.
//   • StructuredRoslynBackend (IExecutionBackend) fully implemented.
//   • SyncService.ApplyKsEdit fully functional; ApplyBpEdits deferred to project P2
//     milestone (dual-pane live highlight) — see V6-BpEditAction-Future-Design-ADR.md.

global using System.Collections.Immutable;
global using KitX.Core.Contract.Workflow;
