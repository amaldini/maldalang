// Copyright (c) 2026 Andrea Maldini
// SPDX-License-Identifier: MIT OR Apache-2.0

namespace MaldaLang.Channels;

using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;

/// <summary>
/// IAgentChannel implementation using Telegram Bot API (long polling and sendMessage).
/// </summary>
public class TelegramChannel : IAgentChannel
{
    private readonly string _botToken;
    private readonly HttpClient _httpClient;
    private readonly Channel<(string chatId, string text)> _messageQueue;
    private long _updateOffset;
    private Task? _pollingTask;
    private readonly CancellationTokenSource _pollingCts = new();
    private const string BaseUrl = "https://api.telegram.org/bot";

    public TelegramChannel(string botToken, HttpClient? httpClient = null)
    {
        _botToken = botToken ?? throw new ArgumentNullException(nameof(botToken));
        _httpClient = httpClient ?? new HttpClient();
        _messageQueue = System.Threading.Channels.Channel.CreateUnbounded<(string, string)>();
        _updateOffset = 0;
    }

    public async Task<(string text, string chatId)> ReceiveMessageAsync(CancellationToken cancel = default)
    {
        EnsurePollingStarted();
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancel, _pollingCts.Token);
        var item = await _messageQueue.Reader.ReadAsync(cts.Token).ConfigureAwait(false);
        return (item.text, item.chatId);
    }

    public async Task SendMessageAsync(string text, string chatId)
    {
        var url = $"{BaseUrl}{_botToken}/sendMessage";
        var body = new Dictionary<string, object>
        {
            ["chat_id"] = chatId,
            ["text"] = text ?? ""
        };
        var json = JsonSerializer.Serialize(body);
        var content = new StringContent(json, Encoding.UTF8, "application/json");
        var response = await _httpClient.PostAsync(url, content).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
        {
            var err = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
            System.Diagnostics.Debug.WriteLine($"Telegram sendMessage failed: {response.StatusCode} {err}");
        }
    }

    private void EnsurePollingStarted()
    {
        if (_pollingTask != null)
            return;
        lock (this)
        {
            if (_pollingTask != null)
                return;
            _pollingTask = Task.Run(() => PollUpdatesAsync(_pollingCts.Token));
        }
    }

    private async Task PollUpdatesAsync(CancellationToken cancel)
    {
        while (!cancel.IsCancellationRequested)
        {
            try
            {
                var url = $"{BaseUrl}{_botToken}/getUpdates?offset={_updateOffset}&timeout=30";
                var response = await _httpClient.GetAsync(url, cancel).ConfigureAwait(false);
                if (!response.IsSuccessStatusCode)
                {
                    await Task.Delay(5000, cancel).ConfigureAwait(false);
                    continue;
                }
                var json = await response.Content.ReadAsStringAsync(cancel).ConfigureAwait(false);
                using var doc = JsonDocument.Parse(json);
                var root = doc.RootElement;
                if (!root.TryGetProperty("ok", out var okEl) || !okEl.GetBoolean())
                    continue;
                if (!root.TryGetProperty("result", out var resultEl) || resultEl.ValueKind != JsonValueKind.Array)
                    continue;
                foreach (var update in resultEl.EnumerateArray())
                {
                    if (!update.TryGetProperty("update_id", out var updateIdEl))
                        continue;
                    var updateId = updateIdEl.GetInt64();
                    _updateOffset = updateId + 1;
                    if (!update.TryGetProperty("message", out var messageEl))
                        continue;
                    if (!messageEl.TryGetProperty("text", out var textEl))
                        continue;
                    var text = textEl.GetString();
                    if (string.IsNullOrEmpty(text))
                        continue;
                    if (!messageEl.TryGetProperty("chat", out var chatEl) || !chatEl.TryGetProperty("id", out var chatIdEl))
                        continue;
                    var chatId = chatIdEl.GetInt64().ToString();
                    _messageQueue.Writer.TryWrite((chatId, text));
                }
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Telegram poll error: {ex.Message}");
                try
                {
                    await Task.Delay(5000, cancel).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    break;
                }
            }
        }
    }

    public void Stop()
    {
        _pollingCts.Cancel();
    }
}
