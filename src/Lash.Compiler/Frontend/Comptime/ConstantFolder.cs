namespace Lash.Compiler.Frontend.Comptime;

using System.Text.RegularExpressions;
using Lash.Compiler.Ast;
using Lash.Compiler.Ast.Expressions;
using Lash.Compiler.Ast.Statements;
using Lash.Compiler.Ast.Types;

internal sealed class ConstantFolder
{
    private readonly Dictionary<string, HashSet<string>> enums = new(StringComparer.Ordinal);
    private readonly Stack<Dictionary<string, Binding>> scopes = new();

    public void Fold(ProgramNode program)
    {
        CollectEnums(program.Statements);
        scopes.Push(new Dictionary<string, Binding>(StringComparer.Ordinal));

        foreach (var statement in program.Statements)
            FoldStatement(statement);
    }

    private void CollectEnums(IEnumerable<Statement> statements)
    {
        foreach (var statement in statements)
        {
            switch (statement)
            {
                case EnumDeclaration enumDeclaration:
                    enums[enumDeclaration.Name] = new HashSet<string>(enumDeclaration.Members, StringComparer.Ordinal);
                    break;
                case FunctionDeclaration functionDeclaration:
                    CollectEnums(functionDeclaration.Body);
                    break;
                case IfStatement ifStatement:
                    CollectEnums(ifStatement.ThenBlock);
                    foreach (var elifClause in ifStatement.ElifClauses)
                        CollectEnums(elifClause.Body);
                    CollectEnums(ifStatement.ElseBlock);
                    break;
                case SwitchStatement switchStatement:
                    foreach (var clause in switchStatement.Cases)
                        CollectEnums(clause.Body);
                    break;
                case ForLoop forLoop:
                    CollectEnums(forLoop.Body);
                    break;
                case WhileLoop whileLoop:
                    CollectEnums(whileLoop.Body);
                    break;
            }
        }
    }

    private void FoldStatement(Statement statement)
    {
        switch (statement)
        {
            case VariableDeclaration variableDeclaration:
                variableDeclaration.Value = FoldExpression(variableDeclaration.Value);
                if (variableDeclaration.Kind == VariableDeclaration.VarKind.Const)
                    BindConst(variableDeclaration.Name, variableDeclaration.Value);
                else
                    Unbind(variableDeclaration.Name);
                break;

            case Assignment assignment:
                assignment.Value = FoldExpression(assignment.Value);
                if (assignment.Target is IdentifierExpression target)
                    Unbind(target.Name);
                break;

            case FunctionDeclaration functionDeclaration:
                foreach (var parameter in functionDeclaration.Parameters)
                {
                    if (parameter.DefaultValue != null)
                        parameter.DefaultValue = FoldExpression(parameter.DefaultValue);
                }
                FoldIsolated(functionDeclaration.Body);
                break;

            case IfStatement ifStatement:
                ifStatement.Condition = FoldExpression(ifStatement.Condition);
                FoldIsolated(ifStatement.ThenBlock);
                foreach (var elifClause in ifStatement.ElifClauses)
                {
                    elifClause.Condition = FoldExpression(elifClause.Condition);
                    FoldIsolated(elifClause.Body);
                }
                FoldIsolated(ifStatement.ElseBlock);
                break;

            case SwitchStatement switchStatement:
                switchStatement.Value = FoldExpression(switchStatement.Value);
                foreach (var clause in switchStatement.Cases)
                {
                    clause.Pattern = FoldExpression(clause.Pattern);
                    FoldIsolated(clause.Body);
                }
                break;

            case ForLoop forLoop:
                forLoop.Range = FoldExpression(forLoop.Range);
                if (forLoop.Step != null)
                    forLoop.Step = FoldExpression(forLoop.Step);
                FoldIsolated(forLoop.Body);
                break;

            case WhileLoop whileLoop:
                whileLoop.Condition = FoldExpression(whileLoop.Condition);
                FoldIsolated(whileLoop.Body);
                break;

            case ReturnStatement returnStatement when returnStatement.Value != null:
                returnStatement.Value = FoldExpression(returnStatement.Value);
                break;

            case ShellStatement shellStatement:
                shellStatement.Command = FoldExpression(shellStatement.Command);
                break;

            case ExpressionStatement expressionStatement:
                expressionStatement.Expression = FoldExpression(expressionStatement.Expression);
                break;
        }
    }

    private void FoldIsolated(IEnumerable<Statement> statements)
    {
        var parent = scopes.Peek();
        scopes.Push(new Dictionary<string, Binding>(parent, StringComparer.Ordinal));
        foreach (var statement in statements)
            FoldStatement(statement);
        scopes.Pop();
    }

