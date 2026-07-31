namespace MaldaLang.Tests;

public sealed record BehaviorSnapshot(
    string Mode,
    string PropertyName,
    MaldaLang.Interpreter.PropertyExecutionStatus Status,
    int Seed,
    int Iterations,
    int? FailedTrial,
    string? ErrorMessage,
    string? Counterexample,
    string? ShrunkCounterexample,
    string? SkipReason,
    string StdOut = "",
    string StdErr = "",
    int ExitCode = 0)
{
    public bool Passed => Status == MaldaLang.Interpreter.PropertyExecutionStatus.Passed;
    public bool Skipped => Status == MaldaLang.Interpreter.PropertyExecutionStatus.Skipped;

    public static BehaviorSnapshot FromPropertyResult(string mode, MaldaLang.Interpreter.PropertyRunResult result)
    {
        return new BehaviorSnapshot(
            Mode: mode,
            PropertyName: result.PropertyName,
            Status: result.Status,
            Seed: result.Seed,
            Iterations: result.Iterations,
            FailedTrial: result.FailedTrial,
            ErrorMessage: result.ErrorMessage,
            Counterexample: result.Counterexample,
            ShrunkCounterexample: result.ShrunkCounterexample,
            SkipReason: result.SkipReason);
    }
}
