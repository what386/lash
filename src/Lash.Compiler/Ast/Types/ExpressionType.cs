namespace Lash.Compiler.Ast.Types;

public abstract class ExpressionType
{
    public abstract string Name { get; }

    public override string ToString() => Name;
}
