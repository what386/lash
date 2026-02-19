namespace Lash.Compiler.Frontend;

using Antlr4.Runtime;
using Lash.Compiler.Ast;
using Lash.Compiler.Ast.Expressions;
using Lash.Compiler.Ast.Statements;
using Lash.Compiler.Ast.Types;
using Lash.Compiler.Preprocessing;

public class AstBuilder : LashBaseVisitor<AstNode>
{
    public override AstNode VisitProgram(LashParser.ProgramContext context)
    {
        var program = new ProgramNode
        {
            Line = context.Start.Line,
            Column = context.Start.Column
        };

        foreach (var stmt in context.statement())
        {
            if (Visit(stmt) is Statement statement)
                program.Statements.Add(statement);
        }

        return program;
    }

    public override AstNode VisitVariableDeclaration(LashParser.VariableDeclarationContext context)
    {
        var hasGlobal = context.GetChild(0).GetText() == "global";
        var keyword = hasGlobal ? context.GetChild(1).GetText() : context.GetChild(0).GetText();
        var declarationKind = string.Equals(keyword, "const", StringComparison.Ordinal)
            ? VariableDeclaration.VarKind.Const
            : VariableDeclaration.VarKind.Let;

        return new VariableDeclaration
        {
            Line = context.Start.Line,
            Column = context.Start.Column,
            Kind = declarationKind,
            IsGlobal = hasGlobal,
            IsPublic = false,
            Name = context.IDENTIFIER().GetText(),
            Value = context.expression() != null
                ? Visit(context.expression()) as Expression ?? new NullLiteral()
                : new NullLiteral { Line = context.Start.Line, Column = context.Start.Column }
        };
    }

    public override AstNode VisitEnumDeclaration(LashParser.EnumDeclarationContext context)
    {
        var declaration = new EnumDeclaration
        {
            Line = context.Start.Line,
            Column = context.Start.Column,
            Name = context.IDENTIFIER().GetText()
        };

        foreach (var member in context.enumMember())
            declaration.Members.Add(member.IDENTIFIER().GetText());

        return declaration;
    }

    public override AstNode VisitAssignment(LashParser.AssignmentContext context)
    {
        var hasGlobal = context.GetChild(0).GetText() == "global";
        Expression target;
        if (context.IDENTIFIER() != null)
        {
            target = new IdentifierExpression
            {
                Name = context.IDENTIFIER().GetText(),
                Line = context.Start.Line,
                Column = context.Start.Column
            };
        }
        else
        {
            target = Visit(context.indexAccess()) as Expression ?? new NullLiteral();
        }

        return new Assignment
        {
            Line = context.Start.Line,
            Column = context.Start.Column,
            IsGlobal = hasGlobal,
            Operator = context.ADD_ASSIGN() != null ? "+=" : "=",
            Target = target,
            Value = Visit(context.expression()) as Expression ?? new NullLiteral()
        };
    }

    public override AstNode VisitFunctionDeclaration(LashParser.FunctionDeclarationContext context)
    {
        var func = new FunctionDeclaration
        {
            Line = context.Start.Line,
            Column = context.Start.Column,
            IsPublic = false,
            IsAsync = false,
            Name = context.IDENTIFIER().GetText()
        };

        if (context.parameterList() != null)
        {
            foreach (var param in context.parameterList().parameter())
            {
                func.Parameters.Add(new Parameter
                {
                    Line = param.Start.Line,
                    Column = param.Start.Column,
                    IsMutable = false,
                    Name = param.IDENTIFIER().GetText(),
                    Type = new PrimitiveType { PrimitiveKind = PrimitiveType.Kind.Any },
                    DefaultValue = param.expression() != null ? Visit(param.expression()) as Expression : null
                });
            }
        }

        foreach (var stmt in context.functionBody().statement())
        {
            if (Visit(stmt) is Statement statement)
                func.Body.Add(statement);
        }

        return func;
    }

