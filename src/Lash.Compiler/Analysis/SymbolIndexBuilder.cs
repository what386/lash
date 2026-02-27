namespace Lash.Compiler.Analysis;

using Lash.Compiler.Ast;
using Lash.Compiler.Ast.Expressions;
using Lash.Compiler.Ast.Statements;

internal sealed class SymbolIndexBuilder
{
    private sealed class Scope
    {
        public Scope(Scope? parent)
        {
            Parent = parent;
        }

        public Scope? Parent { get; }
        public Dictionary<string, SymbolInfo> Symbols { get; } = new(StringComparer.Ordinal);

        public SymbolInfo? Lookup(string name)
        {
            for (var current = this; current is not null; current = current.Parent)
            {
                if (current.Symbols.TryGetValue(name, out var symbol))
                    return symbol;
            }

            return null;
        }
    }

    private readonly List<SymbolInfo> declarations = new();
    private readonly List<SymbolReference> references = new();
    private readonly Dictionary<AstNode, SymbolInfo> declarationByNode = new();
    private Scope currentScope = new(parent: null);

    public SymbolIndex Build(ProgramNode program)
    {
        currentScope = new Scope(parent: null);
        declarations.Clear();
        references.Clear();
        declarationByNode.Clear();

        Predeclare(program.Statements);
        VisitStatements(program.Statements);

        return new SymbolIndex
        {
            Declarations = declarations,
            References = references
        };
    }

    private void VisitStatements(IEnumerable<Statement> statements)
    {
        foreach (var statement in statements)
            VisitStatement(statement);
    }

    private void VisitStatement(Statement statement)
    {
        switch (statement)
        {
            case VariableDeclaration variable:
                VisitExpression(variable.Value);
                DeclareVariable(variable);
                break;
            case Assignment assignment:
                VisitExpression(assignment.Target);
                VisitExpression(assignment.Value);
                break;
            case FunctionDeclaration function:
                VisitFunction(function);
                break;
            case EnumDeclaration:
                // already predeclared
                break;
            case IfStatement ifStatement:
                VisitExpression(ifStatement.Condition);
                VisitScopedBlock(ifStatement.ThenBlock);
                foreach (var clause in ifStatement.ElifClauses)
                {
                    VisitExpression(clause.Condition);
                    VisitScopedBlock(clause.Body);
                }
                VisitScopedBlock(ifStatement.ElseBlock);
                break;
            case SwitchStatement switchStatement:
                VisitExpression(switchStatement.Value);
                foreach (var clause in switchStatement.Cases)
                {
                    VisitExpression(clause.Pattern);
                    VisitScopedBlock(clause.Body);
                }
                break;
            case ForLoop forLoop:
                VisitExpression(forLoop.Range);
                if (forLoop.Step is not null)
                    VisitExpression(forLoop.Step);
                PushScope();
                Declare(
                    forLoop,
                    forLoop.Variable,
                    LashSymbolKind.Variable,
                    isConst: false,
                    typeText: "number");
                Predeclare(forLoop.Body);
                VisitStatements(forLoop.Body);
                PopScope();
                break;
            case WhileLoop whileLoop:
                VisitExpression(whileLoop.Condition);
                VisitScopedBlock(whileLoop.Body);
                break;
            case ReturnStatement returnStatement:
                if (returnStatement.Value is not null)
                    VisitExpression(returnStatement.Value);
                break;
            case ShiftStatement shiftStatement:
                if (shiftStatement.Amount is not null)
                    VisitExpression(shiftStatement.Amount);
                break;
            case SubshellStatement subshellStatement:
                if (subshellStatement.IntoVariable is not null)
                {
                    AddReference(
                        subshellStatement.IntoVariable,
                        subshellStatement.Line,
                        subshellStatement.Column);
                }
                VisitScopedBlock(subshellStatement.Body);
                break;
            case WaitStatement waitStatement:
                if (waitStatement.Target is not null)
                    VisitExpression(waitStatement.Target);
                if (waitStatement.IntoVariable is not null)
                    AddReference(waitStatement.IntoVariable, waitStatement.Line, waitStatement.Column);
                break;
            case ExpressionStatement expressionStatement:
                VisitExpression(expressionStatement.Expression);
                break;
            case ShellStatement shellStatement:
                VisitExpression(shellStatement.Command);
                break;
            case CommandStatement:
            case BreakStatement:
            case ContinueStatement:
                break;
        }
    }

