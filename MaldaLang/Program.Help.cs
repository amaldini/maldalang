// Copyright (c) 2026 Andrea Maldini
// SPDX-License-Identifier: MIT OR Apache-2.0

using MaldaLang;
using MaldaLang.Parser;
using MaldaLang.Parser.AST.Declarations;
using MaldaLang.Parser.AST.Expressions;
using MaldaLang.Interpreter;
using MaldaLang.PackageManager;
using System;
using System.Text;
using System.Reflection;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using System.Threading;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Diagnostics;
using MaldaLang.Testing;
using MaldaLang.Scaffolding;
using MaldaLang.Deployment;
using MaldaLang.UIHost;
using MaldaLang.Runtime.Profiling;
using MaldaLang.Runtime.Workflows;
using MaldaLang.Cli;
using MaldaLang.Eval;
using MaldaLang.IDE;
using MaldaLang.IDE.Models;
using MaldaLang.BuiltIns;
using MaldaLang.DebugAdapter;
using SystemEnvironment = System.Environment;
using PackageManager = MaldaLang.PackageManager.PackageManager;

namespace MaldaLang;

partial class Program
{
    static void ShowHelp()
    {
        Console.WriteLine();
        Console.WriteLine("MALDA CLI");
        Console.WriteLine();
        Console.WriteLine("Usage:");
        Console.WriteLine("  malda <file.malda> [--strict-types] [--profile ...]");
        Console.WriteLine("  malda <command> [options]");
        Console.WriteLine("  malda -e \"<code>\" | malda --exec \"<code>\" [--strict-types] | malda -c \"<code>\" | malda --check \"<code>\"");
        Console.WriteLine("  --strict-types                On run: reject unknown hints, non-exhaustive match, @pure/bounds. On compile --mode transpile: default (refuse emit on Errors). Escape: --lenient-types");
        Console.WriteLine("  echo \"<code>\" | malda");
        Console.WriteLine();
        Console.WriteLine("Getting started:");
        Console.WriteLine("  doctor      Fast diagnostics for runtime, config, providers, and project scaffold");
        Console.WriteLine("  onboard     Create ~/.malda, starter config, optional model downloads");
        Console.WriteLine("  status      Show MALDA home, config, provider, and cron status");
        Console.WriteLine("  new         Scaffold a webapi, fullstack, or game project");
        Console.WriteLine("  play        Compile a .malda file to JavaScript and serve a local canvas/JS preview (live reload)");
        Console.WriteLine();
        Console.WriteLine("Build, test, and ship:");
        Console.WriteLine("  check       Diagnose a .malda file without executing (add --json / --fix)");
        Console.WriteLine("  fmt         Format .malda files in place (--check for CI, --stdin for editors)");
        Console.WriteLine("  lint        Report gotchas as ML0xxx findings (--json; exit 1 on warnings)");
        Console.WriteLine("  eval        Run suite / case evals (not malda test)");
        Console.WriteLine("  prompts     Compare prompt hashes (--diff <baseline.json>)");
        Console.WriteLine("  compile     Compile a MALDA file to exe, dll, js, pwa, or fullstack output");
        Console.WriteLine("  test        Discover and run MALDA tests");
        Console.WriteLine("  db          Inspect, migrate, seed, and roll back scaffolded local-first SQLite state");
        Console.WriteLine("  deploy      Validate deploy, profile, and observability contracts");
        Console.WriteLine("  workflow    Start and inspect durable workflows");
        Console.WriteLine();
        Console.WriteLine("AI and automation:");
        Console.WriteLine("  agent       Chat with the MALDA assistant or run a one-shot prompt");
        Console.WriteLine("  gateway     Long-running Telegram gateway with optional in-process cron");
        Console.WriteLine("  memory      Inspect and maintain GraphMemory artifacts");
        Console.WriteLine("  cron        Add, list, remove, or install scheduled jobs");
        Console.WriteLine();
        Console.WriteLine("Packages and diagnostics:");
        Console.WriteLine("  init        Initialize package.json (offline)");
        Console.WriteLine("  install     Install from local path or remote registry");
        Console.WriteLine("  uninstall   Remove an installed package (offline)");
        Console.WriteLine("  list        List installed packages; list --workspace for packages/");
        Console.WriteLine("  search      Search a remote registry (needs MALDA_REGISTRY_URL)");
        Console.WriteLine("  trace       Summarize, inspect, or replay trace files");
        Console.WriteLine("  symbols     Print classes, functions, and actors from a MALDA file");
        Console.WriteLine("  debug-adapter  DAP stdio (editors; do not mix with malda-lsp)");
        Console.WriteLine("  help        Show top-level help or help for a specific command");
        Console.WriteLine();
        Console.WriteLine("Examples:");
        Console.WriteLine("  malda check app.malda --json");
        Console.WriteLine("  malda fmt src --check");
        Console.WriteLine("  malda lint src --json");
        Console.WriteLine("  malda doctor");
        Console.WriteLine("  malda new webapi my-api");
        Console.WriteLine("  malda new fullstack sales-portal --local-first");
        Console.WriteLine("  malda new game my-game");
        Console.WriteLine("  malda new game my-scores --fullstack");
        Console.WriteLine("  malda new agent my-agent");
        Console.WriteLine("  malda play app.malda");
        Console.WriteLine("  malda db status");
        Console.WriteLine("  malda compile app.malda -o app.exe");
        Console.WriteLine("  malda test tests --filter Smoke");
        Console.WriteLine("  malda workflow list");
        Console.WriteLine("  malda agent -m \"Summarize this file\"");
        Console.WriteLine("  malda agent develop");
        Console.WriteLine("  malda agent develop malda");
        Console.WriteLine("  malda help deploy");
        Console.WriteLine();
        Console.WriteLine("REPL commands:");
        Console.WriteLine("  run | compile | transpile | vars | source | drop | replace | editline | insert | help | exit");
        Console.WriteLine("  vars [kind]                List session definitions (aliases: defs, who)");
        Console.WriteLine("                             kind: variables, functions, classes, prompts, actors, workflows");
        Console.WriteLine("  source [kind|name]         Print definition source as entered (alias: src)");
        Console.WriteLine("                             A name prints line numbers for editline and insert.");
        Console.WriteLine("  drop <name>                Remove a definition (alias: undef)");
        Console.WriteLine("  replace <name>             Drop and reprint source so you can re-enter it (alias: edit)");
        Console.WriteLine("  editline <name> [<line> [text]]");
        Console.WriteLine("                             Without a line, print numbered source. With a line, replace it.");
        Console.WriteLine("                             Omit text to enter several lines; a blank line finishes.");
        Console.WriteLine("  insert <name> [after <line>]");
        Console.WriteLine("                             Without a line, print numbered source. Then insert after that line.");
        Console.WriteLine();
        Console.WriteLine("Use 'malda help <command>' for command-specific usage.");
        Console.WriteLine();
    }

