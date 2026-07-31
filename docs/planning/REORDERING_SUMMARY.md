# Chapter Reordering Summary

## What Was Done

### 1. Enhanced Script Functionality ✅

The `update-chapter-numbers.malda` script now handles **inline links** in addition to the existing functionality:

**New Feature**: Updates inline "Section X:" references in main content
- Example: `<a href="13-agent-orchestration.html">Section 12: Agent Orchestration</a>` 
- Will be updated to: `<a href="13-agent-orchestration.html">Section 15: Agent Orchestration</a>` (if chapter moved to position 15)

**Existing Features** (already working):
- ✅ Page titles
- ✅ Breadcrumbs
- ✅ H1 headings
- ✅ H2/H3 section headings
- ✅ See Also section links
- ✅ Navigation footer links (Previous/Next)

### 2. Proposed Better Learning Order ✅

A more natural learning progression has been proposed in `chapters.json`:

**New Order**:
1. Introduction
2. Lexical Structure
3. Data Types
4. Variables
5. **Expressions** (moved earlier - needed for control structures)
6. **Control Structures** (moved after expressions)
7. **Functions** (moved before arrays - more fundamental)
8. **Arrays** (moved after functions)
9. Classes & Objects
10. **Input/Output** (moved earlier - essential for practical use)
11. Built-in Functions
12. **Graphs** (moved after built-in functions)
13. **VectorDB** (moved after graphs)
14. Actors
15. Agent Orchestration
16. Database Support
17. Web UI Generation
18. REST API Server
19. REST Web Client
20. MCP Server
21. .NET Interop
22. Device Integration
23. **Examples** (moved to reference section)
24. **Grammar** (moved to reference section)
25. **Appendix** (moved to end)

### 3. Updated chapters.json ✅

The `chapters.json` file has been updated with the new order. File names remain unchanged (e.g., `07-expressions.html` stays as `07-expressions.html`), but the order in `chapters.json` determines the chapter numbering.

## How to Apply

1. **Run the script**:
   ```bash
   malda update-chapter-numbers.malda
   ```

2. **The script will**:
   - Read `chapters.json` to get the new order
   - Update all chapter numbers in HTML files
   - Update all cross-references and links
   - Update navigation footers
   - Update inline "Section X:" references

3. **Result**: All HTML files will have correct chapter numbers based on the new order in `chapters.json`

## Verification

After running the script, verify:
- ✅ Chapter numbers in titles match new order
- ✅ "Section X:" links are updated correctly
- ✅ Previous/Next navigation links are correct
- ✅ See Also sections have correct chapter numbers
- ✅ Cross-references are updated

## Notes

- **File names don't need to change**: The script works with existing file names. The order in `chapters.json` determines numbering.
- **Optional file renaming**: If desired, files could be renamed to match new numbers (e.g., `07-expressions.html` → `05-expressions.html`), but this is not required.
- **Script handles relinking**: All references to chapters are automatically updated based on the new order.
