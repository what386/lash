; =============================================================================
; Lash – Tree-sitter highlights
; =============================================================================

; -----------------------------------------------------------------------------
; Keywords
; -----------------------------------------------------------------------------

[
  "let"
  "const"
  "readonly"
  "global"
] @keyword.storage

[
  "fn"
] @keyword.function

[
  "return"
] @keyword.return

[
  "if"
  "elif"
  "else"
  "end"
] @keyword.conditional

[
  "switch"
  "case"
] @keyword.conditional

[
  "for"
  "in"
  "step"
  "while"
  "until"
  "select"
] @keyword.repeat

[
  "break"
  "continue"
] @keyword.control

[
  "enum"
] @keyword.type

[
  "subshell"
  "coproc"
  "wait"
  "jobs"
  "shift"
  "into"
  "sh"
  "test"
  "trap"
  "untrap"
] @keyword.special

; -----------------------------------------------------------------------------
; Preprocessor directives
; -----------------------------------------------------------------------------

[
  "@if"
  "@elif"
  "@else"
  "@end"
  "@define"
  "@undef"
  "@import"
  "@raw"
  "@error"
  "@warning"
] @keyword.directive

(preprocessor_directive_argument) @string.special

; -----------------------------------------------------------------------------
; Declarations
; -----------------------------------------------------------------------------

(function_declaration
  name: (identifier) @function)

(parameter
  name: (identifier) @variable.parameter)

(parameter
  default_value: (_) @variable.parameter.default)

(enum_declaration
  name: (identifier) @type)

(enum_member
  (identifier) @constant)

; -----------------------------------------------------------------------------
; Calls
; -----------------------------------------------------------------------------

(function_call
  name: (identifier) @function.call)

(enum_access
  enum:   (identifier) @type
  member: (identifier) @constant)

; -----------------------------------------------------------------------------
; Variables
; -----------------------------------------------------------------------------

(variable_declaration
  name: (binding_name) @variable)

(readonly_declaration
  name: (binding_name) @variable)

(assignment
  target: (var_ref
    name: (identifier) @variable))

(update_statement
  target: (var_ref
    name: (identifier) @variable))

(var_ref
  name: (identifier) @variable)

; Built-in argv
(argv_index_expression) @variable.builtin
(argv_length_expression) @variable.builtin

; Binding names in for / select / into bindings
(for_loop
  variable: (binding_name) @variable)

(select_loop
  variable: (binding_name) @variable)

(into_binding
  name: (binding_name) @variable)

(into_binding
  target: (var_ref
    name: (identifier) @variable))

; Discard binding
(binding_name
  "_" @variable.builtin)

; Wildcard pattern in case
(wildcard_pattern) @variable.builtin

; -----------------------------------------------------------------------------
; Operators
; -----------------------------------------------------------------------------

[
  "=" "+=" "-=" "*=" "/=" "%="
] @operator

[
  "++" "--"
] @operator

[
  "+" "-" "*" "/" "%"
] @operator.arithmetic

[
  "==" "!=" "<" ">" "<=" ">="
] @operator.comparison

[
  "&&" "||" "!"
] @operator.logical

[
  "|"
] @operator.pipe

[
  ">>" "&>>" "2>>"
  "<<" "<<-"
  "&>" "2>"
  "<>"
] @operator.redirect

(fd_dup_operator) @operator.redirect

".." @operator.range

"#" @operator.length

; -----------------------------------------------------------------------------
; Literals
; -----------------------------------------------------------------------------

(boolean)  @boolean
(number)   @number

(string)                     @string
(multiline_string)           @string
(interpolated_string)        @string
(interpolated_multiline_string) @string

; Interpolation delimiters inside interpolated strings
; (Tree-sitter does not expose sub-tokens here; highlight the whole node uniformly)

; -----------------------------------------------------------------------------
; Shell integration
; -----------------------------------------------------------------------------

(shell_capture_expression
  payload: (shell_payload) @string.special)

(process_substitution_expression
  payload: (shell_payload) @string.special)

(sh_statement
  command: (_) @string.special)

(command_statement
  name: (identifier) @function.builtin)

(command_argument) @string

(glob_pattern) @string.special

; -----------------------------------------------------------------------------
; Comments
; -----------------------------------------------------------------------------

(line_comment)  @comment
(block_comment) @comment

; -----------------------------------------------------------------------------
; Punctuation
; -----------------------------------------------------------------------------

["(" ")"] @punctuation.bracket
["[" "]"] @punctuation.bracket
"::"      @punctuation.delimiter
":"       @punctuation.delimiter
","       @punctuation.delimiter

; Background launch
"&" @operator.special

; -----------------------------------------------------------------------------
; Shebang
; -----------------------------------------------------------------------------

(shebang) @comment.special
