; Raw shell payload inside $(...) capture
((shell_capture_expression
  payload: (shell_payload) @injection.content)
  (#set! injection.language "bash"))

; Raw shell payload inside process substitution <(...) / >(...)
((process_substitution_expression
  payload: (shell_payload) @injection.content)
  (#set! injection.language "bash"))

; sh "..."
((sh_statement
  command: (string) @injection.content)
  (#offset! @injection.content 0 1 0 -1)
  (#set! injection.include-children)
  (#set! injection.language "bash"))

; sh $"..."
((sh_statement
  command: (interpolated_string) @injection.content)
  (#offset! @injection.content 0 2 0 -1)
  (#set! injection.include-children)
  (#set! injection.language "bash"))

; sh [[...]]
((sh_statement
  command: (multiline_string) @injection.content)
  (#offset! @injection.content 0 2 0 -2)
  (#set! injection.include-children)
  (#set! injection.language "bash"))

; sh $[[...]]
((sh_statement
  command: (interpolated_multiline_string) @injection.content)
  (#offset! @injection.content 0 3 0 -2)
  (#set! injection.include-children)
  (#set! injection.language "bash"))
