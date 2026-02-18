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
    | whileLoop
    | shStatement
    | commandStatement
    | returnStatement
    | shiftStatement
    | subshellStatement
    | waitStatement
    | breakStatement
    | continueStatement
    | expressionStatement
    ;

shStatement
    : 'sh' expression
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
    : 'for' IDENTIFIER 'in' expression ('step' expression)? statement* 'end'
    ;

whileLoop
    : 'while' expression statement* 'end'
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
    : 'subshell' ('into' IDENTIFIER)? statement* 'end' AMP?
    ;

waitStatement
    : 'wait' waitTarget? ('into' IDENTIFIER)?
    ;

waitTarget
    : 'jobs'
    | expression
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
    | expression (APPEND | ERR_APPEND | BOTH_APPEND | HERE_STRING | ERR_REDIRECT | BOTH_REDIRECT | READ_WRITE_REDIRECT) expression  # RedirectExpr
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
    | shellCaptureExpression
    | functionCall
    | IDENTIFIER
    | arrayLiteral
    | '(' expression ')'
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

shellCaptureExpression
    : '$' 'sh' expression
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