    public override AstNode VisitIfStatement(LashParser.IfStatementContext context)
    {
        var ifStmt = new IfStatement
        {
            Line = context.Start.Line,
            Column = context.Start.Column,
            Condition = Visit(context.expression()) as Expression ?? new NullLiteral()
        };

        foreach (var stmt in context.ifBlock().statement())
        {
            if (Visit(stmt) is Statement statement)
                ifStmt.ThenBlock.Add(statement);
        }

        foreach (var elifContext in context.elifClause())
        {
            var clause = new ElifClause
            {
                Line = elifContext.Start.Line,
                Column = elifContext.Start.Column,
                Condition = Visit(elifContext.expression()) as Expression ?? new NullLiteral()
            };

            foreach (var stmt in elifContext.ifBlock().statement())
            {
                if (Visit(stmt) is Statement statement)
                    clause.Body.Add(statement);
            }

            ifStmt.ElifClauses.Add(clause);
        }

        if (context.elseClause() != null)
        {
            foreach (var stmt in context.elseClause().ifBlock().statement())
            {
                if (Visit(stmt) is Statement statement)
                    ifStmt.ElseBlock.Add(statement);
            }
        }

        return ifStmt;
    }

    public override AstNode VisitSwitchStatement(LashParser.SwitchStatementContext context)
    {
        var switchStmt = new SwitchStatement
        {
            Line = context.Start.Line,
            Column = context.Start.Column,
            Value = Visit(context.expression()) as Expression ?? new NullLiteral()
        };

        foreach (var clause in context.switchCaseClause())
        {
            var caseClause = new SwitchCaseClause
            {
                Line = clause.Start.Line,
                Column = clause.Start.Column,
                Pattern = Visit(clause.expression()) as Expression ?? new NullLiteral()
            };

            foreach (var stmt in clause.statement())
            {
                if (Visit(stmt) is Statement parsed)
                    caseClause.Body.Add(parsed);
            }

            switchStmt.Cases.Add(caseClause);
        }

        return switchStmt;
    }

    public override AstNode VisitForLoop(LashParser.ForLoopContext context)
    {
        return new ForLoop
        {
            Line = context.Start.Line,
            Column = context.Start.Column,
            IsIncrementing = true,
            Variable = context.IDENTIFIER().GetText(),
            Range = Visit(context.expression(0)) as Expression ?? new NullLiteral(),
            Step = context.expression().Length > 1 ? Visit(context.expression(1)) as Expression : null,
            Body = context.statement().Select(s => Visit(s) as Statement).Where(s => s != null).ToList()!
        };
    }

    public override AstNode VisitWhileLoop(LashParser.WhileLoopContext context)
    {
        return new WhileLoop
        {
            Line = context.Start.Line,
            Column = context.Start.Column,
            Condition = Visit(context.expression()) as Expression ?? new NullLiteral(),
            Body = context.statement().Select(s => Visit(s) as Statement).Where(s => s != null).ToList()!
        };
    }

    public override AstNode VisitReturnStatement(LashParser.ReturnStatementContext context)
    {
        return new ReturnStatement
        {
            Line = context.Start.Line,
            Column = context.Start.Column,
            Value = context.expression() != null ? Visit(context.expression()) as Expression : null
        };
    }

    public override AstNode VisitShiftStatement(LashParser.ShiftStatementContext context)
    {
        return new ShiftStatement
        {
            Line = context.Start.Line,
            Column = context.Start.Column,
            Amount = context.expression() != null ? Visit(context.expression()) as Expression : null
        };
    }

    public override AstNode VisitSubshellStatement(LashParser.SubshellStatementContext context)
    {
        var intoBinding = context.intoBinding();

        return new SubshellStatement
        {
            Line = context.Start.Line,
            Column = context.Start.Column,
            IntoVariable = intoBinding?.IDENTIFIER().GetText(),
            IntoMode = GetIntoMode(intoBinding),
            RunInBackground = context.AMP() != null,
            Body = context.statement().Select(s => Visit(s) as Statement).Where(s => s != null).ToList()!
        };
    }

