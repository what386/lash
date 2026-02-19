namespace Lash.Compiler.Frontend.Comptime;

using System.Text.RegularExpressions;
using Lash.Compiler.Ast;
using Lash.Compiler.Ast.Expressions;
using Lash.Compiler.Ast.Statements;
using Lash.Compiler.Ast.Types;

internal sealed class ConstantFolder
{
    private const int MaxPureFoldDepth = 8;

    private readonly Dictionary<string, HashSet<string>> enums = new(StringComparer.Ordinal);
    private readonly Stack<Dictionary<string, Binding>> scopes = new();
    private readonly Dictionary<string, PureFunctionDefinition> pureCandidates = new(StringComparer.Ordinal);
    private readonly Dictionary<string, PureFunctionDefinition> pureFunctions = new(StringComparer.Ordinal);

    public void Fold(ProgramNode program)
    {
        CollectEnums(program.Statements);
        CollectPureFunctionCandidates(program.Statements);
        ResolvePureFunctions();

        scopes.Push(new Dictionary<string, Binding>(StringComparer.Ordinal));
        program.Statements = FoldStatementsInternal(program.Statements);
    }

    private List<Statement> FoldStatementsInternal(IEnumerable<Statement> statements)
    {
        var output = new List<Statement>();
        var terminated = false;
        foreach (var statement in statements)
        {
            if (terminated)
                break;

            var folded = FoldStatement(statement);
            output.AddRange(folded);
            terminated = IsBlockTerminated(folded);
        }

        return output;
    }

    private static bool IsBlockTerminated(IReadOnlyList<Statement> statements)
    {
        if (statements.Count == 0)
            return false;

        return IsTerminatingStatement(statements[^1]);
    }

    private static bool IsTerminatingStatement(Statement statement)
    {
        return statement switch
        {
            ReturnStatement => true,
            BreakStatement => true,
            ContinueStatement => true,
            IfStatement ifStatement => IfTerminates(ifStatement),
            _ => false
        };
    }

    private static bool IfTerminates(IfStatement ifStatement)
    {
        if (ifStatement.ElseBlock.Count == 0)
            return false;

        if (!IsBlockTerminated(ifStatement.ThenBlock))
            return false;

        if (ifStatement.ElifClauses.Any(clause => !IsBlockTerminated(clause.Body)))
            return false;

        return IsBlockTerminated(ifStatement.ElseBlock);
    }

    private List<Statement> FoldStatement(Statement statement)
    {
        switch (statement)
        {
            case VariableDeclaration variableDeclaration:
                variableDeclaration.Value = FoldExpression(variableDeclaration.Value);
                if (variableDeclaration.Kind == VariableDeclaration.VarKind.Const)
                    BindConst(variableDeclaration.Name, variableDeclaration.Value);
                else
                    Unbind(variableDeclaration.Name);
                return new List<Statement> { variableDeclaration };

            case Assignment assignment:
                assignment.Value = FoldExpression(assignment.Value);
                if (assignment.Target is IdentifierExpression targetIdentifier)
                {
                    Unbind(targetIdentifier.Name);
                }
                else if (assignment.Target is IndexAccessExpression indexTarget && indexTarget.Array is IdentifierExpression arrayIdentifier)
                {
                    Unbind(arrayIdentifier.Name);
                }
                return new List<Statement> { assignment };

            case FunctionDeclaration functionDeclaration:
                foreach (var parameter in functionDeclaration.Parameters)
                {
                    if (parameter.DefaultValue != null)
                        parameter.DefaultValue = FoldExpression(parameter.DefaultValue);
                }
                functionDeclaration.Body = FoldIsolated(functionDeclaration.Body);
                return new List<Statement> { functionDeclaration };

            case IfStatement ifStatement:
                return FoldIfStatement(ifStatement);

            case SwitchStatement switchStatement:
                return FoldSwitchStatement(switchStatement);

            case ForLoop forLoop:
                forLoop.Range = FoldExpression(forLoop.Range);
                if (forLoop.Step != null)
                    forLoop.Step = FoldExpression(forLoop.Step);
                forLoop.Body = FoldIsolated(forLoop.Body);
                return new List<Statement> { forLoop };

            case WhileLoop whileLoop:
                whileLoop.Condition = FoldExpression(whileLoop.Condition);
                if (TryEvaluateCondition(whileLoop.Condition, out var whileCondition) && !whileCondition)
                    return new List<Statement>();
                whileLoop.Body = FoldIsolated(whileLoop.Body);
                return new List<Statement> { whileLoop };

            case ReturnStatement returnStatement when returnStatement.Value != null:
                returnStatement.Value = FoldExpression(returnStatement.Value);
                return new List<Statement> { returnStatement };

            case ShiftStatement shiftStatement when shiftStatement.Amount != null:
                shiftStatement.Amount = FoldExpression(shiftStatement.Amount);
                return new List<Statement> { shiftStatement };

            case SubshellStatement subshellStatement:
                subshellStatement.Body = FoldIsolated(subshellStatement.Body);
                return new List<Statement> { subshellStatement };

            case WaitStatement waitStatement when waitStatement.TargetKind == WaitTargetKind.Target && waitStatement.Target != null:
                waitStatement.Target = FoldExpression(waitStatement.Target);
                return new List<Statement> { waitStatement };

            case ShellStatement shellStatement:
                shellStatement.Command = FoldExpression(shellStatement.Command);
                return new List<Statement> { shellStatement };

            case ExpressionStatement expressionStatement:
                expressionStatement.Expression = FoldExpression(expressionStatement.Expression);
                return new List<Statement> { expressionStatement };

            default:
                return new List<Statement> { statement };
        }
    }