    private void VisitFunction(FunctionDeclaration function)
    {
        if (!declarationByNode.TryGetValue(function, out _))
        {
            Declare(
                function,
                function.Name,
                LashSymbolKind.Function,
                isConst: true,
                typeText: "fn");
        }

        PushScope();

        foreach (var parameter in function.Parameters)
        {
            Declare(
                parameter,
                parameter.Name,
                LashSymbolKind.Parameter,
                isConst: false,
                typeText: "param");
        }

        Predeclare(function.Body);
        VisitStatements(function.Body);
        PopScope();
    }

    private void VisitScopedBlock(IEnumerable<Statement> statements)
    {
        PushScope();
        Predeclare(statements);
        VisitStatements(statements);
        PopScope();
    }

    private void VisitExpression(Expression expression)
    {
        switch (expression)
        {
            case IdentifierExpression identifier:
                AddReference(identifier.Name, identifier.Line, identifier.Column);
                break;
            case FunctionCallExpression call:
                AddReference(call.FunctionName, call.Line, call.Column);
                foreach (var arg in call.Arguments)
                    VisitExpression(arg);
                break;
            case EnumAccessExpression enumAccess:
                AddReference(enumAccess.EnumName, enumAccess.Line, enumAccess.Column);
                break;
            case BinaryExpression binary:
                VisitExpression(binary.Left);
                VisitExpression(binary.Right);
                break;
            case UnaryExpression unary:
                VisitExpression(unary.Operand);
                break;
            case RangeExpression range:
                VisitExpression(range.Start);
                VisitExpression(range.End);
                break;
            case PipeExpression pipe:
                VisitExpression(pipe.Left);
                VisitExpression(pipe.Right);
                break;
            case RedirectExpression redirect:
                VisitExpression(redirect.Left);
                VisitExpression(redirect.Right);
                break;
            case ShellCaptureExpression shellCapture:
                VisitExpression(shellCapture.Command);
                break;
            case IndexAccessExpression indexAccess:
                VisitExpression(indexAccess.Array);
                VisitExpression(indexAccess.Index);
                break;
            case ArrayLiteral arrayLiteral:
                foreach (var element in arrayLiteral.Elements)
                    VisitExpression(element);
                break;
            case LiteralExpression:
            case NullLiteral:
                break;
        }
    }

    private void DeclareVariable(VariableDeclaration variable)
    {
        Declare(
            variable,
            variable.Name,
            variable.Kind == VariableDeclaration.VarKind.Const ? LashSymbolKind.Constant : LashSymbolKind.Variable,
            variable.Kind == VariableDeclaration.VarKind.Const,
            typeText: variable.Value.Type.GetType().Name.Replace("Type", string.Empty, StringComparison.Ordinal));
    }

    private void Predeclare(IEnumerable<Statement> statements)
    {
        foreach (var statement in statements)
        {
            switch (statement)
            {
                case FunctionDeclaration function when !declarationByNode.ContainsKey(function):
                    Declare(function, function.Name, LashSymbolKind.Function, isConst: true, typeText: "fn");
                    break;
                case EnumDeclaration enumDeclaration when !declarationByNode.ContainsKey(enumDeclaration):
                    Declare(enumDeclaration, enumDeclaration.Name, LashSymbolKind.Enum, isConst: true, typeText: "enum");
                    break;
            }
        }
    }

    private void Declare(
        AstNode node,
        string name,
        LashSymbolKind kind,
        bool isConst,
        string? typeText)
    {
        var span = CreateSpan(node.Line, node.Column, name);
        var symbol = new SymbolInfo(name, kind, span, isConst, typeText);
        currentScope.Symbols[name] = symbol;
        declarations.Add(symbol);
        declarationByNode[node] = symbol;
    }

    private void AddReference(string name, int line, int column)
    {
        var span = CreateSpan(line, column, name);
        var resolved = currentScope.Lookup(name);
        references.Add(new SymbolReference(name, span, resolved));
    }

    private static SymbolSpan CreateSpan(int line, int column, string token)
    {
        var length = Math.Max(1, token.Length);
        return new SymbolSpan(line, Math.Max(0, column), Math.Max(0, column) + length);
    }

    private void PushScope() => currentScope = new Scope(currentScope);

    private void PopScope()
    {
        if (currentScope.Parent is not null)
            currentScope = currentScope.Parent;
    }
}
