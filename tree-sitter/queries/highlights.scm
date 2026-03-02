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
; Booleans / Numbers / Strings
; ============================================================
(boolean) @boolean
(number)  @number

(string) @string
(interpolated_string) @string.special
(interpolated_multiline_string) @string.special
(multiline_string) @string
(glob_pattern) @string.special

; ============================================================
; Variables / argv
; ============================================================
(var_ref "$" @operator)
(var_ref name: (identifier) @variable)
(argv_length_expression "#" @operator)
(argv_index_expression "argv" @variable.builtin)
(argv_length_expression "argv" @variable.builtin)

; ============================================================
; Shell capture / process substitution
; ============================================================
(shell_capture_expression "$(" @operator)
(shell_capture_expression
  payload: (shell_payload) @string.special)
(process_substitution_expression
  operator: _ @operator)
(process_substitution_expression
  payload: (shell_payload) @string.special)

; ============================================================
; Functions / parameters / commands
; ============================================================
(function_declaration
  name: (identifier) @function)
(parameter
  name: (identifier) @variable.parameter)
(function_call
  name: (identifier) @function.call)

(command_statement
  name: (identifier) @function.call)
(command_statement
  argument: (bare_word) @string.special)

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
(into_binding
  target: (var_ref
    name: (identifier) @variable))

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
; Operators
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
