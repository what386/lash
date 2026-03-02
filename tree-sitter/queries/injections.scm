; Raw shell payload inside $(...)
((shell_capture_expression
  payload: (shell_payload) @injection.content)
  (#set! injection.language "bash"))

; Raw shell payload inside <(...) / >(...)
((process_substitution_expression
  payload: (shell_payload) @injection.content)
  (#set! injection.language "bash"))

; sh "..."
((sh_statement
  command: (expression
    (primary_expression
      (string) @injection.content)))
  (#offset! @injection.content 0 1 0 -1)
  (#set! injection.include-children)
  (#set! injection.language "bash"))

; sh $"..."
((sh_statement
  command: (expression
    (primary_expression
      (interpolated_string) @injection.content)))
  (#offset! @injection.content 0 2 0 -1)
  (#set! injection.include-children)
  (#set! injection.language "bash"))

; sh [[...]]
((sh_statement
  command: (expression
    (primary_expression
      (multiline_string) @injection.content)))
  (#offset! @injection.content 0 2 0 -2)
  (#set! injection.include-children)
  (#set! injection.language "bash"))

; sh $[[...]]
((sh_statement
  command: (expression
    (primary_expression
      (interpolated_multiline_string) @injection.content)))
  (#offset! @injection.content 0 3 0 -2)
  (#set! injection.include-children)
  (#set! injection.language "bash"))
