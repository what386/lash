; ============================================================
; Punctuation / operators (bash-style baseline)
; ============================================================
[
  "("
  ")"
  "["
  "]"
] @punctuation.bracket

[
  ","
  ":"
] @punctuation.delimiter

[
  ">"
  ">>"
  "<"
  "<<"
  "<<-"
  "&&"
  "|"
  "||"
  "="
  "+="
  "-="
  "*="
  "/="
  "%="
  "=="
  "!="
  "&>"
  "&>>"
  "2>"
  "2>>"
  "<>"
  ".."
  "!"
  "++"
  "--"
  "::"
  "#"
] @operator

; ============================================================
; Shebang / preprocessor
; ============================================================
(shebang) @keyword.directive

(preprocessor_if_directive) @keyword.directive
(preprocessor_elif_directive) @keyword.directive
(preprocessor_else_directive) @keyword.directive
(preprocessor_end_directive) @keyword.directive
(preprocessor_import_directive) @keyword.directive
(preprocessor_raw_directive) @keyword.directive
(preprocessor_define_directive) @keyword.directive
(preprocessor_undef_directive) @keyword.directive
(preprocessor_error_directive) @keyword.directive
(preprocessor_warning_directive) @keyword.directive

(preprocessor_import_directive
  path: (preprocessor_directive_argument) @string.special)
(preprocessor_define_directive
  name: (identifier) @constant)
(preprocessor_define_directive
  value: (preprocessor_directive_argument) @string.special)
(preprocessor_undef_directive
  name: (identifier) @constant)
(preprocessor_if_directive
  condition: (preprocessor_directive_argument) @string.special)
(preprocessor_elif_directive
  condition: (preprocessor_directive_argument) @string.special)
(preprocessor_error_directive
  message: (preprocessor_directive_argument) @string.special)
(preprocessor_warning_directive
  message: (preprocessor_directive_argument) @string.special)

; ============================================================
; Comments
; ============================================================
(line_comment) @comment
(block_comment) @comment

; ============================================================
; Keywords
; ============================================================
"fn"       @keyword.function
"return"   @keyword.return
"end"      @keyword
"var"      @keyword
"let"      @keyword
"readonly" @keyword
"global"   @keyword

"if"       @keyword.conditional
"elif"     @keyword.conditional
"else"     @keyword.conditional
"switch"   @keyword.conditional
"case"     @keyword.conditional

"for"      @keyword.repeat
"select"   @keyword.repeat
"in"       @keyword.repeat
"step"     @keyword.repeat
"while"    @keyword.repeat
"until"    @keyword.repeat
"subshell" @keyword.repeat
"coproc"   @keyword.repeat
"wait"     @keyword.repeat
"shift"    @keyword.repeat

"into"     @keyword
"test"     @keyword
"sh"       @keyword
"trap"     @keyword
"untrap"   @keyword
"enum"     @keyword
"jobs"     @keyword

(break_statement)    @keyword
(continue_statement) @keyword

; ============================================================
; Literals
; ============================================================
(boolean) @boolean
(number) @number

[
  (string)
  (multiline_string)
] @string

[
  (interpolated_string)
  (interpolated_multiline_string)
] @string.special

(glob_pattern) @string.special
(shell_payload) @string.special

; ============================================================
; Variables / sigils
; ============================================================
(var_ref "$" @punctuation.special)
(shell_capture_expression "$(" @punctuation.special)
(process_substitution_expression
  operator: _ @punctuation.special)

(var_ref name: (identifier) @variable)

((var_ref name: (identifier) @constant)
  (#lua-match? @constant "^[A-Z][A-Z_0-9]*$"))

(primary_expression
  (identifier) @variable)

((primary_expression
   (identifier) @constant)
  (#lua-match? @constant "^[A-Z][A-Z_0-9]*$"))

(argv_index_expression "argv" @variable.builtin)
(argv_length_expression "argv" @variable.builtin)

; ============================================================
; Functions / commands
; ============================================================
(function_declaration
  name: (identifier) @function)
(parameter
  name: (identifier) @variable.parameter)
(function_call
  name: (identifier) @function.call)

(command_statement
  name: (identifier) @function.builtin
  (#any-of? @function.builtin
    "." ":" "alias" "bg" "bind" "break" "builtin" "caller" "cd" "command" "compgen" "complete"
    "compopt" "continue" "coproc" "declare" "dirs" "disown" "echo" "enable" "eval" "exec" "exit"
    "export" "false" "fc" "fg" "getopts" "hash" "help" "history" "jobs" "kill" "local" "mapfile"
    "popd" "printf" "pushd" "pwd" "read" "readarray" "readonly" "return" "set" "shift" "shopt"
    "source" "suspend" "test" "time" "times" "trap" "true" "type" "typeset" "ulimit" "umask"
    "unalias" "unset" "wait"))

(command_statement
  name: (identifier) @function.call)

(command_statement
  argument: (command_argument
    (bare_word) @variable.parameter))

; ============================================================
; Declarations / bindings
; ============================================================
(variable_declaration
  name: (binding_name) @variable)
(readonly_declaration
  name: (binding_name) @variable)
(for_loop
  variable: (binding_name) @variable)
(select_loop
  variable: (binding_name) @variable)
(into_binding
  name: (binding_name) @variable)

(assignment
  target: (identifier) @variable)

((assignment
   target: (identifier) @constant)
  (#lua-match? @constant "^[A-Z][A-Z_0-9]*$"))

(update_statement
  target: (identifier) @variable)

; ============================================================
; Enums
; ============================================================
(enum_declaration
  name: (identifier) @type)
(enum_member) @constant
(enum_access
  enum: (identifier) @type
  member: (identifier) @constant)
(wildcard_pattern) @constant.builtin

; ============================================================
; Expression operator fields
; ============================================================
(assignment operator: _ @operator)
(update_statement operator: _ @operator)
(unary_expression operator: _ @operator)
(multiplicative_expression operator: _ @operator)
(additive_expression operator: _ @operator)
(comparison_expression operator: _ @operator)
(logical_expression "&&" @operator)
(logical_expression "||" @operator)
(range_expression ".." @operator)
(pipe_expression "|" @operator)
(enum_access "::" @operator)

(fd_dup_expression
  operator: (fd_dup_operator) @operator)

(redirect_expression operator: "&>>" @operator)
(redirect_expression operator: "2>>" @operator)
(redirect_expression operator: ">>" @operator)
(redirect_expression operator: "<<-" @operator)
(redirect_expression operator: "<<" @operator)
(redirect_expression operator: "&>" @operator)
(redirect_expression operator: "2>" @operator)
(redirect_expression operator: "<>" @operator)
