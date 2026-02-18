namespace Lash.Compiler.Ast.Types;

public class PrimitiveType
{
    public enum Kind { Int, String, Bool, Any, Void }

    public Kind PrimitiveKind { get; set; }

    public override string ToString() => PrimitiveKind.ToString().ToLowerInvariant();

    public override bool Equals(object? obj)
    {
        return obj is PrimitiveType other && PrimitiveKind == other.PrimitiveKind;
    }

    public override int GetHashCode() => PrimitiveKind.GetHashCode();
}