    private List<Statement> FoldIfStatement(IfStatement ifStatement)
    {
        ifStatement.Condition = FoldExpression(ifStatement.Condition);
        if (TryEvaluateCondition(ifStatement.Condition, out var conditionValue))
        {
            if (conditionValue)
                return FoldIsolated(ifStatement.ThenBlock);

            for (int i = 0; i < ifStatement.ElifClauses.Count; i++)
            {
                var clause = ifStatement.ElifClauses[i];
                clause.Condition = FoldExpression(clause.Condition);
                if (!TryEvaluateCondition(clause.Condition, out var clauseValue))
                {
                    ifStatement.Condition = clause.Condition;
                    ifStatement.ThenBlock = FoldIsolated(clause.Body);

                    var remaining = new List<ElifClause>();
                    for (int j = i + 1; j < ifStatement.ElifClauses.Count; j++)
                    {
                        var later = ifStatement.ElifClauses[j];
                        later.Condition = FoldExpression(later.Condition);
                        later.Body = FoldIsolated(later.Body);
                        remaining.Add(later);
                    }

                    ifStatement.ElifClauses = remaining;
                    ifStatement.ElseBlock = FoldIsolated(ifStatement.ElseBlock);
                    return new List<Statement> { ifStatement };
                }

                if (clauseValue)
                    return FoldIsolated(clause.Body);
            }

            return FoldIsolated(ifStatement.ElseBlock);
        }

        ifStatement.ThenBlock = FoldIsolated(ifStatement.ThenBlock);
        foreach (var clause in ifStatement.ElifClauses)
        {
            clause.Condition = FoldExpression(clause.Condition);
            clause.Body = FoldIsolated(clause.Body);
        }
        ifStatement.ElseBlock = FoldIsolated(ifStatement.ElseBlock);
        return new List<Statement> { ifStatement };
    }

    private List<Statement> FoldSwitchStatement(SwitchStatement switchStatement)
    {
        switchStatement.Value = FoldExpression(switchStatement.Value);
        if (!TryEvaluateLiteral(switchStatement.Value, out var switchLiteral))
        {
            foreach (var clause in switchStatement.Cases)
            {
                clause.Pattern = FoldExpression(clause.Pattern);
                clause.Body = FoldIsolated(clause.Body);
            }

            return new List<Statement> { switchStatement };
        }

        for (int i = 0; i < switchStatement.Cases.Count; i++)
        {
            var clause = switchStatement.Cases[i];
            clause.Pattern = FoldExpression(clause.Pattern);
            clause.Body = FoldIsolated(clause.Body);

            if (!TryEvaluateLiteral(clause.Pattern, out var patternLiteral) || !IsExactCasePattern(clause.Pattern))
            {
                var remaining = new List<SwitchCaseClause>();
                remaining.Add(clause);
                for (int j = i + 1; j < switchStatement.Cases.Count; j++)
                {
                    var later = switchStatement.Cases[j];
                    later.Pattern = FoldExpression(later.Pattern);
                    later.Body = FoldIsolated(later.Body);
                    remaining.Add(later);
                }

                switchStatement.Cases = remaining;
                return new List<Statement> { switchStatement };
            }

            if (AreLiteralsEqual(switchLiteral, patternLiteral))
                return clause.Body;
        }

        return new List<Statement>();
    }

