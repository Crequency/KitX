// Global usings for KitX.WorkflowV6.
//
// This library is the experimental successor to KitX.WorkflowIR on the dev=v6-grammar
// branch. It is the test bed for the structured KScript grammar proposal captured
// in `Package/Structured-BS-Discussion-Notes.md`: indented (Python-style) blocks,
// removal of Goto, structured control flow (if/switch/forEach/while/break/continue/exit),
// IR as a structured AST (not block + Goto), and structured C# as the compile target.
//
// Conventions inherited from KitX.WorkflowIR (the immediate predecessor):
//   • The IR is immutable (records + ImmutableArray/Dictionary).
//   • View state (canvas positions, comments) lives in Annotations, separated from
//     semantic fields so that structural equality is unaffected by view state.
//   • Builtins are discovered by reflection and split per role (parser/lowering/codegen/
//     bp-render/bp-reverse) — a single builtin implements only the roles it needs.
//
// The architectural skeleton here mirrors KitX.WorkflowIR's directory layout so that
// design discussion can proceed against a familiar map. All method bodies are
// deliberately empty / throw NotImplementedException: this commit establishes the
// *shape* of the future library, not its behaviour. Filling in the shape is the work
// of the follow-up implementation plan.

global using System.Collections.Immutable;
global using KitX.Core.Contract.Workflow;