    static bool TryShowCommandHelp(string command)
    {
        var normalized = command.Trim().ToLowerInvariant();
        switch (normalized)
        {
            case "check":
                CheckCommandRunner.PrintUsage(Console.Out);
                return true;
            case "fmt":
                ShowFmtHelp();
                return true;
            case "lint":
                ShowLintHelp();
                return true;
            case "eval":
                new EvalCommandRunner().Run(new[] { "--help" }, Console.Out, Console.Error);
                return true;
            case "prompts":
                new PromptsCommandRunner().Run(new[] { "--help" }, Console.Out, Console.Error);
                return true;
            case "doctor":
                DoctorCommandRunner.PrintUsage(Console.Out);
                return true;
            case "compile":
                ShowCompileHelp();
                return true;
            case "test":
                ShowTestHelp();
                return true;
            case "db":
                DbCommandRunner.PrintUsage(Console.Out);
                return true;
            case "new":
                NewCommandOptionsParser.WriteUsage(Console.Out);
                return true;
            case "play":
                PlayCommandOptionsParser.WriteUsage(Console.Out);
                return true;
            case "deploy":
                _ = new DeployCommandRunner().Run(new[] { "--help" }, Console.Out, Console.Error);
                return true;
            case "trace":
                ShowTraceHelp();
                return true;
            case "cron":
                ShowCronHelp();
                return true;
            case "workflow":
                ShowWorkflowHelp();
                return true;
            case "agent":
                ShowAgentHelp();
                return true;
            case "gateway":
                ShowGatewayHelp();
                return true;
            case "memory":
                ShowMemoryHelp();
                return true;
            case "status":
                ShowStatusHelp();
                return true;
            case "onboard":
                ShowOnboardHelp();
                return true;
            case "symbols":
                ShowSymbolsHelp();
                return true;
            case "debug-adapter":
                ShowDebugAdapterHelp();
                return true;
            case "install":
            case "uninstall":
            case "list":
            case "search":
            case "init":
                ShowPackageManagerHelp();
                return true;
            default:
                return false;
        }
    }

    static bool IsHelpFlag(string value)
    {
        return value == "--help" || value == "-h";
    }

