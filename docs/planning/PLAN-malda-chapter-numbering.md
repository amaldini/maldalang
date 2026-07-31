# Plan: MALDA Script for Chapter Numbering

## Overview
Create a MALDA script (`update-chapter-numbers.malda`) that reads `chapters.json` and updates all HTML files with correct chapter and section numbers.

## Required MALDA Language Features

### Currently Available ✅
- **File I/O**: `readFile()`, `writeFile()`, `listDirectory()`, `hasFile()`
- **JSON**: `parseJSON()`, `toJSON()`
- **String Functions**: `substring()`, `indexOf()`, `length()`, `upper()`, `lower()`
- **Arrays**: Array operations, `append()`, iteration
- **Control Flow**: `if/else`, `for`, `while`, functions

### Missing Features ❌ (Need to Add)

#### 1. String Replace Function
**Function**: `replace(text, oldText, newText)`

**Purpose**: Replace all occurrences of a substring in a string

**Signature**: `replace(string text, string oldText, string newText) → string`

**Error Handling**:
- Throws if argument count != 3: `"replace() expects 3 arguments"`
- Throws if types invalid: `"replace() expects (string, string, string)"`

**Example**:
```malda
var text = "Hello world, world is great";
var result = replace(text, "world", "MALDA");
// Returns: "Hello MALDA, MALDA is great"
```

**Implementation**: Uses `String.Replace()` - replaces all occurrences (case-sensitive, ordinal comparison)

---

#### 2. Regex Support Functions

