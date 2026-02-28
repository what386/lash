namespace Lash.Compiler.CodeGen;

using Lash.Compiler.Ast;
using Lash.Compiler.Ast.Statements;

internal sealed partial class StatementGenerator
{
    private readonly BashGenerator owner;

    internal StatementGenerator(BashGenerator owner)
    {
        this.owner = owner;
    }

    internal void GenerateStatement(Statement stmt)
    {
        owner.CurrentContext = stmt.GetType().Name;

        switch (stmt)
        {
            case VariableDeclaration varDecl:
                GenerateVariableDeclaration(varDecl);
                break;

            case Assignment assignment:
                GenerateAssignment(assignment);
                break;

            case FunctionDeclaration funcDecl:
                GenerateFunctionDeclaration(funcDecl);
                break;

            case EnumDeclaration:
                break;

            case IfStatement ifStmt:
                GenerateIfStatement(ifStmt);
                break;

            case SwitchStatement switchStatement:
                GenerateSwitchStatement(switchStatement);
                break;

            case ForLoop forLoop:
                GenerateForLoop(forLoop);
                break;

            case SelectLoop selectLoop:
                GenerateSelectLoop(selectLoop);
                break;

            case WhileLoop whileLoop:
                GenerateWhileLoop(whileLoop);
                break;

            case UntilLoop untilLoop:
                GenerateUntilLoop(untilLoop);
                break;

            case ReturnStatement returnStmt:
                GenerateReturnStatement(returnStmt);
                break;

            case ShiftStatement shiftStatement:
                GenerateShiftStatement(shiftStatement);
                break;

            case SubshellStatement subshellStatement:
                GenerateSubshellStatement(subshellStatement);
                break;

            case CoprocStatement coprocStatement:
                GenerateCoprocStatement(coprocStatement);
                break;

            case WaitStatement waitStatement:
                GenerateWaitStatement(waitStatement);
                break;

            case BreakStatement:
                owner.Emit("break");
                break;

            case ContinueStatement:
                owner.Emit("continue");
                break;

            case ExpressionStatement exprStmt:
                GenerateExpressionStatement(exprStmt.Expression);
                break;

            case ShellStatement shellStmt:
                GenerateShellStatement(shellStmt);
                break;

            case TestStatement testStmt:
                GenerateTestStatement(testStmt);
                break;

            case TrapStatement trapStatement:
                GenerateTrapStatement(trapStatement);
                break;

            case UntrapStatement untrapStatement:
                GenerateUntrapStatement(untrapStatement);
                break;

            case CommandStatement commandStmt:
                owner.Emit(RenderCommandStatement(commandStmt));
                break;

            default:
                owner.EmitComment($"Unsupported statement '{stmt.GetType().Name}'.");
                owner.ReportUnsupported($"statement '{stmt.GetType().Name}'");
                break;
        }
    }

    private static string EscapeCasePattern(string pattern)
    {
        if (pattern.Length == 0)
            return "\"\"";

        if (pattern.IndexOfAny(['"', '`', '$', '\\']) >= 0)
            return $"\"{pattern.Replace("\\", "\\\\", StringComparison.Ordinal).Replace("\"", "\\\"", StringComparison.Ordinal).Replace("$", "\\$", StringComparison.Ordinal).Replace("`", "\\`", StringComparison.Ordinal)}\"";

        return pattern;
    }

    private string HandleUnsupportedExpression(Expression expr, string feature)
    {
        owner.ReportUnsupported(feature);
        return owner.UnsupportedExpression(expr);
    }
}
