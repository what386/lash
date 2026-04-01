namespace Lash.Compiler.Ast.Statements;

public class VariableDeclaration : Statement
{
    public enum VarKind { Var, Let, Readonly }
    public VarKind Kind { get; set; } = VarKind.Var;
    public bool IsGlobal { get; set; }
    public bool IsPublic { get; set; }
    public string Name { get; set; } = string.Empty;
    public Expression Value { get; set; } = null!;
}

public class FunctionDeclaration : Statement
{
    public bool IsPublic { get; set; }
    public bool IsAsync { get; set; }
    public string Name { get; set; } = string.Empty;
    public List<Parameter> Parameters { get; set; } = new();
    public List<Statement> Body { get; set; } = new();
}

public class EnumDeclaration : Statement
{
    public string Name { get; set; } = string.Empty;
    public List<string> Members { get; set; } = new();
}
