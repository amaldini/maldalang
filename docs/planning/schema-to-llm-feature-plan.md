# Schema-to-LLM Feature Plan: Pass JSON Schema to Backend for Typed Prompts

## 1. Overview

**Goal:** When a MALDA prompt declares a return type (`-> Type`), pass the resolved JSON schema to the LLM backend (e.g. OpenAI `response_format`) so the model returns schema-conforming JSON on the first attempt, reducing retries and improving reliability.

**Current behavior:** The schema is used only for post-response validation. The LLM receives no schema; on validation failure, a repair instruction is appended and the prompt is retried (up to 3 times).

**Target behavior:** The schema is sent with the chat request when supported by the backend. Validation still runs (defense in depth); retries remain as fallback for unsupported backends or edge cases.

**Reference:** `structured_prompt_output_spec.md` §6.3 (Optional: Pass schema to the LLM)

---

## 2. API Format (OpenAI / OpenRouter)

OpenAI Structured Outputs use the `response_format` parameter:

```json
{
  "model": "...",
  "messages": [...],
  "response_format": {
    "type": "json_schema",
    "json_schema": {
      "name": "typed_prompt_response",
      "strict": true,
      "schema": {
        "type": "object",
        "properties": { ... },
        "required": [ ... ]
      }
    }
  }
}
```

- **Supported models:** `gpt-4o-mini`, `gpt-4o-mini-2024-07-18`, `gpt-4o-2024-08-06` and later.
- **Older models:** Fall back to current behavior (no schema, rely on retry/repair).
- **OpenRouter:** Typically forwards OpenAI-compatible parameters; verify during implementation.

---

## 3. Data Flow

### 3.1 Current Flow (Interpreter)

```
PromptValue.CallAsync (has returnType, interpreter)
  → Resolves schema via TypedPromptSchemaResolver.TryResolve
  → Creates PromptInstance (no schema today)
  → agent.Think(promptInstanceValue)
  → Conversation.AddUserMessage + Send
  → Client.Chat(messages, tools)  // no response_format
  → Response → extract JSON → validate → retry or return
```

### 3.2 Target Flow

```
PromptValue.CallAsync (has returnType, interpreter)
  → Resolves schema via TypedPromptSchemaResolver.TryResolve
  → Creates PromptInstance WITH ResponseFormatSchema (RuntimeValue)
  → agent.Think(promptInstanceValue)
  → Conversation.Send reads ResponseFormatSchema from PromptInstance (if present)
  → Client.Chat(messages, tools, responseFormat)
  → Backend sends schema in request when supported
  → Response → extract JSON → validate → retry or return
```

### 3.3 Transpiler Path

The transpiler already builds the schema at compile time for validation. The generated `*__ExecuteAsync` creates a PromptInstance and calls the agent. We need to:

1. Add optional `responseFormat` parameter to PromptInstance constructor.
2. When transpiling typed prompts, emit code that passes the pre-built schema into PromptInstance.
3. The same Agent/Conversation/Client path handles it.

---

## 4. Implementation Tasks

### 4.1 Extend PromptInstance

**File:** `MaldaLang/BuiltIns/PromptInstance.cs`

- Add optional constructor parameter: `RuntimeValue? responseFormatSchema = null`
- Add property: `public RuntimeValue? ResponseFormatSchema { get; }`
- When non-null, this holds the JSON-schema-like structure (object with `type`, `properties`, `required`) that `TypedPromptSchemaResolver` produces.

**Schema wrapping:** The resolver produces `{ type, properties, required }`. OpenAI expects this nested under `response_format.json_schema.schema`. Add a helper (e.g. in `TypedPromptValidator` or new `ResponseFormatBuilder`) to wrap it:

```csharp
// Pseudo: build OpenAI response_format structure
{
  "type": "json_schema",
  "json_schema": {
    "name": "typed_prompt_response",
    "strict": true,
    "schema": <resolved_schema>
  }
}
```

### 4.2 Thread Schema Through PromptValue.CallAsync

**File:** `MaldaLang/Interpreter/PromptValue.cs`

- When `Declaration.ReturnType` is present:
  1. Call `TypedPromptSchemaResolver.TryResolve(returnType, interpreter, out schema, out error)` (already done for validation).
  2. Build the `response_format` wrapper (OpenAI format).
  3. Create PromptInstance with the new `responseFormatSchema` parameter.
  4. Pass this PromptInstance to `agent.Think`.

