namespace Lash.Compiler.Ast.Expressions;

public class FunctionCallExpression : Expression
{
    public string FunctionName { get; set; } = string.Empty;
    public List<Expression> Arguments { get; set; } = new();
}

public class ShellCaptureExpression : Expression
{
    public Expression Command { get; set; } = null!;
}

public class IndexAccessExpression : Expression
{
    public Expression Array { get; set; } = null!;
    public Expression Index { get; set; } = null!;
}

public class EnumAccessExpression : Expression
{
    public string EnumName { get; set; } = string.Empty;
    public string MemberName { get; set; } = string.Empty;
}
