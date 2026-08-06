// Copyright (c) 2026 Andrea Maldini
// SPDX-License-Identifier: MIT OR Apache-2.0

namespace MaldaLang.IDE;

using System.Collections.Generic;
using System.Text;
using MaldaLang.IDE.Services;

public class MALDALanguageContextService
{
    public string GetLanguageSpecification()
    {
        var decoratorsSection = BuildDecoratorsSection();
        
        return @"MALDA (Multi Agent Language with Development Automation) is an AI-first programming language with the following key features:

DATA TYPES:
- Integer (32-bit), Float (64-bit), String, Boolean
- Dynamic typing (variables can hold any type)
- Arrays: [1, 2, 3], arr[0], arr.length, arr.append(item), arr.pop(), arr.shift(), arr.concat(otherArray)
- Objects: class instances, null support
- Dictionaries: dict {{ ""a"": 1, ""b"": 2 }}, d[""key""], d.get(), d.set(), d.keys(), d.values()
- Graphs: graph directed {{ nodes: [...], edges: [...] }}, g.addNode(), g.addEdge(), g.shortestPath(), g.bfs(), g.dfs(), etc.

VARIABLES:
- Declaration: var x = value;
- Assignment: x = value;
- Block-scoped with proper shadowing

CONTROL STRUCTURES:
- if (condition) { ... } else { ... }
- while (condition) { ... }
- for (var i = 0; i < 10; i = i + 1) { ... }
- break, continue
- Exception handling: try { ... } catch (error) { ... } finally { ... }
- Throw statement: throw expression;
- NOTE: MALDA does NOT support for-in loops on objects (no ""for (var key in obj)"" syntax)

FUNCTIONS:
- function name(params) { return value; }
- Supports recursion
- Parameters with proper scoping
- Lambda expressions: (params) => expression or (params) => { statements }
  * Expression body: var add = (a, b) => a + b;
  * Block body: var process = (x) => { var result = x * 2; return result + 1; };
  * Single parameter can omit parentheses: var square = x => x * x;
  * Empty parameter list: var getValue = () => 42;
  * Lambdas capture variables from outer scope (closures)
  * Can be assigned to variables, passed as arguments, or returned from functions
" + decoratorsSection + @"

CLASSES:
- class Name { var field; function Method() { ... } }
- Constructors: function ClassName(params) { this.field = value; }
- Inheritance: class Child extends Parent { super(); }
- Access modifiers: public, private
- Static members: static var count; (accessed via ClassName.count)

ARRAYS:
- Declaration: var arr = [1, 2, 3];
- Access: arr[0], arr[1] = value
- Property: arr.length (returns number of elements)
- Methods (called on array instances):
  * arr.append(item) - Adds item to end of array, returns the array
  * arr.pop() - Removes and returns last element
  * arr.popOrNull() - Removes and returns last element or null if array is empty
  * arr.shift() - Removes and returns first element
  * arr.shiftOrNull() - Removes and returns first element or null if array is empty
  * arr.concat(otherArray) - Returns new array with elements from both arrays
  * arr.get(index, fallback?) - Safe access with optional fallback, supports negative indices
  * arr.at(index) - Safe access returning null when out of bounds, supports negative indices
  * arr.map(fn) - Returns new array with transformed elements: arr.map(x => x * 2)
  * arr.filter(fn) - Returns new array with matching elements: arr.filter(x => x > 2)
  * arr.reduce(fn, initial?) - Reduces array to single value: arr.reduce((acc, x) => acc + x, 0)
  * arr.forEach(fn) - Iterates over elements (side effects): arr.forEach(x => print(x))
  * arr.find(fn) - Returns first matching element or null: arr.find(x => x > 1)
  * arr.findIndex(fn) - Returns index of first matching element or -1: arr.findIndex(x => x > 1)
  * arr.some(fn) - Returns true if any element matches: arr.some(x => x > 2)
  * arr.every(fn) - Returns true if all elements match: arr.every(x => x > 0)
  * arr.sort(comparator?) - Sorts array in place: arr.sort((a, b) => a - b)
  * arr.reverse() - Reverses array in place, returns the array
  * arr.slice(start, end?) - Returns new subarray: arr.slice(1, 3)
  * arr.indexOf(value) - Returns index of value or -1: arr.indexOf(2)
  * arr.includes(value) - Returns true if array contains value: arr.includes(2)
- Examples:
  var arr = [1, 2];
  arr.append(3);        // arr is now [1, 2, 3]
  var last = arr.pop(); // last = 3, arr is now [1, 2]
  var first = arr.shift(); // first = 1, arr is now [2]
  var a = [1, 2];
  var b = [3, 4];
  var c = a.concat(b);  // c is [1, 2, 3, 4], a and b unchanged
  var doubled = [1, 2, 3].map(x => x * 2);  // [2, 4, 6]
  var evens = [1, 2, 3, 4].filter(x => x % 2 == 0);  // [2, 4]
  var sum = [1, 2, 3].reduce((acc, x) => acc + x, 0);  // 6

BUILT-IN FUNCTIONS:
- Type conversion: int(), float(), string()
- Math: abs(), max(), min(), pow(), sqrt(), floor(), ceil(), round(), trunc(), sign(), exp(), log(), log10(), log2(), sin(), cos(), tan(), asin(), acos(), atan(), atan2(), hypot(), clamp(), degToRad(), radToDeg(), rsqrt(), randn(), argmax(), argmin(), logSumExp(), softmax(), crossEntropyFromLogits(), randomChoiceWeighted(), seed()
- String: length(), upper(), lower(), trim(), substring(text, startIndex, length), indexOf(), replace(), split(), normalizeText(), tokenize(), tokenOverlap(), similarity(), extractNumbers(), startsWith(), endsWith(), padStart(), padEnd(), repeat()
  * substring() third parameter is COUNT/LENGTH, not end index
  * Example: substring(""Hello"", 0, 3) returns ""Hel"" (start at 0, take 3 chars)
  * Example: substring(""Hello"", 1, 1) returns ""e"" (start at 1, take 1 char)
- I/O: print(), input(), sleep(milliseconds)
- JSON: parseJSON(), toJSON()
- File: readFile(), writeFile(), listDirectory(), replaceInFile()
- Git: gitStatus(), gitAdd(), gitCommit(), gitLog(), gitDiff(), gitBranch(), gitCheckout(), gitPush(), gitPull()
- Environment: getEnv(), getCommandLineArgs(), hasEnv(), getProgramDirectory()
- HTTP: httpGet(url, headers?, queryParams?), httpPost(url, body?, headers?, queryParams?), httpPut(url, body?, headers?, queryParams?), httpDelete(url, headers?, queryParams?), httpPatch(url, body?, headers?, queryParams?)
- Web UI: extractHTML(markdown), generateUI(description, cache?, agent?), ui.generate(description, agent?, cache?)

AI FEATURES (First-class language support):

LLM CLIENTS:
- LLMClient(apiUrl, apiKey, model) - OpenAI-compatible LLM client for any OpenAI-compatible API
- OpenRouterClient(model?) - Simplified OpenRouter client (automatically uses OPENROUTER_API_KEY env var)
- Conversation(client, systemPrompt) - Manages LLM conversations with automatic tool calling

AGENTS (Core AI Agent System):
Agents are autonomous AI assistants that can use tools, maintain conversation context, and execute tasks.

Basic Agent:
- Constructor: new Agent(name, role, instructions, client)
  * name (string): Agent identifier
  * role (string): Agent's role description
  * instructions (string): How the agent should behave
  * client (LLMClient/OpenRouterClient): LLM client to use
- Methods:
  * agent.think(prompt) → object: Process a prompt and return response (handles tool calls automatically)
    - Returns object with 'content' property: response.content
    - Automatically handles tool calls and feeds results back to LLM
  * agent.addTool(tool) or agent.addTool(toolName): Add a tool the agent can use
    - Accepts Tool instance or tool name string (from registry)
  * agent.addToolByName(toolName): Add a registered tool by name
  * agent.addAllTools(): Add all registered tools from the tool registry
  * agent.getAvailableTools() → array: Get list of available tool names from registry
  * agent.getConversation() → Conversation: Get the agent's conversation object
  * agent.reset(): Reset agent's conversation history (tools are preserved)
  * agent.addSubAgent(subAgent, toolDescription): Add another agent as a tool (multi-agent systems)
- Properties:
  * agent.name - Agent name (read-only)
  * agent.role - Agent role (read-only)
  * agent.instructions - Agent instructions (read-only)
- Example:
  var client = new OpenRouterClient(""mistralai/mistral-large"");
  var agent = new Agent(""CodeMaster"", ""senior software engineer"", ""You write clean, efficient code."", client);
  var response = agent.think(""Read example.txt and add error handling."");
  print(response.content);

Specialized Agents (pre-configured with tools):
- CodingAgent(name, role, instructions, client?, workingDirectory?) 
  * Automatically includes all file operation tools (readFile, writeFile, listDirectory, replaceInFile)
  * workingDirectory restricts file operations to that directory (default: current directory)
  * Example: var coder = new CodingAgent(""Dev"", ""developer"", ""Write clean code"", null, ""./src"");

- GitAgent(name, role, instructions, client?, workingDirectory?)
  * Automatically includes all git operation tools (gitStatus, gitAdd, gitCommit, gitLog, etc.)
  * workingDirectory restricts git operations to that repository (default: current directory)

- DevAgent(name, role, instructions, client?, workingDirectory?, includeSymbols?)
  * Automatically includes ALL development tools: file operations, git operations, run commands, compile, askUser
  * includeSymbols (boolean, optional): If true, includes getSymbols tool for code analysis

- HumanAgent(name, role, instructions, client?, workingDirectory?)
  * Automatically includes ask_user tool for human interaction
  * Useful for agents that need to ask questions or get approval

TOOLS:
- Tool(name, description, schema) - Create OpenAI function calling tool
  * name (string): Tool name for LLM to use
  * description (string): What the tool does
  * schema (object or JSON string): Parameter schema (auto-generated if omitted)
- Tool Registry: Functions decorated with @Tool are automatically registered
  * Use agent.addToolByName(""toolName"") to add registered tools
- Helper Functions: createReadFileTool(workingDir), createWriteFileTool(workingDir), 
  createGitStatusTool(workingDir), createGitAddTool(workingDir), etc.

ACTORS (Concurrent programming with message passing):
Actors are independent units with isolated state that communicate via asynchronous messages. Each actor processes messages sequentially.

- Declaration: actor Name { var field; function Name(args) { ... } on handlerName(args) { ... } }
  * Fields and constructor like classes; message handlers use the ""on"" keyword (e.g. on greet() { ... })
  * Optional default handler: on handle(msg) { ... } for unmatched messages
- Spawning: var ref = spawn ActorName(arg1, arg2);  // Creates actor, returns ActorReference
- Sending messages (non-blocking):
  * send ref.handlerName(arg1, arg2);  // Fire-and-forget
  * send ref(arg);  // Routes to handle(msg) if defined
  * send ref.handlerName(args) then (result) { ... };  // Callback when receiver calls reply(value)
  * send ref.handlerName(args) then (result) { ... } timeout ms catch (error) { ... };  // Timeout and error handler
- Inside a handler: reply(value) sends a reply back to the sender (only when message was send ... then (...))
- Self reference: self refers to the current actor (ActorReference); use self.stop() to stop the actor
- Stopping: ref.stop(); from outside, or self.stop(); inside a handler. Actor finishes current message then exits.
- Timing: After spawn, use sleep(100) or similar so the actor loop has started before sending messages; at program end use sleep(500) so messages are processed before exit.

Example:
  actor Counter { var n = 0; on inc() { n = n + 1; } on get() { reply(n); } }
  var c = spawn Counter();
  sleep(100);
  send c.inc(); send c.inc();
  send c.get() then (v) { print(v); };  // prints 2
  sleep(500);

OTHER AI FEATURES:
- RestServer(port, host?) - REST API server with decorator-based routing (@GET, @POST, etc.)
- RestClient(baseUrl?, timeout?) - REST web client for making HTTP requests
- HTMLCache(cacheDirectory?, maxSize?, expirationHours?) - Cache for generated HTML

IMPORTANT: MODULE SYSTEM:
- MALDA does NOT have a module system
- NO require(), import(), module.exports, or any module-related syntax
- All built-in classes (RestServer, LLMClient, OpenRouterClient, etc.) are available globally without imports
- Simply use: var server = new RestServer(3000); (no imports needed)

OPERATORS:
- Arithmetic: +, -, *, /, %
- Comparison: ==, !=, <, >, <=, >=
- Logical: and, or, not
- String concatenation: + (also works for string interpolation: $""text {var}"")
- String repetition: * (string * number or number * string, e.g., ""*"" * 20 produces 20 asterisks)

COMMENTS:
- Single-line: // comment
- Multi-line: /* comment */

STRING ESCAPES:" + 
            "\n- \\n (newline), \\t (tab), \\\" (quote), \\\\ (backslash)";
    }
    
