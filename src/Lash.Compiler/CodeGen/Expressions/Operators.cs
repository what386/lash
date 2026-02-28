namespace Lash.Compiler.CodeGen;

using System.Globalization;
using Lash.Compiler.Ast;
using Lash.Compiler.Ast.Expressions;
using Lash.Compiler.Ast.Types;

internal sealed partial class ExpressionGenerator
{
    private string GenerateBinaryExpression(BinaryExpression bin)
    {
        var left = GenerateExpression(bin.Left);
        var right = GenerateExpression(bin.Right);
        var leftArithmetic = GenerateArithmeticExpression(bin.Left);
        var rightArithmetic = GenerateArithmeticExpression(bin.Right);

        return bin.Operator switch
        {
            "+" => GenerateAddition(bin.Left, bin.Right, left, right),
            "-" => $"$(({leftArithmetic} - {rightArithmetic}))",
            "*" => $"$(({leftArithmetic} * {rightArithmetic}))",
            "/" => $"$(({leftArithmetic} / {rightArithmetic}))",
            "%" => $"$(({leftArithmetic} % {rightArithmetic}))",
            "==" => $"$(( {leftArithmetic} == {rightArithmetic} ))",
            "!=" => $"$(( {leftArithmetic} != {rightArithmetic} ))",
            "<" => $"$(( {leftArithmetic} < {rightArithmetic} ))",
            ">" => $"$(( {leftArithmetic} > {rightArithmetic} ))",
            "<=" => $"$(( {leftArithmetic} <= {rightArithmetic} ))",
            ">=" => $"$(( {leftArithmetic} >= {rightArithmetic} ))",
            "&&" => $"$(( ({leftArithmetic} != 0) && ({rightArithmetic} != 0) ))",
            "||" => $"$(( ({leftArithmetic} != 0) || ({rightArithmetic} != 0) ))",
            _ => $"{left} {bin.Operator} {right}"
        };
    }

    private string GenerateUnaryExpression(UnaryExpression unary)
    {
        return unary.Operator switch
        {
            "-" => $"$((-{GenerateArithmeticExpression(unary.Operand)}))",
            "+" => $"$((+{GenerateArithmeticExpression(unary.Operand)}))",
            "!" => $"$((!{GenerateArithmeticExpression(unary.Operand)}))",
            "#" => GenerateLengthExpression(unary.Operand),
            _ => GenerateArithmeticExpression(unary.Operand)
        };
    }

    private string GenerateAddition(Expression leftExpr, Expression rightExpr, string left, string right)
    {
        if (IsStringLike(leftExpr) || IsStringLike(rightExpr))
        {
            var leftArg = GenerateStringConcatSegment(leftExpr, left);
            var rightArg = GenerateStringConcatSegment(rightExpr, right);
            return $"\"{leftArg}{rightArg}\"";
        }

        return $"$(( {left} + {right} ))";
    }

    private string GenerateStringConcatSegment(Expression expression, string renderedExpression)
    {
        return expression switch
        {
            IdentifierExpression ident => "${" + ident.Name + "}",
            LiteralExpression lit when lit.LiteralType is PrimitiveType { PrimitiveKind: PrimitiveType.Kind.String } => StripOuterQuotes(renderedExpression),
            BinaryExpression nested when nested.Operator == "+" && IsStringLike(nested) => StripOuterQuotes(renderedExpression),
            _ => StripOuterQuotes(renderedExpression)
        };
    }

    private static string StripOuterQuotes(string value)
    {
        if (value.Length >= 2 &&
            ((value[0] == '"' && value[^1] == '"') || (value[0] == '\'' && value[^1] == '\'')))
        {
            return value[1..^1];
        }

        return value;
    }

    private static bool IsStringLike(Expression expr)
    {
        return expr switch
        {
            LiteralExpression lit when lit.LiteralType is PrimitiveType { PrimitiveKind: PrimitiveType.Kind.String } => true,
            BinaryExpression bin when bin.Operator == "+" && (IsStringLike(bin.Left) || IsStringLike(bin.Right)) => true,
            _ when IsStringTyped(expr) => true,
            _ => false
        };
    }

    private string GenerateLengthExpression(Expression operand)
    {
        if (operand is IdentifierExpression ident)
        {
            if (string.Equals(ident.Name, "argv", StringComparison.Ordinal))
                return $"${{#{BashGenerator.ArgvName}[@]}}";

            return $"${{#{ident.Name}[@]}}";
        }

        if (operand is IndexAccessExpression index &&
            index.Array is IdentifierExpression arrayIdent)
        {
            if (string.Equals(arrayIdent.Name, "argv", StringComparison.Ordinal))
            {
                var argvKey = GenerateNumericArrayIndex(index.Index);
                return $"${{#{BashGenerator.ArgvName}[{argvKey}]}}";
            }

            var key = GenerateCollectionIndex(index.Index, owner.IsCurrentScopeAssociative(arrayIdent.Name));
            return $"${{#{arrayIdent.Name}[{key}]}}";
        }

        if (operand is LiteralExpression literal &&
            literal.LiteralType is PrimitiveType { PrimitiveKind: PrimitiveType.Kind.String })
        {
            return (literal.Value?.ToString() ?? string.Empty).Length.ToString(CultureInfo.InvariantCulture);
        }

        if (operand is ArrayLiteral arrayLiteral)
            return arrayLiteral.Elements.Count.ToString(CultureInfo.InvariantCulture);

        return HandleUnsupportedExpression(operand, "length operand");
    }
}