    static void ShowCompileHelp()
    {
        Console.WriteLine("Usage: malda compile|publish <input.malda|input.malda.html> [-o <output.exe|output.dll|output.js|output.mjs|output-dir>] [--mode interpreter|transpile|dll|js|esm|pwa|fullstack] [--target js|esm|pwa|fullstack] [--include-ui-host] [--minify] [--embed-folder <dir[=alias]>] [--with-trading] [--profile] [--profile-output <path>] [--profile-format text|json|both] [--profile-periodic-seconds N]");
        Console.WriteLine("  publish                       Alias for compile --mode transpile (executable publish layout)");
        Console.WriteLine("  -o <path>                     Output executable, DLL, JS file, or PWA directory");
        Console.WriteLine("  --mode <mode>                 interpreter (default), transpile, dll, js, esm (ES module), pwa, or fullstack");
        Console.WriteLine("  --target <js|esm|pwa|fullstack> Alias for --mode js, esm, pwa, or fullstack");
        Console.WriteLine("  --include-ui-host             Force embedding UIHost runtime in transpiled executable");
        Console.WriteLine("  --minify                      js / esm / pwa: minify with esbuild when installed (MALDA_ESBUILD, PATH, node_modules); prints sizes");
        Console.WriteLine("  --embed-folder <dir[=alias]>  Embed a directory as embed:<alias>/... (repeatable)");
        Console.WriteLine("  --with-trading                Bundle optional timeseries and trading pack DLLs beside the executable");
        Console.WriteLine("  --profile                     Enable MALDA profiling in the compiled executable");
        Console.WriteLine("  --profile-output <path>       Write the profile report to a file path");
        Console.WriteLine("  --profile-format <format>     text, json, or both");
        Console.WriteLine("  --profile-periodic-seconds N  Rewrite profile output every N seconds while running (0 = only at exit)");
    }

    static void ShowTestHelp()
    {
        Console.WriteLine("Usage: malda test [path] [--list] [--filter text] [--format human|ci] [--iterations N] [--seed S] [--target interpret|js] [--write-regression] [--regression-dir <path>]");
        Console.WriteLine("  path                          Root directory or test file to discover from (default: current directory)");
        Console.WriteLine("  --list                        Print discovered tests without executing them");
        Console.WriteLine("  --filter <text>               Restrict discovery to matching file names");
        Console.WriteLine("  --format <human|ci>           Human-readable or CI-oriented output");
        Console.WriteLine("  --iterations <N>              Property-test iterations");
        Console.WriteLine("  --seed <S>                    Property-test seed");
        Console.WriteLine("  --target <interpret|js>       Backend to run on (default interpret). js compiles each file and runs it in Node");
        Console.WriteLine("  --write-regression            Write regression artifacts for failing properties");
        Console.WriteLine("  --regression-dir <path>       Output directory for regression artifacts");
    }

    static void ShowTraceHelp()
    {
        Console.WriteLine("Usage:");
        Console.WriteLine("  malda trace summary <traceFile>");
        Console.WriteLine("  malda trace show <traceFile> [--from N] [--to M] [--type TYPE]");
        Console.WriteLine("  malda trace replay <traceFile> [--output <directory>]");
        Console.WriteLine("  malda trace replay-tools <traceFile> --workdir <directory>");
        Console.WriteLine("  replay-tools re-runs recorded tool calls in --workdir (no LLM). Skips web_search, ask_user, remember_progress, and recall_progress.");
    }

    static void ShowCronHelp()
    {
        Console.WriteLine("Usage: malda cron <add|list|remove|install> [options]");
        Console.WriteLine("  add      Add a scheduled MALDA job (--scope defaults to cron:<name>)");
        Console.WriteLine("  list     List stored cron jobs");
        Console.WriteLine("  remove   Remove a stored cron job");
        Console.WriteLine("  install  Install jobs with the OS scheduler");
    }

    static void ShowGatewayHelp()
    {
        Console.WriteLine("Usage: malda gateway [stop] [-c telegram] [--no-cron]");
        Console.WriteLine("  stop                   Stop a running gateway (removes ~/.malda/gateway.pid)");
        Console.WriteLine("  -c, --channel <name>   Channel adapter (default: telegram)");
        Console.WriteLine("  --no-cron              Disable in-process cron scheduler");
        Console.WriteLine("  Runs the assistant over Telegram until Ctrl+C. Writes ~/.malda/gateway.pid while active.");
        Console.WriteLine("  Set channels.telegram.notifyChatId (or MALDA_GATEWAY_NOTIFY_CHAT_ID) for cron/crash Telegram alerts.");
    }

