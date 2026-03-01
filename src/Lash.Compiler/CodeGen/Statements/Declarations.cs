namespace Lash.Compiler.CodeGen;

using Lash.Compiler.Ast;
using Lash.Compiler.Ast.Expressions;
using Lash.Compiler.Ast.Statements;

internal sealed partial class StatementGenerator
{
    private void GenerateVariableDeclaration(VariableDeclaration varDecl)
    {
        var isAssociative = owner.IsAssociativeVariable(varDecl.Name, varDecl.IsGlobal);
        var isFunctionLocal = owner.CurrentFunctionName != null && !varDecl.IsGlobal;
        var value = GenerateVariableDeclarationValue(varDecl.Value);

        if (isAssociative)
        {
            if (isFunctionLocal)
            {
                owner.Emit($"local -A {varDecl.Name}={value}");
                if (varDecl.Kind == VariableDeclaration.VarKind.Const)
                {
                    owner.EmitLine();
                    owner.Emit($"readonly {varDecl.Name}");
                }
                return;
            }

            owner.Emit($"declare -A {varDecl.Name}={value}");
            if (varDecl.Kind == VariableDeclaration.VarKind.Const)
            {
                owner.EmitLine();
                owner.Emit($"readonly {varDecl.Name}");
            }
            return;
        }

        if (varDecl.Kind == VariableDeclaration.VarKind.Const)
        {
            if (isFunctionLocal)
            {
                owner.Emit($"local -r {varDecl.Name}={value}");
                return;
            }

            owner.Emit($"readonly {varDecl.Name}={value}");
            return;
        }

        if (isFunctionLocal)
        {
            owner.Emit($"local {varDecl.Name}={value}");
            return;
        }

        owner.Emit($"{varDecl.Name}={value}");
    }

    private void GenerateAssignment(Assignment assignment)
    {
        if (assignment.Operator == "+=")
        {
            GenerateAppendAssignment(assignment);
            return;
        }

        if (assignment.Target is IndexAccessExpression indexTarget &&
            indexTarget.Array is IdentifierExpression identifierTarget)
        {
            if (string.Equals(identifierTarget.Name, "argv", StringComparison.Ordinal))
            {
                owner.EmitComment("Unsupported assignment target 'argv'.");
                owner.ReportUnsupported("assignment target 'argv'");
                return;
            }

            var key = owner.GenerateCollectionIndex(indexTarget.Index, owner.IsCurrentScopeAssociative(identifierTarget.Name));
            var assignedValue = owner.GenerateExpression(assignment.Value);
            owner.Emit($"{identifierTarget.Name}[{key}]={assignedValue}");
            return;
        }

        if (assignment.Target is IdentifierExpression ident)
        {
            var value = GenerateAssignmentValue(assignment.Value);
            owner.Emit($"{ident.Name}={value}");
            return;
        }

        owner.EmitComment($"Unsupported assignment target '{assignment.Target.GetType().Name}'.");
        owner.ReportUnsupported($"assignment target '{assignment.Target.GetType().Name}'");
    }

    private void GenerateFunctionDeclaration(FunctionDeclaration func)
    {
        var previousFunctionName = owner.CurrentFunctionName;
        owner.CurrentFunctionName = func.Name;

        owner.EmitLine($"{func.Name}() {{");
        owner.IndentLevel++;

        for (int i = 0; i < func.Parameters.Count; i++)
        {
            var param = func.Parameters[i];
            var isArrayParam = owner.IsArrayParameter(func.Name, i);
            if (isArrayParam)
            {
                if (i == func.Parameters.Count - 1)
                    owner.EmitLine($"local -a {param.Name}=(\"${{@:{i + 1}}}\")");
                else
                    owner.EmitLine($"local -a {param.Name}=(\"${i + 1}\")");
                continue;
            }

            if (param.DefaultValue == null)
            {
                owner.EmitLine($"local {param.Name}=\"${i + 1}\"");
                continue;
            }

            owner.EmitLine($"local {param.Name}=\"${{{i + 1}-}}\"");
            if (param.DefaultValue != null)
            {
                var defaultValue = owner.GenerateExpression(param.DefaultValue);
                owner.EmitLine($"if (( $# < {i + 1} )); then {param.Name}={defaultValue}; fi");
            }
        }

        if (func.Parameters.Count > 0)
            owner.EmitLine();

        foreach (var stmt in func.Body)
        {
            GenerateStatement(stmt);
            owner.EmitLine();
        }

        owner.IndentLevel--;
        owner.Emit("}");

        owner.CurrentFunctionName = previousFunctionName;
    }

    private string GenerateVariableDeclarationValue(Expression value)
    {
        if (value is IdentifierExpression identifier &&
            string.Equals(identifier.Name, "argv", StringComparison.Ordinal))
        {
            return "(\"$@\")";
        }

        return owner.GenerateExpression(value);
    }

    private string GenerateAssignmentValue(Expression value)
    {
        if (value is IdentifierExpression identifier &&
            string.Equals(identifier.Name, "argv", StringComparison.Ordinal))
        {
            return "(\"$@\")";
        }

        return owner.GenerateExpression(value);
    }

    private void GenerateAppendAssignment(Assignment assignment)
    {
        if (assignment.Target is not IdentifierExpression identifier)
        {
            owner.EmitComment("Unsupported assignment target for '+='.");
            owner.ReportUnsupported("assignment target for '+='");
            return;
        }

        string appendValue = assignment.Value switch
        {
            ArrayLiteral array => owner.GenerateArrayLiteral(array),
            IdentifierExpression rhsIdentifier when string.Equals(rhsIdentifier.Name, "argv", StringComparison.Ordinal) =>
                "(\"$@\")",
            IdentifierExpression rhsIdentifier => $"(\"${{{rhsIdentifier.Name}[@]}}\")",
            _ => HandleUnsupportedExpression(assignment.Value, "array append value")
        };

        owner.Emit($"{identifier.Name}+={appendValue}");
    }
}
