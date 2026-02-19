## Lash Language Spec (Current)

This document defines the language currently implemented in `src/Lash.Compiler/Lash.g4` and the current semantic/codegen pipeline.

### Design goal

Lash is a lua-like language that transpiles directly to Bash with minimal runtime overhead.

### File format

- Files use `.lash`.
- A leading shebang line (`#!...`) is allowed and stripped before parsing.
- Comments:
  - line comments: `// ...`
  - block comments: `/* ... */`

### Preprocessor directives

- Lash supports `@`-prefixed preprocessor directives evaluated before parsing.
- Supported directives:
  - conditionals: `@if`, `@elif`, `@else`, `@end`
  - textual include: `@import <path>`
  - literal passthrough: `@raw ... @end`
  - symbols: `@define`, `@undef`
  - diagnostics: `@error`, `@warning`
- Directives are compile-time only and do not exist at runtime.
- `@if`/`@elif` conditions support:
  - `defined(NAME)`
  - boolean literals (`true`, `false`)
  - numeric/string literals
  - symbol names from `@define`
  - operators: `!`, `&&`, `||`, `==`, `!=`, and parentheses
- `@define` accepts:
  - `@define NAME`
  - `@define NAME=value`
  - `@define NAME value`
- Inactive conditional branches are stripped before the parser runs.
- `@import` resolves relative paths from the file containing the directive.
- Repeated imports are allowed; each `@import` pastes the target file again.
- Import recursion/cycles are currently not detected by the preprocessor.
- `@import <path> into <name>` imports file text into a variable by emitting an assignment to a multiline string literal.
- `@import <path> into [let|const] <name>` controls the create-kind when the target does not already exist.
- `@import` (both forms) is only valid at file/preprocessor scope, not inside runtime Lash blocks (`if/fn/for/while/switch/enum/subshell`).
- `@raw` copies enclosed lines literally into generated Bash, and directives inside `@raw` are not evaluated.
- Directives are line-based: they must begin a logical line (indentation allowed).
- `into` creates the variable when missing (default `let`), otherwise it assigns to the existing variable.

### Top-level statements

- Variable declaration:
  - `let name = expr`
  - `const name = expr`
  - `global let name = expr`
  - `global const name = expr`
- Assignment:
  - `name = expr`
  - `name += expr` (array/list concatenation)
  - `global name = expr`
  - `arr[index] = expr`
- Function declaration:
  - `fn name(param1, param2 = defaultValue) ... end`
- Enum declaration:
  - `enum Name ...members... end`
- Control flow:
  - `if / elif / else / end`
  - `for x in expr [step expr] ... end`
  - `while expr ... end`
  - `switch expr`, with one or more `case expr: ...` clauses, then closing `end`
  - `break`
  - `continue`
  - `return [expr]`
  - `shift [n]` (mutates current `argv` frame)
  - `subshell [into [let|const] name] ... end [&]`
  - `wait [expr|jobs] [into [let|const] name]`
- Shell passthrough statement:
  - `sh $"...{var}..."` (emit the rendered command directly into generated bash)
- Command statement:
  - bare shell-like lines (for example `echo "hello"` or `rm -rf "$dir"`) are treated as raw command statements
- Expression statement:
  - any expression used as a statement

### Expressions

- Literals:
  - number: `123`, `3.14`
  - boolean: `true`, `false`
  - strings:
    - standard: `"text"`
    - interpolated: `$"hello {name}"`
    - multiline/raw: `[[...]]`
- Variable reference: `$name` — the `$` sigil is exclusively for referencing variables
- Built-in args:
  - `argv[index]` (0-based)
  - `#argv`
  - `shift [n]`
- Array literal: `[expr, expr, ...]`
- Identifier: `name`
- Function call: `name(args...)`
- Shell output capture: `$sh $"...{var}..."`
- Enum access: `EnumName::Member`
- Index access: `expr[expr]`
- Unary operators:
  - `!`, `-`, `+`, `#`
- Binary operators:
  - arithmetic: `*`, `/`, `%`, `+`, `-`
  - range: `..`
  - comparison: `==`, `!=`, `<`, `>`, `<=`, `>=`
  - logical: `&&`, `||`
  - pipe: `|`
  - redirection: `>`, `2>`, `&>`, `<`, `<>`, `>>`, `2>>`, `&>>`, `<<<`, `n>&m`, `n>&-`
  - append assignment: `+=`

### Type model (coarse)

- Coarse expression categories are used for semantic checks: `number`, `string`, `bool`, `array`, `unknown`.
- There is no user-facing type annotation syntax.

### Semantic rules

- Variables must be declared before use.
- `const` variables cannot be reassigned.
- Function arity is checked, including required vs default parameters.
- `break` and `continue` are valid only inside `for`/`while` loops.
- Enum accesses are validated (`EnumName::Member` must exist).
- Basic operation checks include:
  - invalid mixed add (e.g. number + string) is rejected
  - numeric operators require numeric operands
  - `#` requires an array or string operand to return its length

### Diagnostics (code families)

- `E000-E001`: lex/parse diagnostics
- `E110-E116`: name/declaration/scope diagnostics
- `E200-E202`: type and semantic compatibility diagnostics
- `E300-E303`: flow and constant-safety diagnostics
- `E400-E401`: codegen-feasibility diagnostics (constructs that cannot be lowered to Bash)
- `W500-W505`: non-fatal warnings (unreachable code, shadowing, empty `wait jobs`, and unused symbols)

### Pipe and redirection behavior

- Pipe (`|`) supports value-flow patterns used by the transpiler, including assignment sink forms.
- Redirection operators are supported for function/pipe expression statements:
  - `>` truncate-write stdout
  - `2>` truncate-write stderr
  - `&>` truncate-write both stdout+stderr
  - `<` redirect stdin from file
  - `<>` open read/write file for stdin
  - `>>` append stdout
  - `2>>` append stderr
  - `&>>` append both stdout+stderr
  - `<<<` here-string (feed string/expression value to stdin)
  - `n>&m` duplicate/open file descriptor `n` to `m` (for example `3>&1`)
  - `n>&-` close file descriptor `n` (for example `1>&-`)

### Code generation notes

- Target output is Bash.
- Enums are compile-time lowered:
  - `EnumName::Member` → string literal `"EnumNameMember"`
- `subshell ... end` lowers to a Bash subshell block `(...)`.
- `subshell ... end &` launches in background; `subshell into pid ... end &` captures `$!`.
- `wait` lowers directly to Bash `wait`; `wait jobs` drains tracked background subshell pids.
