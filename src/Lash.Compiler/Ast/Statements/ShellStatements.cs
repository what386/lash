namespace Lash.Compiler.Ast.Statements;

public class Assignment : Statement
{
    public bool IsGlobal { get; set; }
    public string Operator { get; set; } = "=";
    public Expression Target { get; set; } = null!;
    public Expression Value { get; set; } = null!;
}

public class ExpressionStatement : Statement
{
    public Expression Expression { get; set; } = null!;
}

public class CommandStatement : Statement
{
    public string Script { get; set; } = string.Empty;
}

public class ShellStatement : Statement
{
    public Expression Command { get; set; } = null!;
}

public class ShiftStatement : Statement
{
    public Expression? Amount { get; set; }
}
