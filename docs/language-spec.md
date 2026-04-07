## Lash Language Spec (Current)

This document describes the language currently implemented by `src/Lash.Compiler/Lash.g4` and the compiler pipeline.

### Design Goal

- Lash is a Lua-like scripting language that lowers directly to Bash with minimal runtime overhead.

### File Format

- Files use `.lash`.
- A leading shebang (`#!...`) is allowed and stripped before parsing.
- Comments:
  - line comments: `// ...`
  - block comments: `/* ... */`

### Preprocessor Directives

- Lash supports `@`-prefixed compile-time directives.
- Supported directives:
  - conditionals: `@if`, `@elif`, `@else`, `@end`
  - textual include: `@import <path>`
  - raw passthrough: `@raw ... @end`
  - symbols: `@define`, `@undef`
  - diagnostics: `@error`, `@warning`
- `@if`/`@elif` conditions support literals, defined-symbol presence checks, `==`, `!=`, `!`, `&&`, `||`, and parentheses.
- `@import <path> into <name>` imports file text into a variable as a multiline string.
- `@import` is only valid at file/preprocessor scope, not inside runtime blocks.
- `into` assigns an existing variable when present, otherwise creates a new `let`.

### Statements

- Variable declarations:
  - `var name = expr`
  - `let name = expr`
  - `readonly name = expr`
  - `global var name = expr`
  - `global let name = expr`
  - `global readonly name = expr`
- Assignments:
  - `name = expr`
  - `global name = expr`
  - `arr[index] = expr`
  - compound numeric updates: `+=`, `-=`, `*=`, `/=`, `%=`
  - postfix numeric updates: `name++`, `name--`
- Functions:
  - `fn name(param1, param2 = defaultValue) ... end`
- Enums:
  - `enum Name ...members... end`
- Control flow:
  - `if / elif / else / end`
  - `for x in expr [step expr] ... end`
  - `for x in glob-pattern ... end`
  - `select x in expr ... end`
  - `while expr ... end`
  - `until expr ... end`
  - `switch expr` with one or more `case` clauses, then `end`
  - `case expr:`
  - `case expr1, expr2:`
  - `case _:`
  - `break [n]`
  - `continue [n]`
  - `return [expr]`
  - `shift [n]`
  - `subshell [into name] ... end [&]`
  - `coproc [into name] ... end`
  - `wait [expr|jobs] [into name]`
- Shell-facing statements:
  - `sh "cmd"`
  - `test expr`
  - `trap SIGNAL into cleanup()`
  - `trap EXIT "echo done"`
  - `untrap SIGNAL`
- Raw shell command statements:
  - bare shell-like lines such as `echo "hello"`, `set -euo pipefail`, `export NAME=value`, or `source env.sh`
- Expression statements:
  - expressions used for value-flow or redirection forms

### Expressions

- Literals:
  - numbers: `123`, `3.14`
  - booleans: `true`, `false`
  - strings:
    - `"text"`
    - `$"hello {name}"`
    - `[[line1\nline2]]`
    - `$[[hello {name}\nmore]]`
- Variable reference: `name`
- Built-in args:
  - `argv[index]`
  - `#argv`
- Collections:
  - array literal: `[expr, expr, ...]`
  - map literal: `{ "key": expr, "other": expr }`
- Function call: `name(args...)`
- Shell output capture: `$(...)`
- Process substitution: `<(...)`, `>(...)`
- Enum access: `EnumName::Member`
- Index access: `expr[expr]`
- Unary operators:
  - `!`, `-`, `+`, `#`
- Binary operators:
  - arithmetic: `*`, `/`, `%`, `+`, `-`
  - range: `..`
  - comparison: `==`, `!=`, `<`, `>`, `<=`, `>=`
  - regex match: `=~`
  - logical: `&&`, `||`
  - pipe: `|`
  - redirection: `>`, `2>`, `&>`, `<`, `<>`, `>>`, `2>>`, `&>>`, `<<`, `<<-`, `n>&m`, `n>&-`

### Type Model

- Lash uses coarse expression categories for semantic checks:
  - `number`
  - `string`
  - `bool`
  - `array`
  - `unknown`
- There is no user-facing type annotation syntax.
- Map literals are represented as string-keyed array containers and lower to Bash associative arrays.

### Semantic Rules

- Variables must be declared before use.
- `let` variables cannot be reassigned.
- `readonly` requires an initializer and is rejected in repeated loop contexts.
- Function arity is checked, including required versus default parameters.
- `break` and `continue` are only valid inside `for`, `select`, `while`, and `until` loops.
- `break n` and `continue n` require a positive integer literal depth.
- Enum accesses are validated.
- Map literal keys must be strings.
- Indexing keeps key kinds consistent for a container: numeric and string keys cannot be mixed.
- `case _:` is the wildcard switch clause and cannot be combined with other patterns in the same clause.
- Duplicate constant switch patterns are diagnosed across the whole `switch`.
- Basic operation checks include:
  - invalid mixed add (for example `number + string`) is rejected
  - numeric operators require numeric operands
  - `#` requires an array/string-like operand
  - `=~` produces a boolean and is intended for condition positions; current Bash lowering rejects it in value-producing contexts

### Diagnostics

- `E000-E001`: lex/parse diagnostics
- `E010-E015`: preprocessor diagnostics
- `E110-E125`: name/declaration/scope diagnostics
- `E200-E203`: type and semantic compatibility diagnostics
- `E300-E303`: flow and constant-safety diagnostics
- `E400-E401`: codegen-feasibility diagnostics
- `W500-W522`: non-fatal warnings

### Pipe And Redirection Behavior

- Pipe (`|`) supports Lash value-flow patterns, including assignment-sink forms.
- Redirection operators are supported for function and pipe expression statements:
  - `>` truncate-write stdout
  - `2>` truncate-write stderr
  - `&>` truncate-write both stdout and stderr
  - `<` redirect stdin from a file
  - `<>` open a file read/write for stdin
  - `>>` append stdout
  - `2>>` append stderr
  - `&>>` append both stdout and stderr
  - `<<` stdin-string redirect:
    - single-line strings lower to Bash here-strings (`<<<`)
    - multiline literals lower to heredocs
  - `<<-` tab-stripping heredoc input
  - `n>&m` duplicate file descriptor `n` to `m`
  - `n>&-` close file descriptor `n`

### Code Generation Notes

- Target output is Bash.
- Enums are compile-time lowered:
  - `EnumName::Member` -> `"EnumNameMember"`
- Map literals lower to Bash associative arrays (`declare -A` / `local -A`).
- `switch` lowers to Bash `case`; multi-pattern Lash cases lower to `pattern1|pattern2)`.
- `=~` lowers to Bash `[[ lhs =~ rhs ]]` in condition code paths.
- `test expr` lowers to a Bash test command and can also be used inside `$(...)` capture.
- `subshell ... end` lowers to a Bash subshell block `(...)`.
- `subshell ... end &` launches in background; `into` captures `$!`.
- `coproc ... end` lowers to Bash `coproc { ... }`; `into` captures `${COPROC_PID}`.
- `wait` lowers directly to Bash `wait`; `wait jobs` drains tracked background subshell/coproc pids.
- Glob-style `for` and `select` loops lower directly to Bash word/glob iteration and follow the shell's active glob options.
