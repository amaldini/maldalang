# Chapter Reordering Proposal

## Rationale

The proposed reordering follows a natural learning progression for users learning MALDA:

### Learning Flow

1. **Language Fundamentals (1-9)**: Core language concepts in logical order
   - Introduction → Lexical Structure → Data Types → Variables → Expressions → Control Structures → Functions → Arrays → Classes
   - This order ensures users understand syntax before semantics, basic types before complex structures, and simple constructs before advanced ones

2. **Built-in Features (10-13)**: Essential built-in capabilities
   - Input/Output → Built-in Functions → Graphs → VectorDB
   - I/O comes first as it's needed for any practical program
   - Built-in functions are fundamental utilities
   - Data structures (Graphs, VectorDB) come after understanding basic language

3. **AI & Advanced Features (14-15)**: AI-specific capabilities
   - Actors → Agent Orchestration
   - These build on the language fundamentals and are MALDA's unique strengths

4. **Extended Features (16-22)**: Advanced built-in capabilities
   - Database → Web UI → REST API → REST Client → MCP Server → .NET Interop → Device Integration
   - These are specialized features that users can learn as needed

5. **Reference Material (23-25)**: Documentation and examples
   - Examples → Grammar → Appendix
   - Reference material comes last, as it's consulted rather than read sequentially

## Key Changes from Current Order

| Old Order | New Order | Change |
|-----------|-----------|--------|
| 5. Arrays | 8. Arrays | Moved after Functions (functions are more fundamental) |
| 6. Graphs | 12. Graphs | Moved after Built-in Functions |
| 7. VectorDB | 13. VectorDB | Moved after Graphs (similar data structure concepts) |
| 7. Expressions | 5. Expressions | Moved before Control Structures (needed for conditions) |
| 8. Control Structures | 6. Control Structures | Moved after Expressions |
| 9. Functions | 7. Functions | Moved before Arrays |
| 10. Classes & Objects | 9. Classes & Objects | Moved after Arrays (OOP after basic data structures) |
| 12. Input/Output | 10. Input/Output | Moved earlier (essential for practical programs) |
| 13. Actors | 14. Actors | Moved after VectorDB (advanced feature) |
| 14. Agent Orchestration | 15. Agent Orchestration | Moved after Actors |
| 15. Database | 16. Database | Moved after AI features |
| 20. Examples | 23. Examples | Moved to reference section |
| 22. Grammar | 24. Grammar | Moved to reference section |
| 23. Appendix | 25. Appendix | Moved to end |

## File Renaming Required

**Note**: The current file names (e.g., `06-vectordb.html`, `07-expressions.html`) don't match the new order. The script will update chapter numbers in the content, but file names should ideally be renamed to match. However, this is optional - the script works with any file names as long as `chapters.json` correctly maps them.

## Benefits

1. **Logical Progression**: Users learn concepts in order of dependency
2. **Better Learning Curve**: Simple concepts before complex ones
3. **Practical First**: I/O and built-in functions come early for practical use
4. **Reference Last**: Examples and grammar are at the end where they belong
5. **AI Features Grouped**: All AI-related features are together (14-15)

## Implementation

1. Update `chapters.json` with new order (done)
2. Run `update-chapter-numbers.malda` to update all HTML files
3. Optionally rename files to match new numbers (e.g., `06-vectordb.html` → `13-vectordb.html`)

The script now handles:
- ✅ Page titles
- ✅ Breadcrumbs  
- ✅ H1 headings
- ✅ H2/H3 section headings
- ✅ Inline "Section X:" links in main content
- ✅ See Also section links
- ✅ Navigation footer links (Previous/Next)
