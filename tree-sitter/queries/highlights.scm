; Shebang
(shebang) @keyword.directive

; Preprocessor directives
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

(preprocessor_undef_directive
  name: (identifier) @constant)

(preprocessor_if_directive
  condition: (preprocessor_directive_argument) @string.special)

(preprocessor_elif_directive
  condition: (preprocessor_directive_argument) @string.special)

(preprocessor_define_directive
  value: (preprocessor_directive_argument) @string.special)

(preprocessor_error_directive
  message: (preprocessor_directive_argument) @string.special)

(preprocessor_warning_directive
  message: (preprocessor_directive_argument) @string.special)

; Comments
(line_comment) @comment
(block_comment) @comment

; Keywords
"fn" @keyword.function
"return" @keyword.return
"end" @keyword
"let" @keyword
"const" @keyword
"global" @keyword
"if" @keyword.conditional
"elif" @keyword.conditional
"else" @keyword.conditional
"switch" @keyword.conditional
"case" @keyword.conditional
"for" @keyword.repeat
"in" @keyword.repeat
"step" @keyword.repeat
"while" @keyword.repeat
"subshell" @keyword.repeat
"wait" @keyword.repeat
"into" @keyword
"jobs" @keyword
"sh" @keyword
"shift" @keyword.repeat
"enum" @keyword
(break_statement) @keyword
(continue_statement) @keyword

; Booleans
(boolean) @boolean

; Numbers
(number) @number

; Strings
(string) @string
(interpolated_string) @string.special
(multiline_string) @string

; Variable references ($var)
(var_ref "$" @operator)
(var_ref name: (identifier) @variable)

; Function declarations
(function_declaration
  name: (identifier) @function)

; Parameters
(parameter
  name: (identifier) @variable.parameter)

(parameter
  default_value: (_) @variable.parameter.default)

; Function calls
(function_call
  name: (identifier) @function.call)

; Shell capture expression ($sh ...)
(shell_capture_expression
  "$" @operator
  "sh" @keyword
  command: (_))

; Command statements (bare word invocations)
(command_statement
  name: (identifier) @function.call)


; Variable declarations
(variable_declaration
  name: (identifier) @variable)

; Assignments
(assignment
  target: (identifier) @variable)

; For loop variable
(for_loop
  variable: (identifier) @variable)

(for_loop
  glob: (glob_pattern) @string.special)

(into_binding
  name: (identifier) @variable)

; Enum declarations
(enum_declaration
  name: (identifier) @type)

(enum_declaration
  (enum_member) @constant)

; Enum access
(enum_access
  enum: (identifier) @type
  member: (identifier) @constant)

; Operators
(pipe_expression "|" @operator)
(fd_dup_expression operator: (fd_dup_operator) @operator)
(redirect_expression operator: "&>>" @operator)
(redirect_expression operator: "2>>" @operator)
(redirect_expression operator: ">>" @operator)
(redirect_expression operator: "<<<" @operator)
(redirect_expression operator: "&>" @operator)
(redirect_expression operator: "2>" @operator)
(redirect_expression operator: "<>" @operator)
"&" @operator
(range_expression ".." @operator)
(additive_expression operator: _ @operator)
(multiplicative_expression operator: _ @operator)
(comparison_expression operator: _ @operator)
(logical_expression "&&" @operator)
(logical_expression "||" @operator)
(unary_expression operator: _ @operator)
(assignment operator: _ @operator)
(variable_declaration "=" @operator)

; Enum access operator
"::" @operator

; Punctuation
"," @punctuation.delimiter
":" @punctuation.delimiter
"(" @punctuation.bracket
")" @punctuation.bracket
"[" @punctuation.bracket
"]" @punctuation.bracket