**Recommended**: Simple regex functions (matches MALDA's function-based style)

**Functions**:
- `regexMatch(text, pattern)` → `bool` - Check if pattern matches anywhere in text
- `regexReplace(text, pattern, replacement)` → `string` - Replace all matches (supports capture groups via `$1`, `$2`, etc.)
- `regexFind(text, pattern)` → `array` - Find all matches (optional, returns array of match objects with `text` and `groups`)

**Function Signatures**:
```malda
regexMatch(string text, string pattern) → bool
regexReplace(string text, string pattern, string replacement) → string
regexFind(string text, string pattern) → array  // Optional
```

**Error Handling**:
- `regexMatch()`: Throws `"regexMatch() expects 2 arguments"` or `"regexMatch() expects (string, string)"`
- `regexReplace()`: Throws `"regexReplace() expects 3 arguments"` or `"regexReplace() expects (string, string, string)"`
- Invalid regex pattern: Throws `"Invalid regex pattern: [error message]"`

**Examples**:
```malda
// Check if text matches pattern
if (regexMatch("<h2>18.1", "^<h2>\\d+\\.\\d+")) {
    print("Matches!");
}

// Replace all matches (use $1, $2 for capture groups)
var result = regexReplace(content, "<h2>(\\d+\\.\\d+\\s*)?([^<]+)</h2>", "<h2>22.$1 $2</h2>");

// Find all matches (optional - returns array of objects)
var matches = regexFind(content, "<h2>(\\d+\\.\\d+\\s*)?([^<]+)</h2>");
foreach (var match in matches) {
    print("Found: " + match.text);
    print("Groups: " + toJSON(match.groups));
}
```

**Regex Pattern Requirements**:
- Uses `.NET Regex` (standard regex syntax)
- Supports capture groups `(group)` - accessed via `$1`, `$2`, etc. in replacement
- Default: case-sensitive, single-line mode
- Escape backslashes in MALDA strings: `"\\d+"` for `\d+`

---

#### 3. Path Utilities (Optional but Helpful)
**Functions**:
- `getFileName(path)` → `string` - Get filename from path
- `getDirectoryName(path)` → `string` - Get directory from path (returns empty string if no directory)

**Function Signatures**:
```malda
getFileName(string path) → string
getDirectoryName(string path) → string
```

**Error Handling**:
- Throws `"getFileName() expects 1 argument"` or `"getFileName() expects a string argument"`
- Throws `"getDirectoryName() expects 1 argument"` or `"getDirectoryName() expects a string argument"`

**Examples**:
```malda
var filename = getFileName("ReferenceManual/09-functions.html");
// Returns: "09-functions.html"

var dir = getDirectoryName("ReferenceManual/09-functions.html");
// Returns: "ReferenceManual"

var dir2 = getDirectoryName("09-functions.html");
// Returns: "" (empty string for files in current directory)
```

**Note**: Can be worked around using `substring()` and `indexOf()` if needed, but these make code cleaner

---

## MALDA Script Structure

### Pseudo-code Outline

```malda
// Load chapters.json
var configContent = readFile("chapters.json");
var config = parseJSON(configContent);
var chapters = config.chapters;

// Helper functions
function getChapterNumber(chapters, filename) {
    // Calculate chapter number from position
}

function getChapterInfo(chapters, filename) {
    // Find chapter by filename
}

function stripNumberPrefix(text) {
    // Remove existing number prefix using regex
    return regexReplace(text, "^\\d+\\.\\d*\\.?\\d*\\s*", "");
}

// Main processing
var files = listDirectory(".");
var htmlFiles = [];
foreach (var file in files) {
    if (hasFile(file) && regexMatch(file, "\\.html$") && file != "index.html") {
        htmlFiles.append(file);
    }
}

foreach (var file in htmlFiles) {
    var filename = getFileName(file);
    var chapter = getChapterInfo(chapters, filename);
    
    if (chapter == null || chapter.isHome) {
        continue;
    }
    
    var chapterNum = getChapterNumber(chapters, filename);
    var content = readFile(file);
    var modified = false;
    
    // Update title
    content = regexReplace(content, "<title>(\\d+\\.\\s*)?([^<]+)</title>", 
        "<title>" + chapterNum + ". " + chapter.title + " - MALDA Reference Manual</title>");
    
    // Update breadcrumbs
    // Update h1
    // Update h2 sections
    // Update h3 subsections
    // Update cross-references
    // Update nav footer
    
    if (modified) {
        writeFile(file, content);
        print("Updated " + filename + " (Chapter " + chapterNum + ")");
    }
}
```

---

## Implementation Priority

### Phase 1: Essential Features
1. **String `replace()` function** - Simple substring replacement
   - Can work around regex for simple cases
   - Essential for basic string manipulation

### Phase 2: Regex Support
2. **`regexReplace()` function** - Pattern-based replacement
   - Needed for complex HTML pattern matching
   - Most critical for the script

3. **`regexMatch()` function** - Pattern matching
   - Useful for validation and conditional logic

### Phase 3: Advanced Regex (Optional)
4. **`regexFind()` function** - Extract matches with groups
   - Useful for extracting capture groups
   - Can be worked around with multiple `regexReplace()` calls

### Phase 4: Path Utilities (Optional)
5. **Path utility functions** - File path manipulation
   - Can be worked around with string manipulation
   - Nice to have for cleaner code

---

## Alternative: Workarounds Without Regex

If regex is too complex to add immediately, we can use:

1. **String `replace()` + `indexOf()`** - For simple replacements
2. **Multiple `replaceInFile()` calls** - For file-based replacements
3. **Manual string parsing** - Using `substring()` and `indexOf()` to find patterns

However, this would be much more verbose and error-prone. Regex is strongly recommended.

---

## Next Steps

1. **Add string `replace()` function** to `BuiltInFunctions.cs`
2. **Add regex functions** (`regexMatch()`, `regexReplace()`, optionally `regexFind()`)
3. **Test with simple examples**
4. **Write the MALDA script** (`update-chapter-numbers.malda`)
5. **Test the script** on the reference manual

---

## Implementation Style Guidelines

### Code Style (Matching MALDA Conventions)

1. **Function Naming**: Lowercase, no underscores, descriptive
   - ✅ `replace()`, `regexMatch()`, `regexReplace()`
   - ❌ `Replace()`, `regex_match()`, `RegexMatch()`

2. **Error Messages**: Clear, consistent format
   - Argument count: `"function() expects X arguments"`
   - Type errors: `"function() expects (type1, type2, ...)"`
   - Invalid input: `"Invalid [description]: [error details]"`

3. **Return Values**: Use appropriate `RuntimeValue` constructors
   - Strings: `RuntimeValue.String(result)`
   - Booleans: `RuntimeValue.Boolean(result)`
   - Integers: `RuntimeValue.Integer(result)`
   - Arrays: `RuntimeValue.Array(matchList)`
   - Null: `RuntimeValue.Null()`

4. **Type Checking Pattern**:
   ```csharp
   if (args.Count != X) throw new Exception("function() expects X arguments");
   if (args[0].Type != ValueType.String) 
       throw new Exception("function() expects a string argument");
   ```

5. **Try-Catch for External Operations**:
   - Regex compilation: catch `ArgumentException` and rethrow with clear message
   - File operations: already handled in existing functions

6. **Method Naming**: `BuiltIn[FunctionName]()` where FunctionName matches the MALDA function name
   - `replace()` → `BuiltInReplace()`
   - `regexMatch()` → `BuiltInRegexMatch()`

## Files to Modify

### Add Built-in Functions
- `MaldaLang/BuiltIns/BuiltInFunctions.cs`
  - Add `BuiltInReplace()` method
    - Check `args.Count != 3`, throw `"replace() expects 3 arguments"`
    - Check all args are strings, throw `"replace() expects (string, string, string)"`
    - Use `String.Replace()` with `StringComparison.Ordinal`
    - Return `RuntimeValue.String(result)`
  
  - Add `BuiltInRegexMatch()` method
    - Check `args.Count != 2`, throw `"regexMatch() expects 2 arguments"`
    - Check both args are strings, throw `"regexMatch() expects (string, string)"`
    - Try-catch regex compilation, throw `"Invalid regex pattern: [error]"`
    - Use `Regex.IsMatch()`, return `RuntimeValue.Boolean(result)`
  
  - Add `BuiltInRegexReplace()` method
    - Check `args.Count != 3`, throw `"regexReplace() expects 3 arguments"`
    - Check all args are strings, throw `"regexReplace() expects (string, string, string)"`
    - Try-catch regex compilation, throw `"Invalid regex pattern: [error]"`
    - Use `Regex.Replace()`, return `RuntimeValue.String(result)`
  
  - Add `BuiltInRegexFind()` method (optional)
    - Check `args.Count != 2`, throw `"regexFind() expects 2 arguments"`
    - Check both args are strings, throw `"regexFind() expects (string, string)"`
    - Try-catch regex compilation, throw `"Invalid regex pattern: [error]"`
    - Use `Regex.Matches()`, build array of match objects with `text` and `groups` properties
    - Return `RuntimeValue.Array(matches)`
  
  - Add `BuiltInGetFileName()` method (optional)
    - Check `args.Count != 1`, throw `"getFileName() expects 1 argument"`
    - Check arg is string, throw `"getFileName() expects a string argument"`
    - Use `Path.GetFileName()`, return `RuntimeValue.String(result)`
  
  - Add `BuiltInGetDirectoryName()` method (optional)
    - Check `args.Count != 1`, throw `"getDirectoryName() expects 1 argument"`
    - Check arg is string, throw `"getDirectoryName() expects a string argument"`
    - Use `Path.GetDirectoryName()`, return `RuntimeValue.String(result ?? "")`
  
  - Register all in `CallBuiltIn()` switch statement:
    ```csharp
    "replace" => BuiltInReplace(args),
    "regexMatch" => BuiltInRegexMatch(args),
    "regexReplace" => BuiltInRegexReplace(args),
    "regexFind" => BuiltInRegexFind(args),  // Optional
    "getFileName" => BuiltInGetFileName(args),  // Optional
    "getDirectoryName" => BuiltInGetDirectoryName(args),  // Optional
    ```

### Update Documentation
- `ReferenceManual/11-built-in-functions.html`
  - Add to "11.3 String Functions" section:
    ```html
    <h2>11.3 String Functions</h2>
    <pre><code>var len = length("Hello");      // String length
    var upper = upper("hello");     // Convert to uppercase
    var lower = lower("HELLO");     // Convert to lowercase
    var substr = substring("Hello", 0, 3);  // Extract substring
    var pos = indexOf("hello world", "world");  // Find position (returns 6, 0-indexed)
    var replaced = replace("Hello world", "world", "MALDA");  // Replace all occurrences
    var matches = regexMatch("test123", "\\d+");  // Check if pattern matches (returns true)
    var result = regexReplace("abc123", "\\d+", "456");  // Replace matches (returns "abc456")</code></pre>
    ```
  - Add new "11.3.1 Regex Functions" subsection (or integrate into 11.3)
  - Follow existing documentation style: concise examples with comments showing return values

### Create Script
- `ReferenceManual/update-chapter-numbers.malda`
  - The actual MALDA script