    public override AstNode VisitWaitStatement(LashParser.WaitStatementContext context)
    {
        var targetKind = WaitTargetKind.Default;
        Expression? target = null;

        if (context.waitTarget() != null)
        {
            if (context.waitTarget()!.GetText() == "jobs")
            {
                targetKind = WaitTargetKind.Jobs;
            }
            else
            {
                targetKind = WaitTargetKind.Target;
                target = Visit(context.waitTarget()!.expression()) as Expression ?? new NullLiteral();
            }
        }

        return new WaitStatement
        {
            Line = context.Start.Line,
            Column = context.Start.Column,
            TargetKind = targetKind,
            Target = target,
            IntoVariable = context.intoBinding()?.IDENTIFIER().GetText(),
            IntoMode = GetIntoMode(context.intoBinding())
        };
    }

    public override AstNode VisitBreakStatement(LashParser.BreakStatementContext context)
    {
        return new BreakStatement { Line = context.Start.Line, Column = context.Start.Column };
    }

    public override AstNode VisitContinueStatement(LashParser.ContinueStatementContext context)
    {
        return new ContinueStatement { Line = context.Start.Line, Column = context.Start.Column };
    }

    public override AstNode VisitExpressionStatement(LashParser.ExpressionStatementContext context)
    {
        return new ExpressionStatement
        {
            Line = context.Start.Line,
            Column = context.Start.Column,
            Expression = Visit(context.expression()) as Expression ?? new NullLiteral()
        };
    }

    public override AstNode VisitShStatement(LashParser.ShStatementContext context)
    {
        return new ShellStatement
        {
            Line = context.Start.Line,
            Column = context.Start.Column,
            Command = Visit(context.expression()) as Expression ?? new NullLiteral()
        };
    }

    public override AstNode VisitCommandStatement(LashParser.CommandStatementContext context)
    {
        const string marker = "__cmd";
        var raw = context.COMMAND_STATEMENT().GetText();
        var script = raw.StartsWith(marker, StringComparison.Ordinal)
            ? raw[marker.Length..].TrimStart()
            : raw;
        var isRawLiteral = RawCommandEncoding.TryDecode(script, out var decodedScript);

        return new CommandStatement
        {
            Line = context.Start.Line,
            Column = context.Start.Column,
            Script = decodedScript,
            IsRawLiteral = isRawLiteral
        };
    }

    public override AstNode VisitPrimaryExpr(LashParser.PrimaryExprContext context)
    {
        return Visit(context.primaryExpression());
    }

    public override AstNode VisitPrimaryExpression(LashParser.PrimaryExpressionContext context)
    {
        if (context.literal() != null)
            return Visit(context.literal());

        if (context.enumAccess() != null)
            return Visit(context.enumAccess());

        if (context.shellCaptureExpression() != null)
            return Visit(context.shellCaptureExpression());

        if (context.functionCall() != null)
            return Visit(context.functionCall());

        if (context.IDENTIFIER() != null)
        {
            return new IdentifierExpression
            {
                Name = context.IDENTIFIER().GetText(),
                Line = context.Start.Line,
                Column = context.Start.Column,
                Type = ExpressionTypes.Unknown
            };
        }

        if (context.arrayLiteral() != null)
            return Visit(context.arrayLiteral());

        if (context.expression() != null)
            return Visit(context.expression());

        return new NullLiteral();
    }

    public override AstNode VisitEnumAccess(LashParser.EnumAccessContext context)
    {
        return new EnumAccessExpression
        {
            Line = context.Start.Line,
            Column = context.Start.Column,
            EnumName = context.IDENTIFIER(0).GetText(),
            MemberName = context.IDENTIFIER(1).GetText(),
            Type = ExpressionTypes.Unknown
        };
    }

