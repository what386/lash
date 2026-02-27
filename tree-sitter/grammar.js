module.exports = grammar({
  name: "lash",

  word: $ => $.identifier,

  conflicts: $ => [
    [$.command_statement, $.primary_expression],
    [$.command_statement],
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
      $.enum_declaration,
      $.assignment,
      $.function_declaration,
      $.if_statement,
      $.switch_statement,
      $.for_loop,
      $.while_loop,
      $.subshell_statement,
      $.wait_statement,
      $.sh_statement,
      $.return_statement,
      $.shift_statement,
      $.break_statement,
      $.continue_statement,
      $.command_statement,
      $.expression_statement,
    ),

    sh_statement: $ => seq(
      "sh",
      field("command", $.expression),
    ),

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

    preprocessor_end_directive: _ => "@end",

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

    variable_declaration: $ => seq(
      optional("global"),
      choice("let", "const"),
      field("name", $.identifier),
      optional(seq("=", field("value", $.expression))),
    ),

    assignment: $ => seq(
      optional("global"),
      field("target", choice($.identifier, $.index_access)),
      field("operator", choice("=", "+=")),
      field("value", $.expression),
    ),

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

    // Body runs until the next 'case' or the switch's 'end' — no per-case end needed
    case_clause: $ => seq(
      "case",
      field("pattern", $.expression),
      ":",
      field("body", repeat($.statement)),
    ),

    for_loop: $ => seq(
      "for",
      field("variable", $.identifier),
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

    while_loop: $ => seq(
      "while",
      field("condition", $.expression),
      field("body", $.block),
    ),

    subshell_statement: $ => seq(
      "subshell",
      optional(field("into", $.into_binding)),
      field("body", repeat($.statement)),
      "end",
      optional("&"),
    ),

    wait_statement: $ => prec.right(seq(
      "wait",
      optional(field("target", choice("jobs", $.expression))),
      optional(field("into", $.into_binding)),
    )),

    into_binding: $ => seq(
      "into",
      optional(field("mode", choice("let", "const"))),
      field("name", $.identifier),
    ),

    return_statement: $ => prec.right(seq(
      "return",
      optional(field("value", $.expression)),
    )),

    shift_statement: $ => prec.right(seq(
      "shift",
      optional(field("amount", $.expression)),
    )),

    break_statement: _ => "break",

    continue_statement: _ => "continue",

    // Bare command: identifier followed by one or more shell-style arguments.
    // Disambiguated from expression_statement via GLR conflict declaration above.
    command_statement: $ => seq(
      field("name", $.identifier),
      repeat(field("argument", $.command_argument)),
    ),

    // Command arguments: $var refs and string literals only.
    // bare_word/bash_redirect omitted — they are too permissive without
    // an external scanner enforcing line boundaries.
    command_argument: $ => choice(
      $.var_ref,
      $.interpolated_string,
      $.multiline_string,
      $.string,
      $.number,
    ),

    glob_pattern: _ => token(seq(
      repeat(/[a-zA-Z0-9_./~\-\[\]]/),
      choice("*", "?"),
      repeat(/[a-zA-Z0-9_./~\-\[\]*?]/),
    )),

    expression_statement: $ => $.expression,

    // -------------------------------------------------------------------------
    // Expressions
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
      field("left", $.expression),
      "|",
      field("right", $.expression),
    )),

    fd_dup_expression: $ => prec.left(1, seq(
      field("left", $.expression),
      field("operator", $.fd_dup_operator),
    )),

    redirect_expression: $ => prec.left(1, seq(
      field("left", $.expression),
      field("operator", choice("&>>", "2>>", ">>", "<<<", "&>", "2>", "<>")),
      field("right", $.expression),
    )),

    logical_expression: $ => choice(
      prec.left(2, seq(
        field("left", $.expression),
        "||",
        field("right", $.expression),
      )),
      prec.left(3, seq(
        field("left", $.expression),
        "&&",
        field("right", $.expression),
      )),
    ),

    comparison_expression: $ => prec.left(4, seq(
      field("left", $.expression),
      field("operator", choice("==", "!=", "<=", ">=", "<", ">")),
      field("right", $.expression),
    )),

    range_expression: $ => prec.left(5, seq(
      field("start", $.expression),
      "..",
      field("end", $.expression),
    )),

    additive_expression: $ => prec.left(6, seq(
      field("left", $.expression),
      field("operator", choice("+", "-")),
      field("right", $.expression),
    )),

    multiplicative_expression: $ => prec.left(7, seq(
      field("left", $.expression),
      field("operator", choice("*", "/", "%")),
      field("right", $.expression),
    )),

    unary_expression: $ => prec.right(8, seq(
      field("operator", choice("!", "-", "+", "#")),
      field("operand", $.expression),
    )),

    index_access: $ => prec.left(9, seq(
      field("array", $.expression),
      "[",
      field("index", $.expression),
      "]",
    )),

    primary_expression: $ => choice(
      $.shell_capture_expression,
      $.var_ref,
      $.boolean,
      $.number,
      $.string,
      $.interpolated_string,
      $.multiline_string,
      $.array_literal,
      $.enum_access,
      $.function_call,
      $.identifier,
      seq("(", $.expression, ")"),
    ),

    shell_capture_expression: $ => prec.right(11, seq(
      "$",
      "sh",
      field("command", $.expression),
    )),

    // $ is exclusively a variable sigil — $name always means var_ref
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
      field("enum", $.identifier),
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
    // Literals & terminals
    // -------------------------------------------------------------------------

    boolean: _ => choice("true", "false"),

    // Floats before integers so 3.14 doesn't tokenize as integer '3' + '..' + integer '14'
    number: _ => token(choice(
      /[0-9]+\.[0-9]+/,
      /[0-9]+/,
    )),

    identifier: _ => /[a-zA-Z_][a-zA-Z0-9_]*/,

    string: _ => token(seq(
      '"',
      repeat(choice(/[^"\\r\n]+/, /\\./)),
      '"',
    )),

    interpolated_string: _ => token(seq(
      '$"',
      repeat(choice(/[^"\\{]+/, /\\./, /\{[^}\r\n]*\}/)),
      '"',
    )),

    multiline_string: _ => token(seq(
      "[[",
      /(.|\n|\r)*?/,
      "]]",
    )),

    fd_dup_operator: _ => token(seq(/[0-9]+/, ">&", choice(/[0-9]+/, "-"))),

    preprocessor_directive_argument: _ => token(/[^\r\n]+/),

    line_comment: _ => token(seq("//", /[^\r\n]*/)),

    block_comment: _ => token(seq("/*", /(.|\n|\r)*?/, "*/")),

  },
});
