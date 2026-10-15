// Copyright (c) 2026 Andrea Maldini
// SPDX-License-Identifier: MIT OR Apache-2.0

namespace MaldaLang.BuiltIns;

using System;
using System.Collections.Concurrent;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Reflection;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Text;
using System.Security.Cryptography;
using System.Globalization;
using MaldaLang;
using MaldaLang.Parser;
using MaldaLang.Runtime;
using MaldaLang.Parser.AST.Declarations;
using MaldaLang.Interpreter;
using MaldaLang.Runtime.Profiling;
using MaldaLang.Runtime.UI;
using MaldaLang.Runtime.Tracing;
using MaldaLang.Runtime.Jobs;
using MaldaLang.Runtime.Workflows;
using MaldaLang.IDE;
using MaldaLang.Testing;
using ValueType = MaldaLang.Interpreter.ValueType;
using Spectre.Console;
using Spectre.Console.Rendering;
using Markdig;
using UglyToad.PdfPig;
using UglyToad.PdfPig.DocumentLayoutAnalysis.TextExtractor;
using DocumentFormat.OpenXml.Packaging;

public static partial class BuiltInFunctions
{
    private static RuntimeValue BuiltInEnqueueJob(List<RuntimeValue> args, Interpreter? interpreter)
    {
        BuiltInArity.Require("enqueueJob", args, 2, 3, "queue, payload, options?");
        if (args[0].Type != ValueType.String)
            throw new Exception("enqueueJob() queue must be a string");

        var payloadJson = args[1].Type == ValueType.Null
            ? "{}"
            : CallBuiltIn("toJSON", new List<RuntimeValue> { args[1] }, interpreter).AsString();

        DateTimeOffset? runAt = null;
        var maxAttempts = 3;
        string? correlationId = null;
        if (args.Count == 3)
        {
            if (args[2].Type != ValueType.Object)
                throw new Exception("enqueueJob() options must be an object");

            var opts = args[2].AsObject();
            var runAtValue = opts.Get("runAt", null);
            if (runAtValue.Type == ValueType.String &&
                DateTimeOffset.TryParse(runAtValue.AsString(), out var parsedRunAt))
            {
                runAt = parsedRunAt;
            }

            var maxAttemptsValue = opts.Get("maxAttempts", null);
            if (maxAttemptsValue.Type == ValueType.Integer)
            {
                maxAttempts = maxAttemptsValue.AsInteger();
            }

            var correlationValue = opts.Get("correlationId", null);
            if (correlationValue.Type == ValueType.String)
            {
                correlationId = correlationValue.AsString();
            }
        }

        var id = JobStore.Default.Enqueue(args[0].AsString(), payloadJson, runAt, maxAttempts, correlationId);
        return RuntimeValue.String(id);
    }

    private static RuntimeValue BuiltInClaimJob(List<RuntimeValue> args)
    {
        BuiltInArity.Require("claimJob", args, 1, 2, "queue, workerId?");
        if (args[0].Type != ValueType.String)
            throw new Exception("claimJob() queue must be a string");

        string? workerId = null;
        if (args.Count == 2)
        {
            if (args[1].Type != ValueType.String)
                throw new Exception("claimJob() workerId must be a string");
            workerId = args[1].AsString();
        }

        var job = JobStore.Default.Claim(args[0].AsString(), workerId);
        return job == null ? RuntimeValue.Null() : JobRecordToRuntimeValue(job);
    }

    private static RuntimeValue BuiltInCompleteJob(List<RuntimeValue> args, Interpreter? interpreter)
    {
        BuiltInArity.Require("completeJob", args, 1, 2, "jobId, result?");
        if (args[0].Type != ValueType.String)
            throw new Exception("completeJob() jobId must be a string");

        string? resultJson = null;
        if (args.Count == 2 && args[1].Type != ValueType.Null)
        {
            resultJson = CallBuiltIn("toJSON", new List<RuntimeValue> { args[1] }, interpreter).AsString();
        }

        JobStore.Default.Complete(args[0].AsString(), resultJson);
        return RuntimeValue.Boolean(true);
    }

