grammar Lash;

program
    : statement* EOF
    ;

statement
    : variableDeclaration
    | enumDeclaration
    | assignment
    | functionDeclaration
    | ifStatement
    | switchStatement
    | forLoop
    | selectLoop
    | whileLoop
    | untilLoop
    | shStatement
    | testStatement
    | trapStatement
    | untrapStatement
    | returnStatement
    | shiftStatement
    | subshellStatement
    | coprocStatement
    | waitStatement
    | breakStatement
    | continueStatement
    | commandStatement
    | expressionStatement
    ;

shStatement
    : 'sh' expression
    ;

testStatement
    : 'test' expression
    ;

trapStatement
    : 'trap' IDENTIFIER ('into' functionCall | expression)
    ;

untrapStatement
    : 'untrap' IDENTIFIER
    ;

commandStatement
    : COMMAND_STATEMENT
    ;

variableDeclaration
    : 'global'? ('let' | 'const') IDENTIFIER ('=' expression)?
    ;

enumDeclaration
    : 'enum' IDENTIFIER enumMember* 'end'
    ;

enumMember
    : IDENTIFIER
    ;

assignment
    : 'global'? (IDENTIFIER | indexAccess) ('=' | ADD_ASSIGN) expression
    ;

functionDeclaration
    : 'fn' IDENTIFIER '(' parameterList? ')' functionBody
    ;

parameterList
    : parameter (',' parameter)*
    ;

parameter
    : IDENTIFIER ('=' expression)?
    ;

functionBody
    : statement* 'end'
    ;

ifStatement
    : 'if' expression ifBlock elifClause* elseClause? 'end'
    ;

ifBlock
    : statement*
    ;

elifClause
    : 'elif' expression ifBlock
    ;

elseClause
    : 'else' ifBlock
    ;

forLoop
    : 'for' IDENTIFIER 'in' (expression ('step' expression)? | GLOB_PATTERN) statement* 'end'
    ;

selectLoop
    : 'select' IDENTIFIER 'in' (expression | GLOB_PATTERN) statement* 'end'
    ;

whileLoop
    : 'while' expression statement* 'end'
    ;

untilLoop
    : 'until' expression statement* 'end'
    ;

switchStatement
    : 'switch' expression switchCaseClause+ 'end'
    ;

switchCaseClause
    : 'case' expression ':' statement*
    ;

returnStatement
    : 'return' expression?
    ;

shiftStatement
    : 'shift' expression?
    ;

subshellStatement
    : 'subshell' intoBinding? statement* 'end' AMP?
    ;

coprocStatement
    : 'coproc' intoBinding? statement* 'end'
    ;

waitStatement
    : 'wait' waitTarget? intoBinding?
    ;

waitTarget
    : 'jobs'
    | expression
    ;

intoBinding
    : 'into' variableReference
    | 'into' 'let' IDENTIFIER
    | 'into' 'const' IDENTIFIER
    ;

breakStatement
    : 'break'
    ;

continueStatement
    : 'continue'
    ;

expressionStatement
    : expression
    ;

expression
    : primaryExpression                                          # PrimaryExpr
    | expression '|' expression                                  # PipeExpr
    | expression FD_DUP                                          # FdDupExpr
    | expression (APPEND | ERR_APPEND | BOTH_APPEND | HERE_STRING | HEREDOC | ERR_REDIRECT | BOTH_REDIRECT | READ_WRITE_REDIRECT) expression  # RedirectExpr
    | expression '[' expression ']'                              # IndexAccessExpr
    | ('!' | '-' | '+' | '#') expression                         # UnaryExpr
    | expression ('*' | '/' | '%') expression                    # MultiplicativeExpr
    | expression ('+' | '-') expression                          # AdditiveExpr
    | expression ('..' ) expression                              # RangeExpr
    | expression ('==' | '!=' | '<' | '>' | '<=' | '>=') expression # ComparisonExpr
    | expression ('&&' | '||') expression                        # LogicalExpr
    ;

primaryExpression
    : literal
    | enumAccess
    | variableReference
    | functionCall
    | arrayLiteral
    | '(' expression ')'
    ;

variableReference
    : '$' IDENTIFIER
    ;

arrayLiteral
    : '[' (expression (',' expression)*)? ']'
    ;

argumentList
    : expression (',' expression)*
    ;

functionCall
    : IDENTIFIER '(' argumentList? ')'
    ;

enumAccess
    : IDENTIFIER '::' IDENTIFIER
    ;

indexAccess
    : expression '[' expression ']'
    ;

literal
    : INTEGER
    | stringLiteral
    | BOOLEAN
    ;

stringLiteral
    : STRING
    | INTERPOLATED_STRING
    | MULTILINE_STRING
    ;

BOOLEAN
    : 'true'
    | 'false'
    ;

BOTH_APPEND
    : '&>>'
    ;

ADD_ASSIGN
    : '+='
    ;

ERR_APPEND
    : '2>>'
    ;

HERE_STRING
    : '<<<'
    ;

HEREDOC
    : '<<'
    ;

BOTH_REDIRECT
    : '&>'
    ;

ERR_REDIRECT
    : '2>'
    ;

READ_WRITE_REDIRECT
    : '<>'
    ;

FD_DUP
    : [0-9]+ '>&' ([0-9]+ | '-')
    ;

AMP
    : '&'
    ;

APPEND
    : '>>'
    ;

IDENTIFIER
    : [a-zA-Z_][a-zA-Z0-9_]*
    ;

INTEGER
    : [0-9]+
    ;

STRING
    : '"' (~["\\\r\n] | '\\' .)* '"'
    ;

INTERPOLATED_STRING
    : '$"' ( ~["{\\] | '\\' . | '{' ~[}]* '}' )* '"'
    ;

MULTILINE_STRING
    : '[[' .*? ']]'
    ;

GLOB_PATTERN
    : GLOB_CHAR* GLOB_META GLOB_CHAR*
    ;

fragment GLOB_CHAR
    : [a-zA-Z0-9_./~]
    | '-'
    | '['
    | ']'
    ;

fragment GLOB_META
    : [*?]
    ;

COMMAND_STATEMENT
    : '__cmd' [ \t]+ ~[\r\n]+
    ;

WS
    : [ \t\r\n]+ -> skip
    ;

LINE_COMMENT
    : '//' ~[\r\n]* -> skip
    ;

BLOCK_COMMENT
    : '/*' .*? '*/' -> skip
    ;