    static void ShowWorkflowHelp()
    {
        Console.WriteLine("Usage: malda workflow <start|list|get|steps|events|report|metrics|resume|retry|cancel|approve|signal|dlq|maintenance> [options]");
        Console.WriteLine("  start       Start a workflow instance from a MALDA file");
        Console.WriteLine("  list        List workflow instances");
        Console.WriteLine("  get         Show workflow instance details");
        Console.WriteLine("  steps       Show persisted step state");
        Console.WriteLine("  events      Show workflow events");
        Console.WriteLine("  report      Unified ops report (instance, steps, timeline, DLQ)");
        Console.WriteLine("  metrics     Show workflow metrics");
        Console.WriteLine("  resume      Resume a paused workflow");
        Console.WriteLine("  retry       Retry a workflow instance");
        Console.WriteLine("  cancel      Cancel a workflow instance");
        Console.WriteLine("  approve     Resolve a human approval step");
        Console.WriteLine("  signal      Send a signal to a workflow");
        Console.WriteLine("  dlq         Inspect or requeue dead letters");
        Console.WriteLine("  maintenance Run workflow maintenance tasks");
    }

    static void ShowAgentHelp()
    {
        Console.WriteLine("Usage: malda agent [-m <message>] [-c <channel>] [-b <backend>]");
        Console.WriteLine("       malda agent develop [generic|malda] [-m <message>] [-b <backend>] [--workdir <dir>]");
        Console.WriteLine("  -m, --message <text>          Send a one-shot message instead of starting chat mode");
        Console.WriteLine("  -c, --channel <name>          Route through a channel such as telegram");
        Console.WriteLine("  -b, --backend <name>          Override the assistant backend (for example local-llama)");
        Console.WriteLine("  develop                       Coding agent in the working directory (default: generic DevAgent)");
        Console.WriteLine("  develop malda                 MALDACodingAgent: .malda edit, run_malda, compile_malda");
        Console.WriteLine("  --kind <generic|malda>        Same split as the positional kind");
        Console.WriteLine("  --workdir <dir>               Project directory for develop (default: current directory)");
        Console.WriteLine("  Develop mode stores memory under ~/.malda/memory/develop or develop-malda");
        Console.WriteLine("  and skips ~/.malda/assistant.malda unless MALDA_AGENT_SCRIPT is set.");
        Console.WriteLine("  MALDA_AGENT_TRACE=1 records traces/<session>.malda-trace.jsonl (or set it to a directory).");
        Console.WriteLine("  Replay those tool calls with: malda trace replay-tools <file> --workdir <dir>");
    }

    static void ShowMemoryHelp()
    {
        Console.WriteLine("Usage: malda memory <stats|validate|reindex|prune|reflect|export-bundle|watch|download-rerank> [options]");
        Console.WriteLine("  --path <path>                 Memory base path (default: ~/.malda/memory/assistant)");
        Console.WriteLine("  --json                        Output JSON");
        Console.WriteLine("  stats                         Show memory statistics");
        Console.WriteLine("  validate                      Health check (exit 1 if issues found)");
        Console.WriteLine("  reindex --dir <dir> --pattern <glob> [--scope <scope>]");
        Console.WriteLine("  prune [--type <type>] [--scope <scope>] [--older-than-days <n>] [--consolidated]");
        Console.WriteLine("  reflect [--scope <scope>] [--dry-run] [--min-confidence <0..1>]");
        Console.WriteLine("  export-bundle [-o <path>]");
        Console.WriteLine("  watch --dir <dir> [--pattern <glob>]   Reindex on KB changes");
        Console.WriteLine("  download-rerank               Download ONNX cross-encoder to ~/.malda/models/cross-encoder");
    }

    static void ShowStatusHelp()
    {
        Console.WriteLine("Usage: malda status [--json]");
        Console.WriteLine("  Show MALDA home, config, channels, skills, gateway, memory stats, and cron jobs.");
        Console.WriteLine("  --json                 Machine-readable JSON output");
    }

    static void ShowOnboardHelp()
    {
        Console.WriteLine("Usage: malda onboard [--download-rerank] [--download-local-llama]");
        Console.WriteLine("  Create ~/.malda, skills/, memory/, and a starter config.json.");
        Console.WriteLine("  --download-rerank        Download ONNX cross-encoder for agents.memory.rerankMode onnx");
        Console.WriteLine("  --download-local-llama   Download default GGUF model and set providers.local_llama.modelPath");
    }

    static void ShowSymbolsHelp()
    {
        Console.WriteLine("Usage: malda --symbols <file.malda>");
        Console.WriteLine("  Print classes, functions, actors, and other top-level symbols from a MALDA file.");
    }

    static void ShowDebugAdapterHelp()
    {
        Console.WriteLine("Usage: malda debug-adapter");
        Console.WriteLine("  Speaks Debug Adapter Protocol on stdin/stdout for editors.");
        Console.WriteLine("  Do not mix with malda-lsp — LSP and DAP each need exclusive stdio.");
        Console.WriteLine("  Interpret-mode only. VS Code F5 wiring is a separate extension contribution.");
    }
}
