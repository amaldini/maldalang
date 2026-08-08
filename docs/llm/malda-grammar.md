# MALDA grammar (plain text)

*Applies to: MALDA 0.1.21*

Extracted from `ReferenceManual/22-grammar.html` for LLM ingestion.
If this file disagrees with the parser (`MaldaLang/Parser/Parser.cs`), the **parser wins**.
Narrative examples: topic chapters under `ReferenceManual/` and `Examples/`.

```ebnf
Program     ::= TopLevelItem*
TopLevelItem::= IncludeStmt | UsingStmt | ImportStmt
              | WorkflowDecl | ActorDecl | ClassDecl | PromptDecl | TypeDecl | ComponentDecl
              | DecoratedFunctionDecl | DecoratedPropertyDecl | PropertyDecl
              | ExportableDecl | Statement

IncludeStmt ::= "include" StringLiteral ";"
UsingStmt   ::= "using" (Identifier "=")? QualifiedName ";"
ImportStmt  ::= "import" ( (Identifier "=")? StringLiteral | (Identifier "=")? QualifiedName ) ";"
ExportableDecl ::= "export"? ( FunctionDecl | ClassDecl | "var" Identifier TypeHint? "=" Expression ";" )
QualifiedName ::= Identifier ("." Identifier)*

FunctionDecl  ::= ("function" | "fn" | "def") Identifier "(" ParamList? ")" ReturnType?
                  ( Block | Expression ";" )
DecoratedFunctionDecl ::= Decorator+ FunctionDecl
ReturnType    ::= ("->" | "=>") Identifier

ClassDecl     ::= "class" Identifier ("extends" Identifier)? "{" ClassMember* "}"
ClassMember   ::= AccessModifier? (FieldDecl | MethodDecl | ConstructorDecl)
FieldDecl     ::= "var" Identifier TypeHint? ("=" Expression)? ";"
MethodDecl    ::= AccessModifier? FunctionDecl
ConstructorDecl ::= AccessModifier? FunctionDecl   /* name equals class name */

TypeDecl      ::= "type" Identifier "=" Constructor ("|" Constructor)* ";"
Constructor   ::= Identifier ("(" ParamList? ")")?

ActorDecl     ::= "actor" Identifier "{" ActorBodyItem* "}"
ActorBodyItem ::= MessageDecl | ActorMember
MessageDecl   ::= "message" Identifier "(" ParamList? ")" ReturnType? ";"
ActorMember   ::= AccessModifier? ( FieldDecl | "on" Identifier "(" ParamList? ")" ReturnType? Block
                  | MethodDecl | ConstructorDecl )

PromptDecl    ::= "prompt" Identifier "(" ParamList? ")" ReturnType? PromptBody
PromptBody    ::= Block | ObjectLiteral   /* statement body or object-literal config */

ComponentDecl ::= "component" Identifier ComponentParams? Block
ComponentParams ::= "(" ParamList? ")"

PropertyDecl  ::= "property" Identifier PropertyParams? Block
DecoratedPropertyDecl ::= Decorator+ PropertyDecl
Decorator     ::= "@" Identifier "(" ArgList? ")"

WorkflowDecl  ::= "workflow" Identifier "(" ParamList? ")" "{" WorkflowStmt* "}"
WorkflowStmt  ::= StepStmt | ApprovalStmt | WaitStmt | Statement
StepStmt      ::= "step" Identifier "=" CallExpr StepOptions? ";"
StepOptions   ::= ("retry" Integer | "backoff" String | "delay" Integer
                  | "maxDelay" Integer | "timeout" Integer | "compensate" CallExpr)*
ApprovalStmt  ::= "approval" Identifier "=" "approval"
                  "(" Expression ("," Expression)? ")" ApprovalOptions? ";"
ApprovalOptions ::= ("timeout" Integer | "onReject" CallExpr)*
WaitStmt      ::= "wait" Identifier "=" "awaitSignal"
                  "(" Expression ("," Expression)? ")" ("timeout" Integer)* ";"

AccessModifier ::= ("public" | "private")? "static"?
TypeHint      ::= ":" Identifier
ParamList     ::= Param ("," Param)*
Param         ::= Decorator* Identifier TypeHint?
CallExpr      ::= Expression PostfixSuffix*   /* see §34.4 */

Statement   ::= VarDecl | DestructuringVarDecl
              | Assignment | DestructuringAssignment
              | IfStmt | WhileStmt | ForStmt | ForeachStmt
              | ReturnStmt | PrintStmt | BreakStmt | ContinueStmt
              | TryStmt | ThrowStmt | SendStmt
              | MatchStmt | ExpressionStmt | Block

VarDecl     ::= "var" Identifier TypeHint? "=" Expression ";"
DestructuringVarDecl ::= "var" DestructuringPattern TypeHint? "=" Expression ";"
Assignment  ::= LValue AssignOp Expression ";"
AssignOp    ::= "=" | "+=" | "-=" | "*=" | "/="
LValue      ::= Identifier | MemberAccess | ArrayAccess
DestructuringAssignment ::= DestructuringPattern "=" Expression ";"

IfStmt      ::= "if" "(" Expression ")" Statement ("else" Statement)?
WhileStmt   ::= "while" "(" Expression ")" Statement
ForStmt     ::= "for" "(" (VarDecl | Assignment)? ";" Expression? ";" Assignment? ")" Statement
ForeachStmt ::= "foreach" "(" "var" Identifier "in" Expression ")" Statement
              | "for" "(" "var" Identifier "in" Expression ")" Statement

ReturnStmt  ::= "return" Expression? ";"
PrintStmt   ::= "print" "(" Expression ")" ";"
BreakStmt   ::= "break" ";"
ContinueStmt::= "continue" ";"

TryStmt     ::= "try" Block CatchClause+ FinallyClause?
              | "try" Block FinallyClause
CatchClause ::= "catch" ("(" Identifier ("if" Expression)? ")")? Block
FinallyClause ::= "finally" Block
ThrowStmt   ::= "throw" Expression ";"

SendStmt    ::= "send" SendTarget SendOptions? ";"
SendTarget  ::= Expression   /* send target.handler(args) or send target(args) */
SendOptions ::= "then" "(" Identifier ")" Block
              | "timeout" Expression ("catch" "(" Identifier ")" Block)?

MatchStmt   ::= "match" Expression "{" MatchCase* DefaultCase? "}" (";")?
MatchCase   ::= "case" Pattern ":" Statement (";")?
DefaultCase ::= "default" ":" Statement (";")?
ExpressionStmt ::= Expression ";"

Block       ::= "{" (TopLevelItem | Statement)* "}"

Expression  ::= MatchExpr | Ternary
MatchExpr   ::= "match" Expression "{" MatchCase* DefaultCase? "}"
Ternary     ::= LogicalOr ("?" Expression ":" Expression)?
LogicalOr   ::= LogicalAnd (("or" | "||") LogicalAnd)*
LogicalAnd  ::= Equality (("and" | "&&") Equality)*
Equality    ::= Comparison (("==" | "!=") Comparison)*
Comparison  ::= Additive (("<" | "<=" | ">" | ">=") Additive)*
Additive    ::= Multiplicative (("+" | "-") Multiplicative)*
Multiplicative ::= Unary (("*" | "/" | "%") Unary)*
Unary       ::= "await" Unary | "async" Unary
              | ("not" | "!" | "-" | "++" | "--") Unary
              | Postfix
Postfix     ::= Primary PostfixSuffix*
PostfixSuffix ::= "(" ArgList? ")" | "[" Expression "]" | "." Identifier
              | "++" | "--"
Primary     ::= Literal | Identifier | "(" Expression ")"
              | "this" | "super" | "self" | "null"
              | "new" Identifier "(" ArgList? ")"
              | "spawn" Identifier "(" ArgList? ")"
              | "receive" "(" ")"
              | ArrayLiteral | DictLiteral | GraphLiteral | ObjectLiteral
              | InterpolatedString | LambdaExpr

LambdaExpr  ::= LambdaParams Arrow (Expression | Block)
LambdaParams::= "(" ParamList? ")" | Identifier
Arrow         ::= "=>" | "->"   /* same Arrow token in lexer */

ArrayLiteral ::= "[" (Expression ("," Expression)*)? "]"
DictLiteral  ::= "dict" "{" (Expression ":" Expression ("," Expression ":" Expression)*)? "}"
GraphLiteral ::= "graph" ("directed" | "undirected") "{"
                  ("nodes" ":" Expression ("," "edges" ":" Expression)?)
                  ("edges" ":" Expression ("," "nodes" ":" Expression)?)?
                 "}"
ObjectLiteral ::= "{" (ObjectEntry ("," ObjectEntry)*)? "}"
ObjectEntry  ::= (StringLiteral | Identifier) ":" Expression

InterpolatedString ::= '$"' … '"' | '$"""' … '"""'

Pattern     ::= LiteralPattern | IdentifierPattern | WildcardPattern
              | VariantPattern | ArrayPattern | ObjectPattern
LiteralPattern ::= Integer | Float | StringLiteral | Boolean | "null"
IdentifierPattern ::= Identifier
WildcardPattern ::= "_"
VariantPattern ::= Identifier "(" (Pattern ("," Pattern)*)? ")"
ArrayPattern ::= "[" (Pattern ("," Pattern)*)? RestPattern? "]"
RestPattern ::= "..." Identifier?
ObjectPattern ::= "{" ObjectPatternEntry ("," ObjectPatternEntry)* "}"
ObjectPatternEntry ::= (Identifier | StringLiteral) (":" Pattern)?

DestructuringPattern ::= ArrayPattern | ObjectPattern

Identifier  ::= [A-Za-z_][A-Za-z0-9_]*
ArgList     ::= Expression ("," Expression)*

```

## Precedence (lowest to highest)

`match` expression, ternary `? :`, `or`/`||`, `and`/`&&`, equality, comparison, additive, multiplicative, unary (`await`, `async`, `not`/`!`, `-`, `++`/`--`), postfix (`()`, `[]`, `.`, postfix `++`/`--`).

## Notes

- `function` / `fn` / `def` are equivalent; prefer `function` in new code.
- Both `=>` and `->` are the same Arrow token (lambdas and return-type hints).
- Semantics: `docs/spec/malda-language-1.0.md`.
