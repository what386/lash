namespace Lash.Compiler.Ast.Statements;
using Lash.Compiler.Ast.Expressions;

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

public class TestStatement : Statement
{
    public Expression Condition { get; set; } = null!;
}

public class TrapStatement : Statement
{
    public string Signal { get; set; } = string.Empty;
    public FunctionCallExpression? Handler { get; set; }
    public Expression? Command { get; set; }
}

public class UntrapStatement : Statement
{
    public string Signal { get; set; } = string.Empty;
}

public class ShiftStatement : Statement
{
    public Expression? Amount { get; set; }
}

public enum IntoBindingMode
{
    Auto,
    Let,
    Const
}

public class SubshellStatement : Statement
{
    public string? IntoVariable { get; set; }
    public IntoBindingMode IntoMode { get; set; } = IntoBindingMode.Auto;
    public bool IntoCreatesVariable { get; set; }
    public bool IntoCreatesConst { get; set; }
    public bool RunInBackground { get; set; }
    public List<Statement> Body { get; set; } = new();
}

public class CoprocStatement : Statement
{
    public string? IntoVariable { get; set; }
    public IntoBindingMode IntoMode { get; set; } = IntoBindingMode.Auto;
    public bool IntoCreatesVariable { get; set; }
    public bool IntoCreatesConst { get; set; }
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
    public IntoBindingMode IntoMode { get; set; } = IntoBindingMode.Auto;
    public bool IntoCreatesVariable { get; set; }
    public bool IntoCreatesConst { get; set; }
}
