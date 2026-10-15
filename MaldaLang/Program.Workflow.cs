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
    static int WorkflowCommand(string[] args)
    {
        static void PrintWorkflowUsage()
        {
            Console.Error.WriteLine("Usage: malda workflow <start|list|get|steps|events|report|metrics|resume|retry|cancel|approve|signal|dlq|maintenance> [options]");
            Console.Error.WriteLine("  malda workflow start <file.malda> <workflowName> [--input file.json]");
            Console.Error.WriteLine("  malda workflow list [--status ...] [--name ...] [--limit ...]");
            Console.Error.WriteLine("  malda workflow get <instanceId>");
            Console.Error.WriteLine("  malda workflow steps <instanceId>");
            Console.Error.WriteLine("  malda workflow events <instanceId> [--limit ...]");
            Console.Error.WriteLine("  malda workflow report <instanceId> [--events-limit ...]");
            Console.Error.WriteLine("  malda workflow metrics");
            Console.Error.WriteLine("  malda workflow resume <instanceId>");
            Console.Error.WriteLine("  malda workflow retry <instanceId>");
            Console.Error.WriteLine("  malda workflow cancel <instanceId> [--reason \"...\"]");
            Console.Error.WriteLine("  malda workflow approve <instanceId> <stepId> [--decision approve|reject|timeout] [--payload file.json]");
            Console.Error.WriteLine("  malda workflow signal <instanceId> <signalName> [--payload file.json]");
            Console.Error.WriteLine("  malda workflow dlq list [--limit ...] [--pending-only]");
            Console.Error.WriteLine("  malda workflow dlq requeue <deadLetterId> [--reason \"...\"] [--by \"operator\"] [--correlation-id \"...\"]");
            Console.Error.WriteLine("  malda workflow maintenance run [--operational-days ...] [--audit-days ...] [--compaction-days ...] [--batch ...] [--dry-run]");
            Console.Error.WriteLine("  global options: --format human|json, --json");
        }

        if (args.Length < 1)
        {
            PrintWorkflowUsage();
            return 1;
        }

        var normalizedArgs = new List<string>();
        var format = "human";
        for (var i = 0; i < args.Length; i++)
        {
            if (args[i] == "--json")
            {
                format = "json";
                continue;
            }
            if ((args[i] == "--format" || args[i] == "-f") && i + 1 < args.Length)
            {
                format = args[i + 1].ToLowerInvariant();
                i++;
                continue;
            }
            normalizedArgs.Add(args[i]);
        }

        if (format != "human" && format != "json")
        {
            Console.Error.WriteLine("Invalid format. Supported values: human, json.");
            return 4;
        }

        args = normalizedArgs.ToArray();
        if (args.Length < 1)
        {
            PrintWorkflowUsage();
            return 1;
        }

        var jsonMode = format == "json";
        var sub = args[0].ToLowerInvariant();
        var engine = WorkflowEngine.Instance;
        void WriteJson(object payload) => Console.WriteLine(JsonSerializer.Serialize(payload));

        try
        {
            if (sub == "start")
            {
                engine.EnsureWorkflowsEnabled("workflow start");
                if (args.Length < 3)
                {
                    Console.Error.WriteLine("Usage: malda workflow start <file.malda> <workflowName> [--input file.json]");
                    return 1;
                }
                var filePath = args[1];
                var workflowName = args[2];
                var inputJson = "{}";
                for (var i = 3; i < args.Length; i++)
                {
                    if ((args[i] == "--input" || args[i] == "-i") && i + 1 < args.Length)
                    {
                        inputJson = File.ReadAllText(args[i + 1]);
                        i++;
                    }
                }
                if (!File.Exists(filePath))
                {
                    Console.Error.WriteLine($"File not found: {filePath}");
                    return 2;
                }
                var fileContent = File.ReadAllText(filePath);
                var escaped = inputJson.Replace("\\", "\\\\").Replace("\"", "\\\"").Replace("\r", "").Replace("\n", "\\n");
                var harness = jsonMode
                    ? fileContent + "\nvar __wfId = startWorkflow(\"" + workflowName + "\", parseJSON(\"" + escaped + "\"));\nprint(toJSON({\"instanceId\": __wfId}));"
                    : fileContent + "\nprint(startWorkflow(\"" + workflowName + "\", parseJSON(\"" + escaped + "\")));";
                Run(harness, null, filePath);
                return 0;
            }
            if (sub == "list")
            {
                var status = (string?)null;
                var name = (string?)null;
                var limit = 100;
                for (var i = 1; i < args.Length; i++)
                {
                    if ((args[i] == "--status" || args[i] == "-s") && i + 1 < args.Length) { status = args[i + 1]; i++; }
                    else if ((args[i] == "--name" || args[i] == "-n") && i + 1 < args.Length) { name = args[i + 1]; i++; }
                    else if ((args[i] == "--limit" || args[i] == "-l") && i + 1 < args.Length) { int.TryParse(args[i + 1], out limit); i++; }
                }
                var instances = engine.ListInstances(status, name, limit);
                if (jsonMode)
                    WriteJson(instances);
                else
                    foreach (var inst in instances)
                        Console.WriteLine($"{inst.Id}  {inst.Name}  {inst.Status}  {inst.CreatedAtUtc}");
                return 0;
            }
            if (sub == "get")
            {
                if (args.Length < 2)
                {
                    Console.Error.WriteLine("Usage: malda workflow get <instanceId>");
                    return 1;
                }
                var inst = engine.GetInstance(args[1]);
                if (inst == null)
                {
                    Console.Error.WriteLine($"Workflow instance not found: {args[1]}");
                    return 2;
                }
                if (jsonMode)
                    WriteJson(inst);
                else
                {
                    Console.WriteLine($"id: {inst.Id}");
                    Console.WriteLine($"name: {inst.Name}");
                    Console.WriteLine($"status: {inst.Status}");
                    Console.WriteLine($"created_at_utc: {inst.CreatedAtUtc}");
                    if (!string.IsNullOrWhiteSpace(inst.CorrelationId)) Console.WriteLine($"correlation_id: {inst.CorrelationId}");
                    if (inst.ResultJson != null) Console.WriteLine($"result: {inst.ResultJson}");
                    if (inst.ErrorJson != null) Console.WriteLine($"error: {inst.ErrorJson}");
                }
                return 0;
            }
            if (sub == "steps")
            {
                if (args.Length < 2)
                {
                    Console.Error.WriteLine("Usage: malda workflow steps <instanceId>");
                    return 1;
                }
                var steps = engine.GetSteps(args[1]);
                if (jsonMode)
                    WriteJson(steps);
                else
                    foreach (var s in steps)
                        Console.WriteLine($"{s.StepName}  {s.State}  attempt={s.Attempt}  output={s.OutputJson ?? s.ErrorJson ?? "-"}");
                return 0;
            }
            if (sub == "events")
            {
                if (args.Length < 2)
                {
                    Console.Error.WriteLine("Usage: malda workflow events <instanceId> [--limit ...]");
                    return 1;
                }

                var limit = 200;
                for (var i = 2; i < args.Length; i++)
                {
                    if ((args[i] == "--limit" || args[i] == "-l") && i + 1 < args.Length)
                    {
                        int.TryParse(args[i + 1], out limit);
                        i++;
                    }
                }

                var events = engine.GetEvents(args[1], limit);
                if (jsonMode)
                {
                    WriteJson(events);
                }
                else
                {
                    foreach (var evt in events)
                        Console.WriteLine($"{evt.CreatedAtUtc}  {evt.EventType}  {evt.PayloadJson}");
                }
                return 0;
            }
            if (sub == "report")
            {
                if (args.Length < 2)
                {
                    Console.Error.WriteLine("Usage: malda workflow report <instanceId> [--events-limit ...]");
                    return 1;
                }

                var eventLimit = 200;
                for (var i = 2; i < args.Length; i++)
                {
                    if ((args[i] == "--events-limit" || args[i] == "--limit" || args[i] == "-l") && i + 1 < args.Length)
                    {
                        int.TryParse(args[i + 1], out eventLimit);
                        i++;
                    }
                }

                var report = engine.GetOpsReport(args[1], eventLimit);
                if (report == null)
                {
                    Console.Error.WriteLine($"Workflow instance not found: {args[1]}");
                    return 2;
                }

                if (jsonMode)
                {
                    Console.WriteLine(JsonSerializer.Serialize(report, new JsonSerializerOptions
                    {
                        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
                    }));
                }
                else
                {
                    var inst = report.Instance;
                    Console.WriteLine("=== Instance ===");
                    Console.WriteLine($"id: {inst.Id}");
                    Console.WriteLine($"name: {inst.Name}");
                    Console.WriteLine($"status: {inst.Status}");
                    Console.WriteLine($"created_at_utc: {inst.CreatedAtUtc}");
                    Console.WriteLine($"updated_at_utc: {inst.UpdatedAtUtc}");
                    if (!string.IsNullOrWhiteSpace(inst.StartedAtUtc)) Console.WriteLine($"started_at_utc: {inst.StartedAtUtc}");
                    if (!string.IsNullOrWhiteSpace(inst.FinishedAtUtc)) Console.WriteLine($"finished_at_utc: {inst.FinishedAtUtc}");
                    if (!string.IsNullOrWhiteSpace(inst.CorrelationId)) Console.WriteLine($"correlation_id: {inst.CorrelationId}");
                    if (inst.InputJson != null) Console.WriteLine($"input: {inst.InputJson}");
                    if (inst.ResultJson != null) Console.WriteLine($"result: {inst.ResultJson}");
                    if (inst.ErrorJson != null) Console.WriteLine($"error: {inst.ErrorJson}");
                    Console.WriteLine($"generated_at_utc: {report.GeneratedAtUtc}");

                    Console.WriteLine();
                    Console.WriteLine("=== Steps ===");
                    if (report.Steps.Count == 0)
                        Console.WriteLine("(none)");
                    else
                        foreach (var s in report.Steps)
                            Console.WriteLine($"{s.StepName}  {s.State}  attempt={s.Attempt}/{s.MaxAttempts}  kind={s.StepKind}  output={s.OutputJson ?? s.ErrorJson ?? "-"}");

                    Console.WriteLine();
                    Console.WriteLine("=== Timeline events ===");
                    if (report.Events.Count == 0)
                        Console.WriteLine("(none)");
                    else
                        foreach (var evt in report.Events)
                            Console.WriteLine($"{evt.CreatedAtUtc}  {evt.EventType}  {evt.PayloadJson}");

                    Console.WriteLine();
                    Console.WriteLine("=== Dead letters ===");
                    if (report.DeadLetters.Count == 0)
                        Console.WriteLine("(none)");
                    else
                        foreach (var dlq in report.DeadLetters)
                            Console.WriteLine($"{dlq.Id}  step={dlq.StepName}  reason={dlq.Reason}  requeued={dlq.RequeuedAtUtc ?? "-"}  created={dlq.CreatedAtUtc}");
                }
                return 0;
            }
            if (sub == "metrics")
            {
                var metrics = engine.GetMinimumMetricSnapshot();
                if (jsonMode)
                {
                    WriteJson(metrics);
                }
                else
                {
                    foreach (var kvp in metrics.OrderBy(k => k.Key, StringComparer.Ordinal))
                        Console.WriteLine($"{kvp.Key}: {kvp.Value}");
                }
                return 0;
            }
            if (sub == "resume")
            {
                if (args.Length < 2) { Console.Error.WriteLine("Usage: malda workflow resume <instanceId>"); return 1; }
                var ok = engine.ResumeInstance(args[1]);
                if (jsonMode) WriteJson(new { success = ok, instanceId = args[1] });
                else Console.WriteLine(ok ? "Resumed" : "Not resumed (invalid state)");
                return ok ? 0 : 3;
            }
            if (sub == "retry")
            {
                if (args.Length < 2) { Console.Error.WriteLine("Usage: malda workflow retry <instanceId>"); return 1; }
                var ok = engine.RetryInstance(args[1]);
                if (jsonMode) WriteJson(new { success = ok, instanceId = args[1] });
                else Console.WriteLine(ok ? "Retry requested" : "Not retried (invalid state)");
                return ok ? 0 : 3;
            }
            if (sub == "cancel")
            {
                if (args.Length < 2) { Console.Error.WriteLine("Usage: malda workflow cancel <instanceId> [--reason \"...\"]"); return 1; }
                var reason = (args.Length >= 4 && args[2] == "--reason") ? args[3] : null;
                var ok = engine.CancelInstance(args[1], reason);
                if (jsonMode) WriteJson(new { success = ok, instanceId = args[1], reason });
                else Console.WriteLine(ok ? "Cancelled" : "Not cancelled (invalid state)");
                return ok ? 0 : 3;
            }
            if (sub == "approve")
            {
                if (args.Length < 3)
                {
                    Console.Error.WriteLine("Usage: malda workflow approve <instanceId> <stepId> [--decision approve|reject|timeout] [--payload file.json]");
                    return 1;
                }

                var instanceId = args[1];
                var stepId = args[2];
                var decision = "approve";
                string? payloadJson = null;

                for (var i = 3; i < args.Length; i++)
                {
                    if ((args[i] == "--decision" || args[i] == "-d") && i + 1 < args.Length)
                    {
                        decision = args[i + 1];
                        i++;
                    }
                    else if ((args[i] == "--payload" || args[i] == "-p") && i + 1 < args.Length)
                    {
                        payloadJson = File.ReadAllText(args[i + 1]);
                        i++;
                    }
                }

                if (!engine.ResolveApproval(instanceId, stepId, decision, payloadJson, out var approvalError))
                {
                    Console.Error.WriteLine(approvalError ?? "Approval resolution failed.");
                    if (!string.IsNullOrWhiteSpace(approvalError) && approvalError.Contains("not found", StringComparison.OrdinalIgnoreCase))
                        return 2;
                    if (!string.IsNullOrWhiteSpace(approvalError) && approvalError.Contains("WF1006", StringComparison.Ordinal))
                        return 3;
                    return 4;
                }

                if (jsonMode) WriteJson(new { success = true, instanceId, stepId, decision });
                else Console.WriteLine($"Approval '{stepId}' resolved with decision '{decision}'.");
                return 0;
            }
            if (sub == "signal")
            {
                if (args.Length < 3)
                {
                    Console.Error.WriteLine("Usage: malda workflow signal <instanceId> <signalName> [--payload file.json]");
                    return 1;
                }

                var instanceId = args[1];
                var signalName = args[2];
                string? payloadJson = null;
                for (var i = 3; i < args.Length; i++)
                {
                    if ((args[i] == "--payload" || args[i] == "-p") && i + 1 < args.Length)
                    {
                        payloadJson = File.ReadAllText(args[i + 1]);
                        i++;
                    }
                }

                if (!engine.DeliverSignal(instanceId, signalName, payloadJson, out var signalError))
                {
                    Console.Error.WriteLine(signalError ?? "Signal delivery failed.");
                    if (!string.IsNullOrWhiteSpace(signalError) && signalError.Contains("not found", StringComparison.OrdinalIgnoreCase))
                        return 2;
                    if (!string.IsNullOrWhiteSpace(signalError) && signalError.Contains("WF1006", StringComparison.Ordinal))
                        return 3;
                    return 4;
                }

                if (jsonMode) WriteJson(new { success = true, instanceId, signalName });
                else Console.WriteLine($"Signal '{signalName}' delivered.");
                return 0;
            }
            if (sub == "dlq")
            {
                if (args.Length < 2)
                {
                    Console.Error.WriteLine("Usage: malda workflow dlq <list|requeue> [options]");
                    return 1;
                }

                var dlqSub = args[1].ToLowerInvariant();
                if (dlqSub == "list")
                {
                    var limit = 100;
                    var includeRequeued = true;
                    for (var i = 2; i < args.Length; i++)
                    {
                        if ((args[i] == "--limit" || args[i] == "-l") && i + 1 < args.Length)
                        {
                            int.TryParse(args[i + 1], out limit);
                            i++;
                        }
                        else if (args[i] == "--pending-only")
                        {
                            includeRequeued = false;
                        }
                    }
                    var deadLetters = engine.ListDeadLetters(limit, includeRequeued);
                    if (jsonMode)
                        WriteJson(deadLetters);
                    else
                        foreach (var dlq in deadLetters)
                            Console.WriteLine($"{dlq.Id}  wf={dlq.WorkflowInstanceId}  step={dlq.StepName}  reason={dlq.Reason}  requeued={dlq.RequeuedAtUtc ?? "-"}");
                    return 0;
                }

                if (dlqSub == "requeue")
                {
                    if (args.Length < 3)
                    {
                        Console.Error.WriteLine("Usage: malda workflow dlq requeue <deadLetterId>");
                        return 1;
                    }
                    var deadLetterId = args[2];
                    string? reason = null;
                    string? requestedBy = null;
                    string? correlationId = null;
                    for (var i = 3; i < args.Length; i++)
                    {
                        if (args[i] == "--reason" && i + 1 < args.Length)
                        {
                            reason = args[i + 1];
                            i++;
                        }
                        else if (args[i] == "--by" && i + 1 < args.Length)
                        {
                            requestedBy = args[i + 1];
                            i++;
                        }
                        else if (args[i] == "--correlation-id" && i + 1 < args.Length)
                        {
                            correlationId = args[i + 1];
                            i++;
                        }
                    }

                    var ok = engine.RequeueDeadLetter(deadLetterId, reason, requestedBy, correlationId, out var requeueError);
                    if (!ok)
                    {
                        Console.Error.WriteLine(requeueError ?? $"Dead letter not found or already requeued: {deadLetterId}");
                        if (!string.IsNullOrWhiteSpace(requeueError) && requeueError.Contains("WF1006", StringComparison.Ordinal))
                            return 3;
                        return 2;
                    }
                    if (jsonMode) WriteJson(new { success = true, deadLetterId, reason, requestedBy, correlationId });
                    else Console.WriteLine($"Requeued dead letter '{deadLetterId}'.");
                    return 0;
                }

                Console.Error.WriteLine($"Unknown dlq subcommand: {dlqSub}");
                return 1;
            }
            if (sub == "maintenance")
            {
                if (args.Length < 2 || args[1].ToLowerInvariant() != "run")
                {
                    Console.Error.WriteLine("Usage: malda workflow maintenance run [--operational-days ...] [--audit-days ...] [--compaction-days ...] [--batch ...] [--dry-run]");
                    return 1;
                }

                var runtimeOptions = engine.GetRuntimeOptions();
                var maintenance = new WorkflowMaintenanceOptions
                {
                    OperationalRetentionDays = runtimeOptions.OperationalRetentionDays,
                    AuditRetentionDays = runtimeOptions.AuditRetentionDays,
                    CompactionRetentionDays = runtimeOptions.CompactionRetentionDays,
                    CleanupBatchSize = runtimeOptions.CleanupBatchSize
                };

                for (var i = 2; i < args.Length; i++)
                {
                    if (args[i] == "--operational-days" && i + 1 < args.Length)
                    {
                        int.TryParse(args[i + 1], out var parsed);
                        maintenance.OperationalRetentionDays = parsed;
                        i++;
                    }
                    else if (args[i] == "--audit-days" && i + 1 < args.Length)
                    {
                        int.TryParse(args[i + 1], out var parsed);
                        maintenance.AuditRetentionDays = parsed;
                        i++;
                    }
                    else if (args[i] == "--compaction-days" && i + 1 < args.Length)
                    {
                        int.TryParse(args[i + 1], out var parsed);
                        maintenance.CompactionRetentionDays = parsed;
                        i++;
                    }
                    else if (args[i] == "--batch" && i + 1 < args.Length)
                    {
                        int.TryParse(args[i + 1], out var parsed);
                        maintenance.CleanupBatchSize = parsed;
                        i++;
                    }
                    else if (args[i] == "--dry-run")
                    {
                        maintenance.DryRun = true;
                    }
                }

                maintenance.ValidateOrThrow();
                var report = engine.RunMaintenanceJob(maintenance);
                if (jsonMode)
                {
                    WriteJson(report);
                }
                else
                {
                    Console.WriteLine($"maintenance_id: {report.MaintenanceId}");
                    Console.WriteLine($"dry_run: {report.DryRun}");
                    Console.WriteLine($"archived_instances: {report.ArchivedInstances}");
                    Console.WriteLine($"deleted_steps: {report.DeletedSteps}");
                    Console.WriteLine($"deleted_events: {report.DeletedEvents}");
                    Console.WriteLine($"deleted_dead_letters: {report.DeletedDeadLetters}");
                    Console.WriteLine($"compacted_steps: {report.CompactedSteps}");
                }
                return 0;
            }
            Console.Error.WriteLine($"Unknown workflow subcommand: {sub}");
            return 1;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"Error: {ex.Message}");
            return 1;
        }
    }
}
