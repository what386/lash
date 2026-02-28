namespace Lash.Compiler.Ast.Statements;

public class IfStatement : Statement
{
    public Expression Condition { get; set; } = null!;
    public List<Statement> ThenBlock { get; set; } = new();
    public List<ElifClause> ElifClauses { get; set; } = new();
    public List<Statement> ElseBlock { get; set; } = new();
}

public class ElifClause : AstNode
{
    public Expression Condition { get; set; } = null!;
    public List<Statement> Body { get; set; } = new();
}

public class ForLoop : Statement
{
    public bool IsIncrementing { get; set; } = true;
    public string Variable { get; set; } = string.Empty;
    public Expression? Range { get; set; }
    public string? GlobPattern { get; set; }
    public Expression? Step { get; set; }
    public List<Statement> Body { get; set; } = new();
}

public class SelectLoop : Statement
{
    public string Variable { get; set; } = string.Empty;
    public Expression? Options { get; set; }
    public string? GlobPattern { get; set; }
    public List<Statement> Body { get; set; } = new();
}

public class WhileLoop : Statement
{
    public Expression Condition { get; set; } = null!;
    public List<Statement> Body { get; set; } = new();
}

public class UntilLoop : Statement
{
    public Expression Condition { get; set; } = null!;
    public List<Statement> Body { get; set; } = new();
}

public class SwitchStatement : Statement
{
    public Expression Value { get; set; } = null!;
    public List<SwitchCaseClause> Cases { get; set; } = new();
}

public class SwitchCaseClause : AstNode
{
    public Expression Pattern { get; set; } = null!;
    public List<Statement> Body { get; set; } = new();
}

public class ReturnStatement : Statement
{
    public Expression? Value { get; set; }
}

public class BreakStatement : Statement { }

public class ContinueStatement : Statement { }
