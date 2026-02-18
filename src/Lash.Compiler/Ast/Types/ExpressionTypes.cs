namespace Lash.Compiler.Ast.Types;

public static class ExpressionTypes
{
    public static readonly ExpressionType Unknown = new UnknownType();
    public static readonly ExpressionType Number = new NumberType();
    public static readonly ExpressionType String = new StringType();
    public static readonly ExpressionType Bool = new BooleanType();
    public static readonly ExpressionType Array = new ArrayType();
}