    private List<Statement> FoldIsolated(IEnumerable<Statement> statements)
    {
        var parent = scopes.Peek();
        scopes.Push(new Dictionary<string, Binding>(parent, StringComparer.Ordinal));
        var folded = FoldStatementsInternal(statements);
        scopes.Pop();
        return folded;
    }

    private Expression FoldExpression(Expression expression, int pureDepth = 0)
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
                unary.Operand = FoldExpression(unary.Operand, pureDepth);
                return (Expression?)TryFoldUnary(unary) ?? unary;

            case BinaryExpression binary:
                binary.Left = FoldExpression(binary.Left, pureDepth);
                binary.Right = FoldExpression(binary.Right, pureDepth);
                return (Expression?)TryFoldBinary(binary) ?? binary;

            case RedirectExpression redirect:
                redirect.Left = FoldExpression(redirect.Left, pureDepth);
                redirect.Right = FoldExpression(redirect.Right, pureDepth);
                return redirect;

            case PipeExpression pipe:
                pipe.Left = FoldExpression(pipe.Left, pureDepth);
                pipe.Right = FoldExpression(pipe.Right, pureDepth);
                return pipe;

            case IndexAccessExpression indexAccess:
                indexAccess.Array = FoldExpression(indexAccess.Array, pureDepth);
                indexAccess.Index = FoldExpression(indexAccess.Index, pureDepth);
                return (Expression?)TryFoldIndexAccess(indexAccess) ?? indexAccess;

            case ArrayLiteral arrayLiteral:
                for (int i = 0; i < arrayLiteral.Elements.Count; i++)
                    arrayLiteral.Elements[i] = FoldExpression(arrayLiteral.Elements[i], pureDepth);
                return arrayLiteral;

            case FunctionCallExpression functionCall:
                for (int i = 0; i < functionCall.Arguments.Count; i++)
                    functionCall.Arguments[i] = FoldExpression(functionCall.Arguments[i], pureDepth);

                if (TryFoldPureFunctionCall(functionCall, pureDepth, out var foldedPureCall))
                    return foldedPureCall;
                return functionCall;

            case ShellCaptureExpression shellCapture:
                shellCapture.Command = FoldExpression(shellCapture.Command, pureDepth);
                return shellCapture;

            case LiteralExpression literal when literal.IsInterpolated:
                return TryFoldInterpolatedString(literal) ?? literal;

