// Copyright (c) 2026 Andrea Maldini
// SPDX-License-Identifier: MIT OR Apache-2.0

namespace MaldaLang.BuiltIns.ACP;

using System;
using System.Threading;
using System.Threading.Tasks;
using MaldaLang.Runtime.Actors;

/// <summary>
/// Actor handler for await/resume functionality.
/// This actor waits for resume messages to continue agent execution.
/// </summary>
public class AwaitHandler : IActor
{
    private string _runId;
    private string _agentId;
    private ACPAwaitRequest? _awaitRequest;
    private TaskCompletionSource<string>? _resumeSource;
    private readonly CancellationTokenSource _cancellation = new();
    
    public AwaitHandler(string runId, string agentId, ACPAwaitRequest awaitRequest)
    {
        _runId = runId;
        _agentId = agentId;
        _awaitRequest = awaitRequest;
        _resumeSource = new TaskCompletionSource<string>();
    }
    
    /// <summary>
    /// Wait for resume input. This will block until resume is called.
    /// </summary>
    public async Task<string> WaitForResumeAsync()
    {
        try
        {
            return await _resumeSource!.Task.WaitAsync(_cancellation.Token);
        }
        catch (OperationCanceledException)
        {
            throw new Exception("Await was cancelled");
        }
    }
    
    /// <summary>
    /// Resume with input. This is called by the resume endpoint.
    /// </summary>
    public void Resume(string input)
    {
        _resumeSource?.SetResult(input);
    }
    
    /// <summary>
    /// Cancel the await.
    /// </summary>
    public void Cancel()
    {
        _cancellation.Cancel();
        _resumeSource?.SetCanceled();
    }
    
    public string RunId => _runId;
    public string AgentId => _agentId;
    public ACPAwaitRequest? AwaitRequest => _awaitRequest;
}
