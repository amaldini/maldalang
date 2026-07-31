// Copyright (c) 2026 Andrea Maldini
// SPDX-License-Identifier: MIT OR Apache-2.0

using MaldaLang.IDE.Components;
using MaldaLang.IDE.Services;
using MaldaLang.IDE;
using Microsoft.JSInterop;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();

// Register IDE services
builder.Services.AddSingleton<ILanguageService, LanguageService>();
builder.Services.AddSingleton<SyntaxHighlightingService>();
builder.Services.AddScoped<ExecutionService>(sp => 
{
    var jsRuntime = sp.GetRequiredService<IJSRuntime>();
    return new ExecutionService(jsRuntime);
});
builder.Services.AddSingleton<DebuggerService>();
builder.Services.AddSingleton<CompilerService>();
builder.Services.AddScoped<FileService>();
builder.Services.AddSingleton<MALDALanguageContextService>();
builder.Services.AddScoped<CodeDiffService>();
builder.Services.AddScoped<AIChatService>();

var app = builder.Build();

// Configure the HTTP request pipeline.
if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error", createScopeForErrors: true);
    // The default HSTS value is 30 days. You may want to change this for production scenarios, see https://aka.ms/aspnetcore-hsts.
    app.UseHsts();
}

app.UseHttpsRedirection();

app.UseStaticFiles();
app.UseAntiforgery();

app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode();

app.Run();