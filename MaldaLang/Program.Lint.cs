// Copyright (c) 2026 Andrea Maldini
// SPDX-License-Identifier: MIT OR Apache-2.0

using System;
using System.Linq;
using MaldaLang.Linting;
using SystemEnvironment = System.Environment;

namespace MaldaLang;

partial class Program
{
    /// <summary><c>malda lint</c> — see <see cref="LintCommandRunner"/>.</summary>
    static void LintCommand(string[] args)
    {
        var runner = new LintCommandRunner();
        var exitCode = runner.Run(args.Skip(1).ToArray(), Console.Out, Console.Error);
        Console.Out.Flush();
        SystemEnvironment.Exit(exitCode);
    }

    static void ShowLintHelp() => LintCommandRunner.PrintUsage(Console.Out);
}