### 4.3 Extend Agent.Think to Pass Schema to Conversation

**File:** `MaldaLang/BuiltIns/Agent.cs`

- When `promptOrInstance` is a `PromptInstance`:
  - Check for `promptInst.ResponseFormatSchema` (or equivalent accessor).
  - If present, pass it to the conversation before `Send()`.

**Option A:** Conversation has a temporary "current response format" that Send() uses.
**Option B:** Conversation.Send() accepts an optional `responseFormat` parameter.

Recommend **Option B** for clarity: `Conversation.Send(responseFormat?: RuntimeValue)`.

### 4.4 Extend Conversation.Send

**File:** `MaldaLang/BuiltIns/Conversation.cs`

- Add optional parameter: `RuntimeValue? responseFormat = null`
- When calling `_client.Chat(...)`, `_llamaClient.Chat(...)`, or `_bridgeClient.Chat(...)`, pass `responseFormat` as a third argument.

### 4.5 Extend Chat Signatures

**Files:**
- `MaldaLang/BuiltIns/LLMClient.cs`
- `MaldaLang/BuiltIns/LlamaCppClient.cs`
- `MaldaLang/BuiltIns/LLMClientBridge/IBackendAdapter.cs`
- `MaldaLang/BuiltIns/LLMClientBridge/LLMClientBridgeInstance.cs`
- `MaldaLang/BuiltIns/LLMClientBridge/BackendAdapters/*.cs`

**Change:** `Chat(RuntimeValue messages, RuntimeValue? tools)` → `Chat(RuntimeValue messages, RuntimeValue? tools, RuntimeValue? responseFormat = null)`.

### 4.6 LLMClient: Add response_format to Request Body

**File:** `MaldaLang/BuiltIns/LLMClient.cs`

- In the request body construction (around line 177):
  - If `responseFormat != null`, serialize it to a JSON object and add `["response_format"] = <object>` to `requestBody`.
- Convert `RuntimeValue` (JsonObject) to `Dictionary<string, object?>` for `JsonSerializer.Serialize` (reuse existing `JsonToObject`-style logic if available).

### 4.7 LlamaCppClient: Backend Support

**File:** `MaldaLang/BuiltIns/LlamaCppClient.cs`

- **Research:** Does LlamaCpp/llama.cpp server support `response_format` or equivalent?
- **Fallback:** If not supported, ignore `responseFormat` and keep current behavior (retry/repair). No error.

### 4.8 LLMClientBridge Adapters

**Files:** `LocalServerAdapter.cs`, `OpenRouterAdapter.cs`, `RemoteApiAdapter.cs`, `DirectLocalAdapter.cs`

- Pass `responseFormat` through to the underlying client's `Chat` method.
- Adapters that call `_client.Chat(messages, tools)` → `_client.Chat(messages, tools, responseFormat)`.

### 4.9 Transpiler: Emit Schema into Generated Code

**File:** `MaldaLang.Compiler/CSharpTranspiler.cs`

- In `TranspilePrompt`, when `promptDecl.ReturnType` is present:
  1. Resolve schema via `TypedPromptSchemaResolver.BuildSchemaFromClassDeclaration` (for custom classes) or use a static mapping for built-ins (`Plan`, primitives).
  2. Serialize schema to a JSON string or C# literal.
  3. Emit a static field or local that holds the `response_format` structure.
  4. When creating `PromptInstance`, pass this schema: `new PromptInstance(..., responseFormatSchema: schemaValue)`.

- Reuse `BuiltInFunctions.SerializeToJson` or equivalent to produce a JSON string, then parse at runtime into `RuntimeValue` for the PromptInstance.

### 4.10 Tools + Response Format Interaction

**Consideration:** When a prompt has `tools` in its body, the model may emit tool calls instead of content. Structured output expects content.

**Decision (v1):**
- Only pass `response_format` when `tools` is null or empty.
- If tools are requested, do not pass response_format (keep current retry/repair behavior).
- Document this in the reference manual.

### 4.11 Backend Support Detection (Optional)

**Idea:** Some backends may not support `response_format`. Options:
- **A)** Always send it; unsupported backends may ignore or error (handle error gracefully, fall back to no schema).
- **B)** Maintain a list of known-supporting backends (e.g. OpenAI, OpenRouter) and only send when using those.
- **C)** Add a MALDA-level opt-out: `prompt foo() -> Bar { ..., responseFormat: false }` to disable schema passthrough.