    private Expression FoldExpression(Expression expression)
    {
        switch (expression)
        {
            case IdentifierExpression identifier:
                return TryResolveLiteral(identifier.Name, out var boundLiteral)
                    ? CloneLiteral(boundLiteral)
                    : expression;

            case EnumAccessExpression enumAccess:
                return FoldEnumAccess(enumAccess);

            case UnaryExpression unary:
                unary.Operand = FoldExpression(unary.Operand);
                return (Expression?)TryFoldUnary(unary) ?? unary;

            case BinaryExpression binary:
                binary.Left = FoldExpression(binary.Left);
                binary.Right = FoldExpression(binary.Right);
                return (Expression?)TryFoldBinary(binary) ?? binary;

            case RedirectExpression redirect:
                redirect.Left = FoldExpression(redirect.Left);
                redirect.Right = FoldExpression(redirect.Right);
                return redirect;

            case PipeExpression pipe:
                pipe.Left = FoldExpression(pipe.Left);
                pipe.Right = FoldExpression(pipe.Right);
                return pipe;

            case IndexAccessExpression indexAccess:
                indexAccess.Array = FoldExpression(indexAccess.Array);
                indexAccess.Index = FoldExpression(indexAccess.Index);
                return indexAccess;

            case ArrayLiteral arrayLiteral:
                for (int i = 0; i < arrayLiteral.Elements.Count; i++)
                    arrayLiteral.Elements[i] = FoldExpression(arrayLiteral.Elements[i]);
                return arrayLiteral;

            case FunctionCallExpression functionCall:
                for (int i = 0; i < functionCall.Arguments.Count; i++)
                    functionCall.Arguments[i] = FoldExpression(functionCall.Arguments[i]);
                return functionCall;

            case ShellCaptureExpression shellCapture:
                shellCapture.Command = FoldExpression(shellCapture.Command);
                return shellCapture;

            case LiteralExpression literal when literal.IsInterpolated:
                return TryFoldInterpolatedString(literal) ?? literal;

            default:
                return expression;
        }
    }

    private Expression FoldEnumAccess(EnumAccessExpression enumAccess)
    {
        if (!enums.TryGetValue(enumAccess.EnumName, out var members))
            return enumAccess;
        if (!members.Contains(enumAccess.MemberName))
            return enumAccess;

        return StringLiteral(enumAccess.EnumName + enumAccess.MemberName);
    }

    private LiteralExpression? TryFoldInterpolatedString(LiteralExpression literal)
    {
        if (literal.Value is not string template)
            return null;

        var output = Regex.Replace(template, @"\{([A-Za-z_][A-Za-z0-9_]*)\}", match =>
        {
            var symbol = match.Groups[1].Value;
            if (!TryResolveLiteral(symbol, out var replacement))
                return match.Value;
            return LiteralToText(replacement);
        });

        if (output == template)
            return null;

        return StringLiteral(output);
    }

    private LiteralExpression? TryFoldUnary(UnaryExpression unary)
    {
        if (unary.Operator == "#" && TryGetArrayLength(unary.Operand, out var arrayLength))
            return NumberLiteral(arrayLength);

        if (unary.Operand is not LiteralExpression literal)
            return null;

        return unary.Operator switch
        {
            "-" when TryGetNumber(literal, out var negValue, out var negWasInt) =>
                NumberLiteral(-negValue, negWasInt),
            "+" when TryGetNumber(literal, out var posValue, out var posWasInt) =>
                NumberLiteral(posValue, posWasInt),
            "!" => BoolLiteral(!ToBoolean(literal)),
            "#" when TryGetStringLength(literal, out var stringLength) =>
                NumberLiteral(stringLength),
            _ => null
        };
    }

    private LiteralExpression? TryFoldBinary(BinaryExpression binary)
    {
        if (binary.Left is not LiteralExpression left || binary.Right is not LiteralExpression right)
            return null;

        if (binary.Operator == "+" && (IsStringLike(left) || IsStringLike(right)))
            return StringLiteral(LiteralToText(left) + LiteralToText(right));

        if (TryGetNumber(left, out var leftNum, out var leftInt) && TryGetNumber(right, out var rightNum, out var rightInt))
        {
            switch (binary.Operator)
            {
                case "+":
                    return NumberLiteral(leftNum + rightNum, leftInt && rightInt);
                case "-":
                    return NumberLiteral(leftNum - rightNum, leftInt && rightInt);
                case "*":
                    return NumberLiteral(leftNum * rightNum, leftInt && rightInt);
                case "/" when rightNum != 0:
                    return NumberLiteral(leftNum / rightNum, false);
                case "%" when rightNum != 0:
                    return NumberLiteral(leftNum % rightNum, leftInt && rightInt);
                case "==":
                    return BoolLiteral(leftNum == rightNum);
                case "!=":
                    return BoolLiteral(leftNum != rightNum);
                case "<":
                    return BoolLiteral(leftNum < rightNum);
                case ">":
                    return BoolLiteral(leftNum > rightNum);
                case "<=":
                    return BoolLiteral(leftNum <= rightNum);
                case ">=":
                    return BoolLiteral(leftNum >= rightNum);
                case "&&":
                    return BoolLiteral((leftNum != 0) && (rightNum != 0));
                case "||":
                    return BoolLiteral((leftNum != 0) || (rightNum != 0));
            }
        }

        if (binary.Operator is "==" or "!=" && IsStringLike(left) && IsStringLike(right))
        {
            var equals = string.Equals(LiteralToText(left), LiteralToText(right), StringComparison.Ordinal);
            return BoolLiteral(binary.Operator == "==" ? equals : !equals);
        }

        return null;
    }

