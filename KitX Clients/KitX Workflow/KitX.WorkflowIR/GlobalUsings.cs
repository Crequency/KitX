// Global usings for KitX.WorkflowIR.
//
// The library is the greenfield successor to the mutable CFG model in KitX.Workflow.
// Its entire IR is immutable (records + ImmutableArray/Dictionary); annotations are
// separated from semantic fields so that structural equality is unaffected by view
// state such as layout coordinates.

global using System.Collections.Immutable;
global using KitX.Core.Contract.Workflow;
