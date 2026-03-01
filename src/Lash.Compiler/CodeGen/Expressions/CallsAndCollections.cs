namespace Lash.Compiler.CodeGen;

using System.Globalization;
using Lash.Compiler.Ast;
using Lash.Compiler.Ast.Expressions;
using Lash.Compiler.Ast.Types;

internal sealed partial class ExpressionGenerator
{
    private string GenerateFunctionCall(FunctionCallExpression call)
    {
        var args = string.Join(
            " ",
            call.Arguments.Select((arg, index) => GenerateFunctionCallArg(call.FunctionName, arg, index)));
        return args.Length > 0 ? $"$({call.FunctionName} {args})" : $"$({call.FunctionName})";
    }

    private string GenerateShellCaptureExpression(ShellCaptureExpression shellCapture)
    {
        if (!TryGenerateShellPayload(shellCapture.Command, out var payload))
            return HandleUnsupportedExpression(shellCapture, "shell capture payload");

        return $"$({payload})";
    }

    private string GenerateTestCaptureExpression(TestCaptureExpression testCapture)
    {
        if (!TryGenerateShellPayload(testCapture.Condition, out var payload))
            return HandleUnsupportedExpression(testCapture, "test capture payload");

        return $"$(if [[ {payload} ]]; then echo 1; else echo 0; fi)";
    }

    private string GenerateFunctionCallArg(string functionName, Expression expression, int argumentIndex)
    {
        if (expression is IdentifierExpression identifier &&
            string.Equals(identifier.Name, "argv", StringComparison.Ordinal))
        {
            return "\"$@\"";
        }

        if (expression is IdentifierExpression arrayIdentifier &&
            owner.IsArrayParameter(functionName, argumentIndex))
        {
            return $"\"${{{arrayIdentifier.Name}[@]}}\"";
        }

        var rendered = GenerateExpression(expression);
        if (IsAlreadyQuoted(rendered))
            return rendered;
        return $"\"{rendered}\"";
    }

    private static bool IsAlreadyQuoted(string rendered)
    {
        return rendered.Length >= 2 &&
               ((rendered[0] == '"' && rendered[^1] == '"') || (rendered[0] == '\'' && rendered[^1] == '\''));
    }

    private string GeneratePipeExpression(PipeExpression expr)
    {
        return GenerateValuePipeExpression(expr);
    }

    private string GenerateValuePipeExpression(PipeExpression expr)
    {
        var input = GenerateExpression(expr.Left);
        return expr.Right switch
        {
            FunctionCallExpression call => GenerateFunctionPipeInvocation(input, call),
            _ => HandleUnsupportedExpression(expr, "value pipe right stage")
        };
    }

    private string GenerateFunctionPipeInvocation(string pipedInput, FunctionCallExpression call)
    {
        var args = new List<string> { QuoteRenderedArg(pipedInput) };
        args.AddRange(call.Arguments.Select((arg, index) => GenerateFunctionCallArg(call.FunctionName, arg, index)));
        return $"$({call.FunctionName} {string.Join(" ", args)})";
    }

    private static string QuoteRenderedArg(string rendered)
    {
        if (IsAlreadyQuoted(rendered))
            return rendered;
        return $"\"{rendered}\"";
    }

    private string GenerateIndexAccess(IndexAccessExpression index)
    {
        if (index.Array is IdentifierExpression ident)
        {
            if (string.Equals(ident.Name, "argv", StringComparison.Ordinal))
            {
                var argvOffset = GeneratePositionalOffset(index.Index);
                return $"${{@:{argvOffset}:1}}";
            }

            var key = GenerateCollectionIndex(index.Index, owner.IsCurrentScopeAssociative(ident.Name));
            return $"${{{ident.Name}[{key}]}}";
        }

        return HandleUnsupportedExpression(index, "index access receiver");
    }

    internal string GenerateNumericArrayIndex(Expression index)
    {
        if (index is LiteralExpression { LiteralType: PrimitiveType { PrimitiveKind: PrimitiveType.Kind.Int }, Value: not null } literal)
            return Convert.ToString(literal.Value, CultureInfo.InvariantCulture) ?? "0";

        return $"$(( {GenerateArithmeticExpression(index)} ))";
    }

    private string GeneratePositionalOffset(Expression index)
    {
        if (index is LiteralExpression { LiteralType: PrimitiveType { PrimitiveKind: PrimitiveType.Kind.Int }, Value: not null } literal
            && literal.Value is int intValue)
        {
            return (intValue + 1).ToString(CultureInfo.InvariantCulture);
        }

        return $"$(( {GenerateArithmeticExpression(index)} + 1 ))";
    }
}
