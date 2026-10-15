// Copyright (c) 2026 Andrea Maldini
// SPDX-License-Identifier: MIT OR Apache-2.0

using System;
using System.Linq;
using MaldaLang.Formatting;
using SystemEnvironment = System.Environment;

namespace MaldaLang;

partial class Program
{
    /// <summary><c>malda fmt</c> — see <see cref="FmtCommandRunner"/>.</summary>
    static void FmtCommand(string[] args)
    {
        var runner = new FmtCommandRunner();
        var exitCode = runner.Run(args.Skip(1).ToArray(), Console.Out, Console.Error);
        Console.Out.Flush();
        SystemEnvironment.Exit(exitCode);
    }

    static void ShowFmtHelp() => FmtCommandRunner.PrintUsage(Console.Out);
}