    public override AstNode VisitFunctionCall(LashParser.FunctionCallContext context)
    {
        var call = new FunctionCallExpression
        {
            Line = context.Start.Line,
            Column = context.Start.Column,
            FunctionName = context.IDENTIFIER().GetText()
        };

        if (context.argumentList() != null)
        {
            foreach (var arg in context.argumentList().expression())
            {
                if (Visit(arg) is Expression expr)
                    call.Arguments.Add(expr);
            }
        }

        return call;
    }

    public override AstNode VisitShellCaptureExpression(LashParser.ShellCaptureExpressionContext context)
    {
        return new ShellCaptureExpression
        {
            Line = context.Start.Line,
            Column = context.Start.Column,
            Command = Visit(context.expression()) as Expression ?? new NullLiteral(),
            Type = ExpressionTypes.Unknown
        };
    }

    public override AstNode VisitIndexAccessExpr(LashParser.IndexAccessExprContext context)
    {
        return new IndexAccessExpression
        {
            Line = context.Start.Line,
            Column = context.Start.Column,
            Array = Visit(context.expression(0)) as Expression ?? new NullLiteral(),
            Index = Visit(context.expression(1)) as Expression ?? new NullLiteral(),
            Type = ExpressionTypes.Unknown
        };
    }

    public override AstNode VisitPipeExpr(LashParser.PipeExprContext context)
    {
        return new PipeExpression
        {
            Line = context.Start.Line,
            Column = context.Start.Column,
            Left = Visit(context.expression(0)) as Expression ?? new NullLiteral(),
            Right = Visit(context.expression(1)) as Expression ?? new NullLiteral(),
            Type = ExpressionTypes.Unknown
        };
    }

    public override AstNode VisitRedirectExpr(LashParser.RedirectExprContext context)
    {
        return new RedirectExpression
        {
            Line = context.Start.Line,
            Column = context.Start.Column,
            Left = Visit(context.expression(0)) as Expression ?? new NullLiteral(),
            Operator = context.GetChild(1).GetText(),
            Right = Visit(context.expression(1)) as Expression ?? new NullLiteral(),
            Type = ExpressionTypes.Unknown
        };
    }

    public override AstNode VisitFdDupExpr(LashParser.FdDupExprContext context)
    {
        return new RedirectExpression
        {
            Line = context.Start.Line,
            Column = context.Start.Column,
            Left = Visit(context.expression()) as Expression ?? new NullLiteral(),
            Operator = context.FD_DUP().GetText(),
            Right = new NullLiteral
            {
                Line = context.Start.Line,
                Column = context.Start.Column,
                Type = ExpressionTypes.Unknown
            },
            Type = ExpressionTypes.Unknown
        };
    }

    public override AstNode VisitUnaryExpr(LashParser.UnaryExprContext context)
    {
        return new UnaryExpression
        {
            Line = context.Start.Line,
            Column = context.Start.Column,
            Operator = context.GetChild(0).GetText(),
            Operand = Visit(context.expression()) as Expression ?? new NullLiteral(),
            Type = ExpressionTypes.Unknown
        };
    }

    public override AstNode VisitMultiplicativeExpr(LashParser.MultiplicativeExprContext context)
    {
        return BuildBinaryExpression(context, context.expression(0), context.GetChild(1).GetText(), context.expression(1));
    }

    public override AstNode VisitAdditiveExpr(LashParser.AdditiveExprContext context)
    {
        return BuildBinaryExpression(context, context.expression(0), context.GetChild(1).GetText(), context.expression(1));
    }

    public override AstNode VisitRangeExpr(LashParser.RangeExprContext context)
    {
        return new RangeExpression
        {
            Line = context.Start.Line,
            Column = context.Start.Column,
            Start = Visit(context.expression(0)) as Expression ?? new NullLiteral(),
            End = Visit(context.expression(1)) as Expression ?? new NullLiteral()
        };
    }

    public override AstNode VisitComparisonExpr(LashParser.ComparisonExprContext context)
    {
        return BuildBinaryExpression(context, context.expression(0), context.GetChild(1).GetText(), context.expression(1));
    }