            default:
                return expression;
        }
    }

    private bool TryFoldPureFunctionCall(FunctionCallExpression functionCall, int pureDepth, out LiteralExpression literal)
    {
        literal = null!;
        if (pureDepth >= MaxPureFoldDepth)
            return false;

        if (!pureFunctions.TryGetValue(functionCall.FunctionName, out var pureFunction))
            return false;

        var callBindings = new Dictionary<string, Binding>(StringComparer.Ordinal);
        for (int i = 0; i < pureFunction.Parameters.Count; i++)
        {
            var parameter = pureFunction.Parameters[i];
            Expression argumentExpression;

            if (i < functionCall.Arguments.Count)
            {
                argumentExpression = functionCall.Arguments[i];
            }
            else
            {
                if (parameter.DefaultValue == null)
                    return false;

                var defaultScope = new Dictionary<string, Binding>(scopes.Peek(), StringComparer.Ordinal);
                foreach (var binding in callBindings)
                    defaultScope[binding.Key] = binding.Value;

                scopes.Push(defaultScope);
                argumentExpression = FoldExpression(CloneExpression(parameter.DefaultValue), pureDepth + 1);
                scopes.Pop();
            }

            if (argumentExpression is LiteralExpression literalArgument)
            {
                callBindings[parameter.Name] = new Binding(CloneLiteral(literalArgument), null, null);
                continue;
            }

            if (argumentExpression is ArrayLiteral arrayArgument)
            {
                callBindings[parameter.Name] = new Binding(null, CloneArrayLiteral(arrayArgument), arrayArgument.Elements.Count);
                continue;
            }

            return false;
        }

        var evalScope = new Dictionary<string, Binding>(scopes.Peek(), StringComparer.Ordinal);
        foreach (var binding in callBindings)
            evalScope[binding.Key] = binding.Value;

        scopes.Push(evalScope);
        var foldedReturn = FoldExpression(CloneExpression(pureFunction.ReturnExpression), pureDepth + 1);
        scopes.Pop();

        if (foldedReturn is not LiteralExpression foldedLiteral)
            return false;

        literal = CloneLiteral(foldedLiteral);
        return true;
    }

    private Expression? TryFoldIndexAccess(IndexAccessExpression indexAccess)
    {
        if (!TryGetIntIndex(indexAccess.Index, out var index))
            return null;
        if (index < 0)
            return null;

        if (indexAccess.Array is ArrayLiteral arrayLiteral)
        {
            if (index >= arrayLiteral.Elements.Count)
                return null;
            if (arrayLiteral.Elements[index] is LiteralExpression arrayElementLiteral)
                return CloneLiteral(arrayElementLiteral);
            return null;
        }

        if (indexAccess.Array is IdentifierExpression arrayIdentifier &&
            TryResolveArray(arrayIdentifier.Name, out var boundArray))
        {
            if (index >= boundArray.Elements.Count)
                return null;
            if (boundArray.Elements[index] is LiteralExpression boundLiteral)
                return CloneLiteral(boundLiteral);
            return null;
        }

        if (indexAccess.Array is LiteralExpression stringLiteral &&
            stringLiteral.LiteralType is PrimitiveType { PrimitiveKind: PrimitiveType.Kind.String } &&
            stringLiteral.Value is string text &&
            index < text.Length)
        {
            return StringLiteral(text[index].ToString(), indexAccess.Line, indexAccess.Column);
        }

        return null;
    }

    private static bool TryGetIntIndex(Expression expression, out int index)
    {
        if (expression is LiteralExpression literal &&
            literal.LiteralType is PrimitiveType { PrimitiveKind: PrimitiveType.Kind.Int } &&
            literal.Value is int intValue)
        {
            index = intValue;
            return true;
        }

        index = 0;
        return false;
    }

    private Expression FoldEnumAccess(EnumAccessExpression enumAccess)
    {
        if (!enums.TryGetValue(enumAccess.EnumName, out var members))
            return enumAccess;
        if (!members.Contains(enumAccess.MemberName))
            return enumAccess;

        return StringLiteral(enumAccess.EnumName + enumAccess.MemberName, enumAccess.Line, enumAccess.Column);
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

        return StringLiteral(output, literal.Line, literal.Column);
    }

    private LiteralExpression? TryFoldUnary(UnaryExpression unary)
    {
        if (unary.Operator == "#" && TryGetArrayLength(unary.Operand, out var arrayLength))
            return NumberLiteral(arrayLength, unary.Line, unary.Column);

        if (unary.Operand is not LiteralExpression literal)
            return null;

        return unary.Operator switch
        {
            "-" when TryGetNumber(literal, out var negValue, out var negWasInt) =>
                NumberLiteral(-negValue, unary.Line, unary.Column, negWasInt),
            "+" when TryGetNumber(literal, out var posValue, out var posWasInt) =>
                NumberLiteral(posValue, unary.Line, unary.Column, posWasInt),
            "!" => BoolLiteral(!ToBoolean(literal), unary.Line, unary.Column),
            "#" when TryGetStringLength(literal, out var stringLength) =>
                NumberLiteral(stringLength, unary.Line, unary.Column),
            _ => null
        };
    }

    private LiteralExpression? TryFoldBinary(BinaryExpression binary)
    {
        if (binary.Left is not LiteralExpression left)
            return null;

        if (binary.Operator == "&&")
        {
            if (!ToBoolean(left))
                return BoolLiteral(false, binary.Line, binary.Column);

            if (binary.Right is LiteralExpression rightWhenAnd)
                return BoolLiteral(ToBoolean(rightWhenAnd), binary.Line, binary.Column);

            return null;
        }

        if (binary.Operator == "||")
        {
            if (ToBoolean(left))
                return BoolLiteral(true, binary.Line, binary.Column);

            if (binary.Right is LiteralExpression rightWhenOr)
                return BoolLiteral(ToBoolean(rightWhenOr), binary.Line, binary.Column);

            return null;
        }

        if (binary.Right is not LiteralExpression right)
            return null;

        if (binary.Operator == "+" && (IsStringLike(left) || IsStringLike(right)))
        {
            return StringLiteral(
                LiteralToText(left) + LiteralToText(right),
                binary.Line,
                binary.Column);
        }

        if (TryGetNumber(left, out var leftNum, out var leftInt) && TryGetNumber(right, out var rightNum, out var rightInt))
        {
            switch (binary.Operator)
            {
                case "+":
                    return NumberLiteral(leftNum + rightNum, binary.Line, binary.Column, leftInt && rightInt);
                case "-":
                    return NumberLiteral(leftNum - rightNum, binary.Line, binary.Column, leftInt && rightInt);
                case "*":
                    return NumberLiteral(leftNum * rightNum, binary.Line, binary.Column, leftInt && rightInt);
                case "/" when rightNum != 0:
                    return NumberLiteral(leftNum / rightNum, binary.Line, binary.Column, false);
                case "%" when rightNum != 0:
                    return NumberLiteral(leftNum % rightNum, binary.Line, binary.Column, leftInt && rightInt);
                case "==":
                    return BoolLiteral(leftNum == rightNum, binary.Line, binary.Column);
                case "!=":
                    return BoolLiteral(leftNum != rightNum, binary.Line, binary.Column);
                case "<":
                    return BoolLiteral(leftNum < rightNum, binary.Line, binary.Column);
                case ">":
                    return BoolLiteral(leftNum > rightNum, binary.Line, binary.Column);
                case "<=":
                    return BoolLiteral(leftNum <= rightNum, binary.Line, binary.Column);
                case ">=":
                    return BoolLiteral(leftNum >= rightNum, binary.Line, binary.Column);
            }
        }

        if (binary.Operator is "==" or "!=" &&
            left.LiteralType.PrimitiveKind == PrimitiveType.Kind.Bool &&
            right.LiteralType.PrimitiveKind == PrimitiveType.Kind.Bool &&
            left.Value is bool leftBool &&
            right.Value is bool rightBool)
        {
            var equals = leftBool == rightBool;
            return BoolLiteral(binary.Operator == "==" ? equals : !equals, binary.Line, binary.Column);
        }

        if (binary.Operator is "==" or "!=" && IsStringLike(left) && IsStringLike(right))
        {
            var equals = string.Equals(LiteralToText(left), LiteralToText(right), StringComparison.Ordinal);
            return BoolLiteral(binary.Operator == "==" ? equals : !equals, binary.Line, binary.Column);
        }

        return null;
    }

    private void BindConst(string name, Expression expression)
    {
        if (expression is LiteralExpression literal)
        {
            scopes.Peek()[name] = new Binding(CloneLiteral(literal), null, null);
            return;
        }

        if (expression is ArrayLiteral arrayLiteral)
        {
            scopes.Peek()[name] = new Binding(
                null,
                CloneArrayLiteral(arrayLiteral),
                arrayLiteral.Elements.Count);
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

    private bool TryResolveArray(string name, out ArrayLiteral array)
    {
        foreach (var scope in scopes)
        {
            if (scope.TryGetValue(name, out var binding) && binding.Array != null)
            {
                array = binding.Array;
                return true;
            }
        }

        array = null!;
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
                if (!scope.TryGetValue(identifier.Name, out var binding))
                    continue;

                if (binding.ArrayLength.HasValue)
                {
                    length = binding.ArrayLength.Value;
                    return true;
                }

                if (binding.Array != null)
                {
                    length = binding.Array.Elements.Count;
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

    private static bool TryEvaluateCondition(Expression expression, out bool value)
    {
        if (expression is LiteralExpression literal)
        {
            value = ToBoolean(literal);
            return true;
        }

        value = false;
        return false;
    }

    private static bool TryEvaluateLiteral(Expression expression, out LiteralExpression literal)
    {
        if (expression is LiteralExpression literalExpression)
        {
            literal = literalExpression;
            return true;
        }

        literal = null!;
        return false;
    }

    private static bool IsExactCasePattern(Expression pattern)
    {
        if (pattern is not LiteralExpression literal ||
            literal.LiteralType is not PrimitiveType { PrimitiveKind: PrimitiveType.Kind.String })
        {
            return true;
        }

        var text = literal.Value?.ToString() ?? string.Empty;
        return text.IndexOfAny(['*', '?', '[', ']']) < 0;
    }

    private static bool AreLiteralsEqual(LiteralExpression left, LiteralExpression right)
    {
        if (left.LiteralType.PrimitiveKind != right.LiteralType.PrimitiveKind)
            return false;

        return left.LiteralType.PrimitiveKind switch
        {
            PrimitiveType.Kind.Int when left.Value is int li && right.Value is int ri => li == ri,
            PrimitiveType.Kind.Bool when left.Value is bool lb && right.Value is bool rb => lb == rb,
            PrimitiveType.Kind.String => string.Equals(left.Value?.ToString(), right.Value?.ToString(), StringComparison.Ordinal),
            _ => false
        };
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

    private static ArrayLiteral CloneArrayLiteral(ArrayLiteral array)
    {
        return new ArrayLiteral
        {
            Line = array.Line,
            Column = array.Column,
            Type = array.Type,
            Elements = array.Elements.Select(CloneExpression).ToList()
        };
    }

    private static Expression CloneExpression(Expression expression)
    {
        return expression switch
        {
            LiteralExpression literal => CloneLiteral(literal),
            IdentifierExpression identifier => new IdentifierExpression
            {
                Line = identifier.Line,
                Column = identifier.Column,
                Name = identifier.Name,
                Type = identifier.Type
            },
            EnumAccessExpression enumAccess => new EnumAccessExpression
            {
                Line = enumAccess.Line,
                Column = enumAccess.Column,
                EnumName = enumAccess.EnumName,
                MemberName = enumAccess.MemberName,
                Type = enumAccess.Type
            },
            UnaryExpression unary => new UnaryExpression
            {
                Line = unary.Line,
                Column = unary.Column,
                Operator = unary.Operator,
                Operand = CloneExpression(unary.Operand),
                Type = unary.Type
            },
            BinaryExpression binary => new BinaryExpression
            {
                Line = binary.Line,
                Column = binary.Column,
                Left = CloneExpression(binary.Left),
                Operator = binary.Operator,
                Right = CloneExpression(binary.Right),
                Type = binary.Type
            },
            PipeExpression pipe => new PipeExpression
            {
                Line = pipe.Line,
                Column = pipe.Column,
                Left = CloneExpression(pipe.Left),
                Right = CloneExpression(pipe.Right),
                Type = pipe.Type
            },
            RedirectExpression redirect => new RedirectExpression
            {
                Line = redirect.Line,
                Column = redirect.Column,
                Left = CloneExpression(redirect.Left),
                Operator = redirect.Operator,
                Right = CloneExpression(redirect.Right),
                Type = redirect.Type
            },
            IndexAccessExpression index => new IndexAccessExpression
            {
                Line = index.Line,
                Column = index.Column,
                Array = CloneExpression(index.Array),
                Index = CloneExpression(index.Index),
                Type = index.Type
            },
            FunctionCallExpression call => new FunctionCallExpression
            {
                Line = call.Line,
                Column = call.Column,
                FunctionName = call.FunctionName,
                Arguments = call.Arguments.Select(CloneExpression).ToList(),
                Type = call.Type
            },
            ShellCaptureExpression capture => new ShellCaptureExpression
            {
                Line = capture.Line,
                Column = capture.Column,
                Command = CloneExpression(capture.Command),
                Type = capture.Type
            },
            RangeExpression range => new RangeExpression
            {
                Line = range.Line,
                Column = range.Column,
                Start = CloneExpression(range.Start),
                End = CloneExpression(range.End),
                Type = range.Type
            },
            ArrayLiteral array => CloneArrayLiteral(array),
            NullLiteral nullLiteral => new NullLiteral
            {
                Line = nullLiteral.Line,
                Column = nullLiteral.Column,
                Type = nullLiteral.Type
            },
            _ => expression
        };
    }

    private static LiteralExpression NumberLiteral(double value, int line, int column, bool isInt = true)
    {
        _ = isInt;
        return new LiteralExpression
        {
            Line = line,
            Column = column,
            Value = (int)value,
            LiteralType = new PrimitiveType { PrimitiveKind = PrimitiveType.Kind.Int },
            Type = ExpressionTypes.Number
        };
    }

    private static LiteralExpression StringLiteral(string value, int line, int column)
    {
        return new LiteralExpression
        {
            Line = line,
            Column = column,
            Value = value,
            LiteralType = new PrimitiveType { PrimitiveKind = PrimitiveType.Kind.String },
            Type = ExpressionTypes.String
        };
    }

    private static LiteralExpression BoolLiteral(bool value, int line, int column)
    {
        return new LiteralExpression
        {
            Line = line,
            Column = column,
            Value = value,
            LiteralType = new PrimitiveType { PrimitiveKind = PrimitiveType.Kind.Bool },
            Type = ExpressionTypes.Bool
        };
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
                case SubshellStatement subshellStatement:
                    CollectEnums(subshellStatement.Body);
                    break;
            }
        }
    }

    private void CollectPureFunctionCandidates(IEnumerable<Statement> statements)
    {
        foreach (var statement in statements)
        {
            switch (statement)
            {
                case FunctionDeclaration function when TryGetPureReturnExpression(function, out var returnExpression):
                    pureCandidates[function.Name] = new PureFunctionDefinition(
                        function.Name,
                        function.Parameters,
                        returnExpression);
                    CollectPureFunctionCandidates(function.Body);
                    break;

                case FunctionDeclaration function:
                    CollectPureFunctionCandidates(function.Body);
                    break;

                case IfStatement ifStatement:
                    CollectPureFunctionCandidates(ifStatement.ThenBlock);
                    foreach (var clause in ifStatement.ElifClauses)
                        CollectPureFunctionCandidates(clause.Body);
                    CollectPureFunctionCandidates(ifStatement.ElseBlock);
                    break;

                case SwitchStatement switchStatement:
                    foreach (var clause in switchStatement.Cases)
                        CollectPureFunctionCandidates(clause.Body);
                    break;

                case ForLoop forLoop:
                    CollectPureFunctionCandidates(forLoop.Body);
                    break;

                case WhileLoop whileLoop:
                    CollectPureFunctionCandidates(whileLoop.Body);
                    break;

                case SubshellStatement subshellStatement:
                    CollectPureFunctionCandidates(subshellStatement.Body);
                    break;
            }
        }
    }

    private static bool TryGetPureReturnExpression(FunctionDeclaration function, out Expression returnExpression)
    {
        returnExpression = null!;
        if (function.Body.Count != 1)
            return false;
        if (function.Body[0] is not ReturnStatement { Value: not null } returnStatement)
            return false;

        returnExpression = returnStatement.Value;
        return true;
    }

    private void ResolvePureFunctions()
    {
        bool changed;
        do
        {
            changed = false;
            foreach (var candidate in pureCandidates.Values)
            {
                if (pureFunctions.ContainsKey(candidate.Name))
                    continue;

                var parameters = candidate.Parameters.Select(p => p.Name).ToHashSet(StringComparer.Ordinal);
                var knownPureFunctions = pureFunctions.Keys.ToHashSet(StringComparer.Ordinal);
                if (!IsPureExpression(candidate.ReturnExpression, parameters, knownPureFunctions, candidate.Name))
                    continue;

                pureFunctions[candidate.Name] = candidate;
                changed = true;
            }
        } while (changed);
    }

    private static bool IsPureExpression(
        Expression expression,
        HashSet<string> parameters,
        HashSet<string> knownPureFunctions,
        string currentFunction)
    {
        switch (expression)
        {
            case LiteralExpression:
            case EnumAccessExpression:
                return true;

            case IdentifierExpression identifier:
                return parameters.Contains(identifier.Name);

            case UnaryExpression unary:
                return IsPureExpression(unary.Operand, parameters, knownPureFunctions, currentFunction);

            case BinaryExpression binary:
                return IsPureExpression(binary.Left, parameters, knownPureFunctions, currentFunction)
                       && IsPureExpression(binary.Right, parameters, knownPureFunctions, currentFunction);

            case IndexAccessExpression index:
                return IsPureExpression(index.Array, parameters, knownPureFunctions, currentFunction)
                       && IsPureExpression(index.Index, parameters, knownPureFunctions, currentFunction);

            case ArrayLiteral array:
                return array.Elements.All(e => IsPureExpression(e, parameters, knownPureFunctions, currentFunction));

            case FunctionCallExpression call:
                if (string.Equals(call.FunctionName, currentFunction, StringComparison.Ordinal))
                    return false;
                if (!knownPureFunctions.Contains(call.FunctionName))
                    return false;
                return call.Arguments.All(a => IsPureExpression(a, parameters, knownPureFunctions, currentFunction));

            default:
                return false;
        }
    }

    private sealed record Binding(LiteralExpression? Literal, ArrayLiteral? Array, int? ArrayLength);
    private sealed record PureFunctionDefinition(string Name, IReadOnlyList<Parameter> Parameters, Expression ReturnExpression);
}
