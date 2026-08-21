// Copyright (c) 2026 Andrea Maldini
// SPDX-License-Identifier: MIT OR Apache-2.0

namespace MaldaLang.Interpreter;

/// <summary>
/// Per-task interpreter state. <c>async f()</c> hot-starts a callee on a forked
/// activation so overlapping tasks do not share <c>_environment</c>, stacks, or
/// <c>this</c>. <see cref="System.Threading.AsyncLocal{T}"/> flows the activation
/// across <c>await</c>; <c>WrapCallAsTask</c> restores the caller on this thread.
/// </summary>
internal sealed class InterpreterActivation
{
    public InterpreterActivation(Interpreter owner, Environment environment)
    {
        Owner = owner;
        Environment = environment;
        CallStack = new List<InterpreterCallStackFrame>();
        ExecutionStack = new Stack<ExecutionFrame>();
        DeferFrames = new Stack<List<Func<Task>>>();
    }

    public Interpreter Owner { get; }

    public Environment Environment { get; set; }
    public ObjectInstance? CurrentObject { get; set; }
    public ClassDefinition? CurrentClass { get; set; }
    public ActorInstance? CurrentActor { get; set; }
    public string? CurrentFile { get; set; }
    public List<InterpreterCallStackFrame> CallStack { get; }
    public Stack<ExecutionFrame> ExecutionStack { get; }
    public Stack<List<Func<Task>>> DeferFrames { get; }
    public WorkflowExecutionContext? WorkflowContext { get; set; }
    public bool InsideWorkflowStep { get; set; }

    /// <summary>
    /// New stacks for a hot-started task; shares the caller's env/this/file/workflow
    /// flags until <c>CallFunctionAsync</c> installs the callee frame.
    /// </summary>
    public InterpreterActivation ForkForTask()
    {
        return new InterpreterActivation(Owner, Environment)
        {
            CurrentObject = CurrentObject,
            CurrentClass = CurrentClass,
            CurrentActor = CurrentActor,
            CurrentFile = CurrentFile,
            WorkflowContext = WorkflowContext,
            InsideWorkflowStep = InsideWorkflowStep
        };
    }
}