**Recommendation for v1:** Option A – send when we have a schema; if the backend errors, catch and retry without schema (or surface a clear error). Keep implementation simple.

---

## 5. Schema Format Compatibility

**Current schema (TypedPromptSchemaResolver):** JSON Schema-like `{ type, properties?, required? }` for objects.

**OpenAI requirements:** The schema must conform to [OpenAI's supported schema subset](https://platform.openai.com/docs/guides/structured-outputs#supported-schemas). Our schemas use basic types (`string`, `integer`, `number`, `boolean`, `array`, `object`) which are typically supported.

**Action:** During implementation, validate that our schema output matches OpenAI's expectations. If not, add a transformation step.

---

## 6. Testing

### 6.1 Unit Tests

- **TypedPromptValidator / ResponseFormatBuilder:** Given a resolved schema, produce correct OpenAI `response_format` structure.
- **PromptInstance:** When constructed with `responseFormatSchema`, property returns it.

### 6.2 Integration Tests (Interpreter)

- Mock or stub LLM client to capture request body.
- Invoke `await promptWithReturnType(args)`.
- Assert request body contains `response_format` with expected schema.
- Use a fake client that returns valid JSON; assert no retries when schema is passed.

### 6.3 Integration Tests (Transpiler)

- Transpile a program with `prompt foo() -> Plan { ... }`.
- Run the executable with a mock/fake LLM or record HTTP.
- Assert the outgoing request includes `response_format`.

### 6.4 Backend Tests (Manual / Optional)

- Run against real OpenAI API with a typed prompt.
- Verify first-pass success rate improves vs. no schema.

---

## 7. Documentation Updates

- **Reference Manual (09-functions.html):** Add a note that when `-> Type` is present and the backend supports it, the schema is sent to improve first-pass success. Mention that tools and response_format are mutually exclusive for v1.
- **structured_prompt_output_spec.md:** Update §6.3 to reference this plan and mark as "In Progress" or "Implemented" when done.

---

## 8. Acceptance Criteria

- [x] PromptInstance accepts optional `responseFormatSchema`.
- [x] PromptValue.CallAsync passes schema into PromptInstance for typed prompts.
- [x] Agent.Think passes schema to Conversation.Send when present.
- [x] Conversation.Send accepts and forwards optional `responseFormat`.
- [x] LLMClient.Chat adds `response_format` to request body when provided.
- [x] LlamaCppClient either supports or gracefully ignores `responseFormat`.
- [x] LLMClientBridge adapters pass `responseFormat` through.
- [x] Transpiler emits schema for typed prompts and passes it to PromptInstance.
- [x] Phase 6 `schema` declarations resolve as typed-prompt return types (interpreter + transpiler).
- [x] When tools are present, `response_format` is not sent (v1).
- [x] Tests cover interpreter and transpiler paths (`TypedPromptValidatorTests`, `TranspiledTypedPromptTests`, `SchemaToLlmTests`).
- [x] Reference manual updated.

---

## 9. File Summary

| File | Changes |
|------|---------|
| `PromptInstance.cs` | Add `ResponseFormatSchema` property and constructor param |
| `PromptValue.cs` | Resolve schema, build response_format, pass to PromptInstance |
| `Agent.cs` | Pass `ResponseFormatSchema` from PromptInstance to Conversation.Send |
| `Conversation.cs` | Add `responseFormat` param to Send, forward to clients |
| `LLMClient.cs` | Add `responseFormat` to Chat, include in request body |
| `LlamaCppClient.cs` | Add param, support or ignore |
| `IBackendAdapter.cs` | Add `responseFormat` to Chat signature |
| `LLMClientBridgeInstance.cs` | Forward `responseFormat` |
| `*Adapter.cs` (4 files) | Forward `responseFormat` to underlying client |
| `CSharpTranspiler.cs` | Emit schema for typed prompts, pass to PromptInstance |
| New helper (optional) | `ResponseFormatBuilder` to wrap schema for OpenAI |
| `structured_prompt_output_spec.md` | Update §6.3 status |
| `09-functions.html` | Document schema passthrough |

---

## 10. Risks and Mitigations

| Risk | Mitigation |
|------|------------|
| Backend rejects `response_format` | Catch error, fall back to retry without schema, or document unsupported backends |
| Schema format mismatch | Validate against OpenAI docs; add transformation if needed |
| Tools + response_format conflict | v1: don't send schema when tools present |
| Transpiler schema bloat | Schema is small; acceptable for v1 |
