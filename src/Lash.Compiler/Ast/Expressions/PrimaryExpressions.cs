namespace Lash.Compiler.Ast.Expressions;
using Lash.Compiler.Ast.Types;

// ============================================
// Primary Expressions
// ============================================

public class LiteralExpression : Expression
{
    public object Value { get; set; } = null!;
    public PrimitiveType LiteralType { get; set; } = null!;
    public bool IsInterpolated { get; set; }
    public bool IsMultiline { get; set; }
}

public class IdentifierExpression : Expression
{
    public string Name { get; set; } = string.Empty;
}

public class NullLiteral : Expression
{
    public NullLiteral()
    {
        Type = ExpressionTypes.Unknown;
    }
}