    private static RuntimeValue BuiltInFailJob(List<RuntimeValue> args, Interpreter? interpreter)
    {
        BuiltInArity.Require("failJob", args, 1, 3, "jobId, error?, retry?");
        if (args[0].Type != ValueType.String)
            throw new Exception("failJob() jobId must be a string");

        string? errorJson = null;
        if (args.Count >= 2 && args[1].Type != ValueType.Null)
        {
            errorJson = args[1].Type == ValueType.String
                ? CallBuiltIn("toJSON", new List<RuntimeValue> { args[1] }, interpreter).AsString()
                : CallBuiltIn("toJSON", new List<RuntimeValue> { args[1] }, interpreter).AsString();
        }

        var retry = true;
        if (args.Count == 3)
        {
            if (args[2].Type != ValueType.Boolean)
                throw new Exception("failJob() retry must be a boolean");
            retry = args[2].AsBoolean();
        }

        JobStore.Default.Fail(args[0].AsString(), errorJson, retry);
        return RuntimeValue.Boolean(true);
    }

    private static RuntimeValue BuiltInGetJob(List<RuntimeValue> args)
    {
        BuiltInArity.Require("getJob", args, 1, 1, "jobId");
        if (args[0].Type != ValueType.String)
            throw new Exception("getJob() jobId must be a string");

        var job = JobStore.Default.Get(args[0].AsString());
        return job == null ? RuntimeValue.Null() : JobRecordToRuntimeValue(job);
    }

    private static RuntimeValue BuiltInListJobs(List<RuntimeValue> args)
    {
        BuiltInArity.Require("listJobs", args, 0, 3, "queue?, status?, limit?");
        string? queue = null;
        string? status = null;
        var limit = 50;

        if (args.Count >= 1 && args[0].Type != ValueType.Null)
        {
            if (args[0].Type != ValueType.String)
                throw new Exception("listJobs() queue must be a string");
            queue = args[0].AsString();
        }

        if (args.Count >= 2 && args[1].Type != ValueType.Null)
        {
            if (args[1].Type != ValueType.String)
                throw new Exception("listJobs() status must be a string");
            status = args[1].AsString();
        }

        if (args.Count == 3)
        {
            if (args[2].Type != ValueType.Integer)
                throw new Exception("listJobs() limit must be an integer");
            limit = args[2].AsInteger();
        }

        var jobs = JobStore.Default.List(queue, status, limit);
        return RuntimeValue.Array(jobs.Select(JobRecordToRuntimeValue).ToList());
    }

    private static RuntimeValue JobRecordToRuntimeValue(JobRecord job)
    {
        var obj = new JsonObject();
        obj.Set("id", RuntimeValue.String(job.Id));
        obj.Set("queue", RuntimeValue.String(job.Queue));
        obj.Set("status", RuntimeValue.String(job.Status));
        obj.Set("attempts", RuntimeValue.Integer(job.Attempts));
        obj.Set("maxAttempts", RuntimeValue.Integer(job.MaxAttempts));
        obj.Set("runAt", RuntimeValue.String(job.RunAt.ToString("O")));
        obj.Set("createdAt", RuntimeValue.String(job.CreatedAt.ToString("O")));
        obj.Set("updatedAt", RuntimeValue.String(job.UpdatedAt.ToString("O")));
        if (job.LockedBy != null)
            obj.Set("lockedBy", RuntimeValue.String(job.LockedBy));
        if (job.CorrelationId != null)
            obj.Set("correlationId", RuntimeValue.String(job.CorrelationId));

        try
        {
            obj.Set("payload", CallBuiltIn("parseJSON", new List<RuntimeValue> { RuntimeValue.String(job.PayloadJson) }, null));
        }
        catch
        {
            obj.Set("payload", RuntimeValue.String(job.PayloadJson));
        }

        if (!string.IsNullOrEmpty(job.ResultJson))
        {
            try
            {
                obj.Set("result", CallBuiltIn("parseJSON", new List<RuntimeValue> { RuntimeValue.String(job.ResultJson) }, null));
            }
            catch
            {
                obj.Set("result", RuntimeValue.String(job.ResultJson));
            }
        }

        if (!string.IsNullOrEmpty(job.ErrorJson))
        {
            try
            {
                obj.Set("error", CallBuiltIn("parseJSON", new List<RuntimeValue> { RuntimeValue.String(job.ErrorJson) }, null));
            }
            catch
            {
                obj.Set("error", RuntimeValue.String(job.ErrorJson));
            }
        }

        return RuntimeValue.Object(obj);
    }
}
