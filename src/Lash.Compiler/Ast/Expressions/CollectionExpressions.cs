namespace Lash.Compiler.Ast.Expressions;

public class ArrayLiteral : Expression
{
    public List<Expression> Elements { get; set; } = new();
}
