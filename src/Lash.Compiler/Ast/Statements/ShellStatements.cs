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
    public bool IsRawLiteral { get; set; }
}

public class ShellStatement : Statement
{
    public Expression Command { get; set; } = null!;
}

public class ShiftStatement : Statement
{
    public Expression? Amount { get; set; }
}

public class SubshellStatement : Statement
{
    public string? IntoVariable { get; set; }
    public bool RunInBackground { get; set; }
    public List<Statement> Body { get; set; } = new();
}

public enum WaitTargetKind
{
    Default,
    Target,
    Jobs
}

public class WaitStatement : Statement
{
    public WaitTargetKind TargetKind { get; set; } = WaitTargetKind.Default;
    public Expression? Target { get; set; }
    public string? IntoVariable { get; set; }
}