    private string BuildDecoratorsSection()
    {
        var decorators = LanguageService.GetSupportedDecorators();
        var sb = new StringBuilder();
        
        sb.AppendLine("DECORATORS:");
        sb.AppendLine("Decorators are used to annotate functions and function parameters. They are placed before function declarations using @DecoratorName syntax.");
        sb.AppendLine();
        sb.AppendLine("CRITICAL RULES:");
        sb.AppendLine("- Parameter decorators (@PathParam, @QueryParam, @Body) can ONLY be used with HTTP endpoint decorators (@GET, @POST, @PUT, @DELETE, @PATCH, @OPTIONS)");
        sb.AppendLine("- Tool decorators (@Tool, @MCPTool) are for standalone functions and do NOT use parameter decorators");
        sb.AppendLine("- Never mix parameter decorators with tool decorators - they are mutually exclusive");
        sb.AppendLine();
        
        // Group decorators by category
        var httpDecorators = new List<string> { "GET", "POST", "PUT", "DELETE", "PATCH", "OPTIONS" };
        var toolDecorators = new List<string> { "Tool", "MCPTool" };
        var paramDecorators = new List<string> { "PathParam", "QueryParam", "Body" };
        
        sb.AppendLine("HTTP Endpoint Decorators (for REST API functions):");
        sb.AppendLine("These decorators mark functions as REST API endpoints. Parameter decorators can be used with these.");
        foreach (var name in httpDecorators)
        {
            if (decorators.TryGetValue(name, out var info))
            {
                sb.AppendLine($"- {info.Format}");
                sb.AppendLine($"  {info.Documentation}");
                if (info.ArgDescriptions.Count > 0)
                {
                    foreach (var argDesc in info.ArgDescriptions)
                    {
                        sb.AppendLine($"    • {argDesc}");
                    }
                }
                sb.AppendLine();
            }
        }
        
        sb.AppendLine("Tool Decorators (for LLM/MCP tool functions):");
        sb.AppendLine("These decorators register functions as tools for LLM/MCP. These are standalone functions and do NOT use parameter decorators.");
        foreach (var name in toolDecorators)
        {
            if (decorators.TryGetValue(name, out var info))
            {
                sb.AppendLine($"- {info.Format}");
                sb.AppendLine($"  {info.Documentation}");
                if (info.ArgDescriptions.Count > 0)
                {
                    foreach (var argDesc in info.ArgDescriptions)
                    {
                        sb.AppendLine($"    • {argDesc}");
                    }
                }
                sb.AppendLine();
            }
        }
        
        sb.AppendLine("Parameter Decorators (ONLY for function parameters in REST endpoints):");
        sb.AppendLine("These decorators can ONLY be used on parameters of functions decorated with HTTP endpoint decorators.");
        foreach (var name in paramDecorators)
        {
            if (decorators.TryGetValue(name, out var info))
            {
                sb.AppendLine($"- {info.Format}");
                sb.AppendLine($"  {info.Documentation}");
                if (info.ArgDescriptions.Count > 0)
                {
                    foreach (var argDesc in info.ArgDescriptions)
                    {
                        sb.AppendLine($"    • {argDesc}");
                    }
                }
                sb.AppendLine();
            }
        }
        
        sb.AppendLine("Examples:");
        sb.AppendLine("  // REST endpoint with path parameter (parameter decorator with HTTP decorator)");
        sb.AppendLine("  @GET(\"/api/users/{id}\")");
        sb.AppendLine("  function getUser(@PathParam(\"id\") userId) { ... }");
        sb.AppendLine();
        sb.AppendLine("  // REST endpoint with query parameter");
        sb.AppendLine("  @GET(\"/api/users\")");
        sb.AppendLine("  function getUsers(@QueryParam(\"limit\") limit) { ... }");
        sb.AppendLine();
        sb.AppendLine("  // REST endpoint with request body");
        sb.AppendLine("  @POST(\"/api/users\")");
        sb.AppendLine("  function createUser(@Body() userData) { ... }");
        sb.AppendLine();
        sb.AppendLine("  // Tool decorator (standalone function, NO parameter decorators)");
        sb.AppendLine("  @Tool(\"calculate_sum\", \"Adds two numbers\")");
        sb.AppendLine("  function add(a, b) { return a + b; }");
        sb.AppendLine();
        sb.AppendLine("  // MCPTool decorator (standalone function, NO parameter decorators)");
        sb.AppendLine("  @MCPTool(\"get_weather\", \"Get weather for a location\")");
        sb.AppendLine("  function getWeather(location) { ... }");
        sb.AppendLine();
        sb.AppendLine("  // WRONG: Do NOT use parameter decorators with tool decorators");
        sb.AppendLine("  // @MCPTool(\"get_weather\", \"...\")");
        sb.AppendLine("  // function getWeather(@QueryParam(\"location\") location) { ... }  // ERROR!");
        
        return sb.ToString();
    }
}
