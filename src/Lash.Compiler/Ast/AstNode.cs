namespace Lash.Compiler.Ast;
using Lash.Compiler.Ast.Types;

public abstract class AstNode
{
    public int Line { get; set; }
    public int Column { get; set; }
}

public class ProgramNode : AstNode
{
    public List<Statement> Statements { get; set; } = new();
}

public abstract class Statement : AstNode { }

public abstract class Expression : AstNode
{
    public ExpressionType Type { get; set; } = ExpressionTypes.Unknown;
}

public class Parameter : AstNode
{
    public bool IsMutable { get; set; }
    public string Name { get; set; } = string.Empty;
    public PrimitiveType Type { get; set; } = null!;
    public Expression? DefaultValue { get; set; }
}
