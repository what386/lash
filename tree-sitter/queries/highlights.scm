; ============================================================
; Shebang
; ============================================================
(shebang) @keyword.directive

; ============================================================
; Preprocessor directives
; ============================================================
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

; Preprocessor arguments / names
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
"let"      @keyword
"const"    @keyword
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
"trap"     @keyword
"untrap"   @keyword
"enum"     @keyword
"sh"       @keyword

(break_statement)    @keyword
(continue_statement) @keyword

; ============================================================
; Booleans
; ============================================================
(boolean) @boolean

; ============================================================
; Numbers
; ============================================================
(number) @number

; ============================================================
; Strings
; ============================================================
(string)              @string
(interpolated_string) @string.special
(multiline_string)    @string
(glob_pattern)        @string.special

; ============================================================
; Variable references  ($var)
; ============================================================
(var_ref "$" @operator)
(var_ref name: (identifier) @variable)

; ============================================================
; Shell capture  $(...)
; ============================================================
(shell_capture_expression
  "$"                           @operator
  "("                           @punctuation.bracket
  payload: (capture_payload)    @string.special
  ")"                           @punctuation.bracket)

; ============================================================
; Function declarations
; ============================================================
(function_declaration
  name: (identifier) @function)

; ============================================================
; Parameters
; ============================================================
(parameter
  name: (identifier) @variable.parameter)
; default_value is an expression — highlight its outermost node naturally;
; the specific child nodes will be caught by their own rules below.

; ============================================================
; Function calls
; ============================================================
(function_call
  name: (identifier) @function.call)

; ============================================================
; Command statements  (bare-word shell invocations)
; ============================================================
(command_statement
  name: (identifier) @function.call)

; ============================================================
; sh / test statements — command expression highlighted as string
; ============================================================
(sh_statement   "sh"   @keyword
  command: (_) @string.special)
(test_statement "test" @keyword
  condition: (_) @string.special)

; ============================================================
; trap / untrap — signal names
; ============================================================
(trap_statement
  signal: (identifier) @constant)
(untrap_statement
  signal: (identifier) @constant)

; ============================================================
; Variable declarations
; ============================================================
(variable_declaration
  name: (identifier) @variable)

; ============================================================
; Assignments
; ============================================================
(assignment
  target: (identifier) @variable)


; ============================================================
; For / select loop variables
; ============================================================
(for_loop
  variable: (identifier) @variable)
(for_loop
  glob: (glob_pattern) @string.special)

(select_loop
  variable: (identifier) @variable)
(select_loop
  glob: (glob_pattern) @string.special)

; ============================================================
; wait — jobs keyword & into binding
; ============================================================
(wait_statement "jobs" @keyword)

; ============================================================
; into bindings
; ============================================================
(into_binding "into" @keyword)
(into_binding "let"  @keyword)
(into_binding "const" @keyword)
(into_binding
  target: (var_ref name: (identifier) @variable))
(into_binding
  name: (identifier) @variable)

; ============================================================
; Enum declarations
; ============================================================
(enum_declaration
  name: (identifier) @type)
(enum_member) @constant

; ============================================================
; Enum access  Foo::Bar
; ============================================================
(enum_access
  enum:   (identifier) @type
  member: (identifier) @constant)

; ============================================================
; Operators
; ============================================================
(pipe_expression "|" @operator)
(fd_dup_expression
  operator: (fd_dup_operator) @operator)

; Redirect operators — listed most-specific first so longer tokens win
(redirect_expression operator: "&>>"  @operator)
(redirect_expression operator: "2>>"  @operator)
(redirect_expression operator: ">>"   @operator)
(redirect_expression operator: "<<<"  @operator)
(redirect_expression operator: "<<"   @operator)
(redirect_expression operator: "&>"   @operator)
(redirect_expression operator: "2>"   @operator)
(redirect_expression operator: "<>"   @operator)

"&"  @operator
"::" @operator

(range_expression         ".."       @operator)
(additive_expression      operator: _ @operator)
(multiplicative_expression operator: _ @operator)
(comparison_expression    operator: _ @operator)
(logical_expression       "&&"       @operator)
(logical_expression       "||"       @operator)
(unary_expression         operator: _ @operator)
(assignment               operator: _ @operator)
(variable_declaration     "="        @operator)

; ============================================================
; Punctuation
; ============================================================
"," @punctuation.delimiter
":" @punctuation.delimiter
"(" @punctuation.bracket
")" @punctuation.bracket
"[" @punctuation.bracket
"]" @punctuation.bracket
