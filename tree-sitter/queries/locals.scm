; Scopes
(function_declaration) @local.scope
(for_loop) @local.scope
(select_loop) @local.scope
(while_loop) @local.scope
(until_loop) @local.scope
(subshell_statement) @local.scope
(coproc_statement) @local.scope

; Definitions
(function_declaration
  name: (identifier) @local.definition.function)

(parameter
  name: (identifier) @local.definition.var)

(variable_declaration
  name: (binding_name
    (identifier) @local.definition.var))

(readonly_declaration
  name: (binding_name
    (identifier) @local.definition.var))

(for_loop
  variable: (binding_name
    (identifier) @local.definition.var))

(select_loop
  variable: (binding_name
    (identifier) @local.definition.var))

(into_binding
  name: (binding_name
    (identifier) @local.definition.var))

; References
(primary_expression
  (identifier) @local.reference)

(assignment
  target: (identifier) @local.reference)

(update_statement
  target: (identifier) @local.reference)

(function_call
  name: (identifier) @local.reference)

(enum_access
  enum: (identifier) @local.reference)