    public override AstNode VisitLogicalExpr(LashParser.LogicalExprContext context)
    {
        return BuildBinaryExpression(context, context.expression(0), context.GetChild(1).GetText(), context.expression(1));
    }

    public override AstNode VisitArrayLiteral(LashParser.ArrayLiteralContext context)
    {
        var array = new ArrayLiteral
        {
            Line = context.Start.Line,
            Column = context.Start.Column,
            Type = ExpressionTypes.Array
        };

        foreach (var expr in context.expression())
        {
            if (Visit(expr) is Expression element)
                array.Elements.Add(element);
        }

        return array;
    }

    public override AstNode VisitIndexAccess(LashParser.IndexAccessContext context)
    {
        return new IndexAccessExpression
        {
            Line = context.Start.Line,
            Column = context.Start.Column,
            Array = Visit(context.expression(0)) as Expression ?? new NullLiteral(),
            Index = Visit(context.expression(1)) as Expression ?? new NullLiteral(),
            Type = ExpressionTypes.Unknown
        };
    }

    public override AstNode VisitLiteral(LashParser.LiteralContext context)
    {
        if (context.INTEGER() != null)
        {
            return new LiteralExpression
            {
                Line = context.Start.Line,
                Column = context.Start.Column,
                Value = int.Parse(context.INTEGER().GetText()),
                LiteralType = new PrimitiveType { PrimitiveKind = PrimitiveType.Kind.Int },
                Type = ExpressionTypes.Number
            };
        }

        if (context.BOOLEAN() != null)
        {
            return new LiteralExpression
            {
                Line = context.Start.Line,
                Column = context.Start.Column,
                Value = bool.Parse(context.BOOLEAN().GetText()),
                LiteralType = new PrimitiveType { PrimitiveKind = PrimitiveType.Kind.Bool },
                Type = ExpressionTypes.Bool
            };
        }

        if (context.stringLiteral() != null)
        {
            var stringLiteral = context.stringLiteral();
            return new LiteralExpression
            {
                Line = context.Start.Line,
                Column = context.Start.Column,
                Value = UnquoteStringLiteral(stringLiteral.GetText()),
                LiteralType = new PrimitiveType { PrimitiveKind = PrimitiveType.Kind.String },
                Type = ExpressionTypes.String,
                IsInterpolated = stringLiteral.INTERPOLATED_STRING() != null,
                IsMultiline = stringLiteral.MULTILINE_STRING() != null
            };
        }

        return new NullLiteral { Line = context.Start.Line, Column = context.Start.Column };
    }

    private AstNode BuildBinaryExpression(
        ParserRuleContext context,
        LashParser.ExpressionContext leftContext,
        string op,
        LashParser.ExpressionContext rightContext)
    {
        return new BinaryExpression
        {
            Line = context.Start.Line,
            Column = context.Start.Column,
            Left = Visit(leftContext) as Expression ?? new NullLiteral(),
            Operator = op,
            Right = Visit(rightContext) as Expression ?? new NullLiteral(),
            Type = ExpressionTypes.Unknown
        };
    }

    private static IntoBindingMode GetIntoMode(LashParser.IntoBindingContext? intoBinding)
    {
        if (intoBinding == null)
            return IntoBindingMode.Auto;

        if (intoBinding.ChildCount >= 3)
        {
            var modeText = intoBinding.GetChild(1).GetText();
            if (string.Equals(modeText, "const", StringComparison.Ordinal))
                return IntoBindingMode.Const;
            if (string.Equals(modeText, "let", StringComparison.Ordinal))
                return IntoBindingMode.Let;
        }

        return IntoBindingMode.Auto;
    }

    private static string UnquoteStringLiteral(string text)
    {
        if (text.StartsWith("$\"") && text.EndsWith("\"") && text.Length >= 3)
            return text[2..^1];
        if (text.StartsWith("\"") && text.EndsWith("\"") && text.Length >= 2)
            return text[1..^1];
        if (text.StartsWith("[[") && text.EndsWith("]]") && text.Length >= 4)
            return text[2..^2];
        return text;
    }
}