    private void BindConst(string name, Expression expression)
    {
        if (expression is LiteralExpression literal)
        {
            scopes.Peek()[name] = new Binding(CloneLiteral(literal), null);
            return;
        }

        if (expression is ArrayLiteral arrayLiteral)
        {
            scopes.Peek()[name] = new Binding(null, arrayLiteral.Elements.Count);
            return;
        }

        scopes.Peek().Remove(name);
    }

    private void Unbind(string name)
    {
        scopes.Peek().Remove(name);
    }

    private bool TryResolveLiteral(string name, out LiteralExpression literal)
    {
        foreach (var scope in scopes)
        {
            if (scope.TryGetValue(name, out var binding) && binding.Literal != null)
            {
                literal = binding.Literal;
                return true;
            }
        }

        literal = null!;
        return false;
    }

    private bool TryGetArrayLength(Expression expression, out int length)
    {
        if (expression is ArrayLiteral arrayLiteral)
        {
            length = arrayLiteral.Elements.Count;
            return true;
        }

        if (expression is IdentifierExpression identifier)
        {
            foreach (var scope in scopes)
            {
                if (scope.TryGetValue(identifier.Name, out var binding) && binding.ArrayLength.HasValue)
                {
                    length = binding.ArrayLength.Value;
                    return true;
                }
            }
        }

        if (expression is LiteralExpression literal && TryGetStringLength(literal, out var stringLength))
        {
            length = stringLength;
            return true;
        }

        length = 0;
        return false;
    }

    private static bool TryGetStringLength(LiteralExpression literal, out int length)
    {
        if (!IsStringLike(literal))
        {
            length = 0;
            return false;
        }

        length = LiteralToText(literal).Length;
        return true;
    }

    private static bool TryGetNumber(LiteralExpression literal, out double number, out bool isInt)
    {
        isInt = literal.LiteralType.PrimitiveKind == PrimitiveType.Kind.Int;

        switch (literal.LiteralType.PrimitiveKind)
        {
            case PrimitiveType.Kind.Int when literal.Value is int i:
                number = i;
                return true;
            case PrimitiveType.Kind.Bool when literal.Value is bool b:
                number = b ? 1 : 0;
                isInt = true;
                return true;
            default:
                number = 0;
                return false;
        }
    }

    private static bool IsStringLike(LiteralExpression literal)
    {
        return literal.LiteralType.PrimitiveKind == PrimitiveType.Kind.String;
    }

    private static bool ToBoolean(LiteralExpression literal)
    {
        if (literal.LiteralType.PrimitiveKind == PrimitiveType.Kind.Bool && literal.Value is bool b)
            return b;
        if (TryGetNumber(literal, out var numeric, out _))
            return numeric != 0;
        return !string.IsNullOrEmpty(LiteralToText(literal));
    }

    private static string LiteralToText(LiteralExpression literal)
    {
        return literal.LiteralType.PrimitiveKind switch
        {
            PrimitiveType.Kind.String => literal.Value?.ToString() ?? string.Empty,
            PrimitiveType.Kind.Bool => (literal.Value is bool b && b) ? "true" : "false",
            _ => literal.Value?.ToString() ?? string.Empty
        };
    }

    private static LiteralExpression CloneLiteral(LiteralExpression literal)
    {
        return new LiteralExpression
        {
            Line = literal.Line,
            Column = literal.Column,
            Value = literal.Value,
            LiteralType = new PrimitiveType { PrimitiveKind = literal.LiteralType.PrimitiveKind },
            Type = literal.Type,
            IsInterpolated = literal.IsInterpolated,
            IsMultiline = literal.IsMultiline
        };
    }

    private static LiteralExpression NumberLiteral(double value, bool isInt = true)
    {
        _ = isInt;
        return new LiteralExpression
        {
            Value = (int)value,
            LiteralType = new PrimitiveType { PrimitiveKind = PrimitiveType.Kind.Int },
            Type = ExpressionTypes.Number
        };
    }

    private static LiteralExpression StringLiteral(string value)
    {
        return new LiteralExpression
        {
            Value = value,
            LiteralType = new PrimitiveType { PrimitiveKind = PrimitiveType.Kind.String },
            Type = ExpressionTypes.String
        };
    }

    private static LiteralExpression BoolLiteral(bool value)
    {
        return new LiteralExpression
        {
            Value = value,
            LiteralType = new PrimitiveType { PrimitiveKind = PrimitiveType.Kind.Bool },
            Type = ExpressionTypes.Bool
        };
    }

    private sealed record Binding(LiteralExpression? Literal, int? ArrayLength);
}
