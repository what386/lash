module.exports = grammar({
  name: "lash",

  word: $ => $.identifier,

  conflicts: $ => [
    [$.argv_length_expression, $.argv_index_expression],
  ],

  extras: $ => [
    /[ \t\r\n]/,
    $.line_comment,
    $.block_comment,
  ],

  rules: {
    // -------------------------------------------------------------------------
    // Top level
    // -------------------------------------------------------------------------
    source_file: $ => seq(
      optional($.shebang),
      repeat($.statement),
    ),

    shebang: _ => token(seq("#!", /[^\r\n]*/)),

    // -------------------------------------------------------------------------
    // Statements
    // -------------------------------------------------------------------------
    statement: $ => choice(
      $.preprocessor_directive,
      $.variable_declaration,
      $.readonly_declaration,
      $.enum_declaration,
      $.assignment,
      $.update_statement,
      $.function_declaration,
      $.if_statement,
      $.switch_statement,
      $.for_loop,
      $.select_loop,
      $.while_loop,
      $.until_loop,
      $.subshell_statement,
      $.coproc_statement,
      $.wait_statement,
      $.sh_statement,
      $.test_statement,
      $.trap_statement,
      $.untrap_statement,
      $.return_statement,
      $.shift_statement,
      $.break_statement,
      $.continue_statement,
      $.command_statement,
      $.expression_statement,
    ),

    // -------------------------------------------------------------------------
    // Shell passthrough / test / trap
    // -------------------------------------------------------------------------
    sh_statement: $ => seq(
      "sh",
      field("command", $.expression),
    ),

    test_statement: $ => seq(
      "test",
      field("condition", $.expression),
    ),

    trap_statement: $ => seq(
      "trap",
      field("signal", $.identifier),
      choice(
        seq("into", field("handler", $.function_call)),
        field("command", $.expression),
      ),
    ),

    untrap_statement: $ => seq(
      "untrap",
      field("signal", $.identifier),
    ),

    // -------------------------------------------------------------------------
    // Preprocessor directives
    // -------------------------------------------------------------------------
    preprocessor_directive: $ => choice(
      $.preprocessor_if_directive,
      $.preprocessor_elif_directive,
      $.preprocessor_else_directive,
      $.preprocessor_end_directive,
      $.preprocessor_import_directive,
      $.preprocessor_raw_directive,
      $.preprocessor_define_directive,
      $.preprocessor_undef_directive,
      $.preprocessor_error_directive,
      $.preprocessor_warning_directive,
    ),

    preprocessor_if_directive: $ => seq(
      "@if",
      field("condition", $.preprocessor_directive_argument),
    ),

    preprocessor_elif_directive: $ => seq(
      "@elif",
      field("condition", $.preprocessor_directive_argument),
    ),

    preprocessor_else_directive: _ => "@else",
    preprocessor_end_directive:  _ => "@end",

    preprocessor_import_directive: $ => seq(
      "@import",
      field("path", $.preprocessor_directive_argument),
    ),

    preprocessor_raw_directive: _ => "@raw",

    preprocessor_define_directive: $ => seq(
      "@define",
      field("name", $.identifier),
      optional(field("value", $.preprocessor_directive_argument)),
    ),

    preprocessor_undef_directive: $ => seq(
      "@undef",
      field("name", $.identifier),
    ),

    preprocessor_error_directive: $ => seq(
      "@error",
      optional(field("message", $.preprocessor_directive_argument)),
    ),

    preprocessor_warning_directive: $ => seq(
      "@warning",
      optional(field("message", $.preprocessor_directive_argument)),
    ),

    // -------------------------------------------------------------------------
    // Variable / constant declarations
    // -------------------------------------------------------------------------
    variable_declaration: $ => seq(
      optional("global"),
      field("kind", choice("let", "const")),
      field("name", $.binding_name),
      optional(seq("=", field("value", $.expression))),
    ),

    readonly_declaration: $ => seq(
      optional("global"),
      "readonly",
      field("name", $.binding_name),
      "=",
      field("value", $.expression),
    ),

    // -------------------------------------------------------------------------
    // Assignment / update
    // -------------------------------------------------------------------------
    assignment: $ => seq(
      optional("global"),
      field("target", choice($.var_ref, $.index_access)),
      field("operator", choice("=", "+=", "-=", "*=", "/=", "%=")),
      field("value", $.expression),
    ),

    update_statement: $ => seq(
      optional("global"),
      field("target", $.var_ref),
      field("operator", choice("++", "--")),
    ),

    // -------------------------------------------------------------------------
    // Function declaration
    // -------------------------------------------------------------------------
    function_declaration: $ => seq(
      "fn",
      field("name", $.identifier),
      "(",
      optional($.parameter_list),
      ")",
      field("body", $.block),
    ),

    parameter_list: $ => seq(
      $.parameter,
      repeat(seq(",", $.parameter)),
    ),

    parameter: $ => seq(
      field("name", $.identifier),
      optional(seq("=", field("default_value", $.expression))),
    ),

    block: $ => seq(
      repeat($.statement),
      "end",
    ),

    // -------------------------------------------------------------------------
    // Control flow
    // -------------------------------------------------------------------------
    if_statement: $ => seq(
      "if",
      field("condition", $.expression),
      field("body", repeat($.statement)),
      repeat(field("elif_clause", $.elif_clause)),
      optional(field("else_clause", $.else_clause)),
      "end",
    ),

    elif_clause: $ => seq(
      "elif",
      field("condition", $.expression),
      field("body", repeat($.statement)),
    ),

    else_clause: $ => seq(
      "else",
      field("body", repeat($.statement)),
    ),

    switch_statement: $ => seq(
      "switch",
      field("value", $.expression),
      repeat1($.case_clause),
      "end",
    ),

    case_clause: $ => seq(
      "case",
      field("pattern", choice($.wildcard_pattern, $.expression)),
      ":",
      field("body", repeat($.statement)),
    ),

    wildcard_pattern: _ => "_",

    for_loop: $ => seq(
      "for",
      field("variable", $.binding_name),
      "in",
      choice(
        seq(
          field("iterable", $.expression),
          optional(seq("step", field("step", $.expression))),
        ),
        field("glob", $.glob_pattern),
      ),
      field("body", $.block),
    ),

    select_loop: $ => seq(
      "select",
      field("variable", $.binding_name),
      "in",
      choice(
        field("options", $.expression),
        field("glob", $.glob_pattern),
      ),
      field("body", $.block),
    ),

    while_loop: $ => seq(
      "while",
      field("condition", $.expression),
      field("body", $.block),
    ),

    until_loop: $ => seq(
      "until",
      field("condition", $.expression),
      field("body", $.block),
    ),

    // -------------------------------------------------------------------------
    // Subshell / coproc / wait
    // -------------------------------------------------------------------------
    subshell_statement: $ => seq(
      "subshell",
      optional(field("into", $.into_binding)),
      field("body", repeat($.statement)),
      "end",
      optional("&"),
    ),

    coproc_statement: $ => seq(
      "coproc",
      optional(field("into", $.into_binding)),
      field("body", repeat($.statement)),
      "end",
    ),

    wait_statement: $ => prec.right(seq(
      "wait",
      optional(field("target", choice("jobs", $.expression))),
      optional(field("into", $.into_binding)),
    )),

    into_binding: $ => choice(
      seq("into", field("target", $.var_ref)),
      seq("into", field("mode", choice("let", "const")), field("name", $.binding_name)),
    ),

    // -------------------------------------------------------------------------
    // Simple statements
    // -------------------------------------------------------------------------
    return_statement: $ => prec.right(seq(
      "return",
      optional(field("value", $.expression)),
    )),

    shift_statement: $ => prec.right(seq(
      "shift",
      optional(field("amount", $.expression)),
    )),

    break_statement:    _ => "break",
    continue_statement: _ => "continue",

    // -------------------------------------------------------------------------
    // Command statement
    // Bare identifier followed by at least one argument so it is distinguishable
    // from a plain expression_statement (bare identifier with no args).
    // -------------------------------------------------------------------------
    command_statement: $ => prec(-1, seq(
      field("name", $.identifier),
      repeat1(field("argument", $.command_argument)),
    )),

    command_argument: $ => choice(
      $.var_ref,
      $.shell_capture_expression,
      $.process_substitution_expression,
      $.interpolated_string,
      $.interpolated_multiline_string,
      $.multiline_string,
      $.string,
      $.number,
      $.glob_pattern,
      $.bare_word,
    ),

    // Bare words that appear as shell arguments (flags, paths, bare names, etc.)
    bare_word: _ => token(prec(-1, /[a-zA-Z0-9_\-./]+/)),

    expression_statement: $ => $.expression,

    // -------------------------------------------------------------------------
    // Expressions — precedence table (higher number = tighter binding)
    //
    //  1  pipe
    //  1  fd_dup / redirect
    //  2  ||
    //  3  &&
    //  4  comparison  (== != < > <= >=)
    //  5  range  (..)
    //  6  additive  (+ -)
    //  7  multiplicative  (* / %)
    //  8  unary  (! - + #)   right-assoc
    //  9  index access
    // 10  function call / enum access (primary)
    // -------------------------------------------------------------------------
    expression: $ => choice(
      $.pipe_expression,
      $.fd_dup_expression,
      $.redirect_expression,
      $.logical_expression,
      $.comparison_expression,
      $.range_expression,
      $.additive_expression,
      $.multiplicative_expression,
      $.unary_expression,
      $.index_access,
      $.primary_expression,
    ),

    pipe_expression: $ => prec.left(1, seq(
      field("left",  $.expression),
      "|",
      field("right", $.expression),
    )),

    fd_dup_expression: $ => prec.left(1, seq(
      field("left",     $.expression),
      field("operator", $.fd_dup_operator),
    )),

    // Redirect operators in precedence order (longest tokens first to avoid
    // ambiguity with comparison operators like < and >).
    redirect_expression: $ => prec.left(1, seq(
      field("left",     $.expression),
      field("operator", choice(
        "&>>", "2>>", ">>",
        "<<-", "<<",
        "&>",  "2>",
        "<>",
        // plain > and < are NOT listed here — they are handled in
        // comparison_expression and process_substitution_expression.
        // Emit them via process_substitution or sh statements instead.
      )),
      field("right", $.expression),
    )),

    logical_expression: $ => choice(
      prec.left(2, seq(
        field("left",  $.expression),
        "||",
        field("right", $.expression),
      )),
      prec.left(3, seq(
        field("left",  $.expression),
        "&&",
        field("right", $.expression),
      )),
    ),

    comparison_expression: $ => prec.left(4, seq(
      field("left",     $.expression),
      field("operator", choice("==", "!=", "<=", ">=", "<", ">")),
      field("right",    $.expression),
    )),

    range_expression: $ => prec.left(5, seq(
      field("start", $.expression),
      "..",
      field("end",   $.expression),
    )),

    additive_expression: $ => prec.left(6, seq(
      field("left",     $.expression),
      field("operator", choice("+", "-")),
      field("right",    $.expression),
    )),

    multiplicative_expression: $ => prec.left(7, seq(
      field("left",     $.expression),
      field("operator", choice("*", "/", "%")),
      field("right",    $.expression),
    )),

    unary_expression: $ => prec.right(8, seq(
      field("operator", choice("!", "-", "+", "#")),
      field("operand",  $.expression),
    )),

    index_access: $ => prec.left(9, seq(
      field("array", $.expression),
      "[",
      field("index", $.expression),
      "]",
    )),

    primary_expression: $ => choice(
      $.shell_capture_expression,
      $.process_substitution_expression,
      $.argv_expression,
      $.var_ref,
      $.boolean,
      $.number,
      $.string,
      $.interpolated_string,
      $.interpolated_multiline_string,
      $.multiline_string,
      $.array_literal,
      $.enum_access,
      $.function_call,
      seq("(", $.expression, ")"),
    ),

    // $(...) — shell output capture
    shell_capture_expression: $ => seq(
      token("$("),
      field("payload", $.shell_payload),
      ")",
    ),

    // <(...) or >(...) — process substitution
    process_substitution_expression: $ => seq(
      field("operator", choice(
        token("<("),
        token(">("),
      )),
      field("payload", $.shell_payload),
      ")",
    ),

    // Raw shell text inside $(...) / <(...) / >(...)
    shell_payload: _ => token(prec(1, /[^)\r\n]+/)),

    // Built-in argv access — split into two rules to avoid the '#' 'argv' '[' conflict
    argv_expression: $ => choice(
      $.argv_index_expression,
      $.argv_length_expression,
    ),

    argv_index_expression: $ => seq(
      "argv",
      "[",
      field("index", $.expression),
      "]",
    ),

    argv_length_expression: _ => seq("#", "argv"),

    // $name — variable reference ($ sigil is exclusive)
    var_ref: $ => seq("$", field("name", $.identifier)),

    array_literal: $ => seq(
      "[",
      optional(seq(
        $.expression,
        repeat(seq(",", $.expression)),
      )),
      "]",
    ),

    function_call: $ => prec(10, seq(
      field("name", $.identifier),
      "(",
      optional($.argument_list),
      ")",
    )),

    argument_list: $ => seq(
      $.expression,
      repeat(seq(",", $.expression)),
    ),

    enum_access: $ => prec(10, seq(
      field("enum",   $.identifier),
      "::",
      field("member", $.identifier),
    )),

    // -------------------------------------------------------------------------
    // Enum declaration
    // -------------------------------------------------------------------------
    enum_declaration: $ => seq(
      "enum",
      field("name", $.identifier),
      repeat(field("member", $.enum_member)),
      "end",
    ),

    enum_member: $ => $.identifier,

    // -------------------------------------------------------------------------
    // Binding name (identifier or discard _)
    // -------------------------------------------------------------------------
    binding_name: $ => choice($.identifier, "_"),

    // -------------------------------------------------------------------------
    // Literals & terminals
    // -------------------------------------------------------------------------
    boolean: _ => choice("true", "false"),

    // Float before integer so 3.14 is not tokenised as 3 + .. + 14
    number: _ => token(choice(
      /[0-9]+\.[0-9]+/,
      /[0-9]+/,
    )),

    identifier: _ => /[a-zA-Z_][a-zA-Z0-9_]*/,

    string: _ => token(seq(
      '"',
      repeat(choice(/[^"\\\r\n]+/, /\\./)),
      '"',
    )),

    interpolated_string: _ => token(seq(
      '$"',
      repeat(choice(/[^"\\{]+/, /\\./, /\{[^}\r\n]*\}/)),
      '"',
    )),

    interpolated_multiline_string: _ => token(seq(
      "$[[",
      /[\s\S]*?/,
      "]]",
    )),

    multiline_string: _ => token(seq(
      "[[",
      /[\s\S]*?/,
      "]]",
    )),

    glob_pattern: _ => token(seq(
      repeat(/[a-zA-Z0-9_.\/~\-\[\]]/),
      choice("*", "?"),
      repeat(/[a-zA-Z0-9_.\/~\-\[\]*?]/),
    )),

    fd_dup_operator: _ => token(seq(/[0-9]+/, ">&", choice(/[0-9]+/, "-"))),

    preprocessor_directive_argument: _ => token(/[^\r\n]+/),

    line_comment:  _ => token(seq("//", /[^\r\n]*/)),
    block_comment: _ => token(seq("/*", /[\s\S]*?/, "*/")),
  },
});
