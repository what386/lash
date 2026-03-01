namespace Lash.Compiler.Diagnostics;

public static class DiagnosticCodes
{
    // Parse/Lex
    public const string LexInvalidToken = "E000";
    public const string ParseSyntaxError = "E001";
    public const string ParseUnclosedBlockInfo = "I001";

    // Preprocessor
    public const string PreprocessorUnknownDirective = "E010";
    public const string PreprocessorDirectiveSyntax = "E011";
    public const string PreprocessorConditionalStructure = "E012";
    public const string PreprocessorImportIo = "E013";
    public const string PreprocessorImportUsage = "E014";
    public const string PreprocessorRawUsage = "E015";
    public const string PreprocessorWarning = "W010";

    // Name/Declaration/Scope
    public const string InvalidAssignmentTarget = "E110";
    public const string UndeclaredVariable = "E111";
    public const string DuplicateDeclaration = "E112";
    public const string UnknownFunction = "E113";
    public const string FunctionArityMismatch = "E114";
    public const string InvalidControlFlowContext = "E115";
    public const string InvalidParameterDeclaration = "E116";
    public const string InvalidTrapSignal = "E117";
    public const string InvalidTrapHandler = "E118";
    public const string InvalidCommandUsage = "E119";

    // Type/Semantics
    public const string TypeMismatch = "E200";
    public const string InvalidShellPayload = "E201";
    public const string InvalidIndexOrContainerUsage = "E202";

    // Flow/Constant Safety
    public const string MaybeUninitializedVariable = "E300";
    public const string DivisionOrModuloByZero = "E301";
    public const string InvalidShiftAmount = "E302";
    public const string InvalidForStep = "E303";

    // Codegen Feasibility
    public const string UnsupportedExpressionForCodegen = "E400";
    public const string UnsupportedStatementForCodegen = "E401";

    // Warnings
    public const string UnreachableStatement = "W500";
    public const string ShadowedVariable = "W501";
    public const string WaitJobsWithoutTrackedJobs = "W502";
    public const string UnusedVariable = "W503";
    public const string UnusedParameter = "W504";
    public const string UnusedFunction = "W505";
}
