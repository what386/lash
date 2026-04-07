namespace Lash.Compiler.Ast.Expressions;

public class ArrayLiteral : Expression
{
    public List<Expression> Elements { get; set; } = new();
}

public class MapLiteral : Expression
{
    public List<MapLiteralEntry> Entries { get; set; } = new();
}

public class MapLiteralEntry : AstNode
{
    public Expression Key { get; set; } = null!;
    public Expression Value { get; set; } = null!;
}
