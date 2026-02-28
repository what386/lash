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
        var expressions = context.expression();
        return new ForLoop
        {
            Line = context.Start.Line,
            Column = context.Start.Column,
            IsIncrementing = true,
            Variable = context.IDENTIFIER().GetText(),
            Range = expressions.Length > 0 ? Visit(expressions[0]) as Expression ?? new NullLiteral() : null,
            GlobPattern = context.GLOB_PATTERN()?.GetText(),
            Step = expressions.Length > 1 ? Visit(expressions[1]) as Expression : null,
            Body = context.statement().Select(s => Visit(s) as Statement).Where(s => s != null).ToList()!
        };
    }

    public override AstNode VisitSelectLoop(LashParser.SelectLoopContext context)
    {
        return new SelectLoop
        {
            Line = context.Start.Line,
            Column = context.Start.Column,
            Variable = context.IDENTIFIER().GetText(),
            Options = context.expression() != null ? Visit(context.expression()) as Expression ?? new NullLiteral() : null,
            GlobPattern = context.GLOB_PATTERN()?.GetText(),
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

    public override AstNode VisitUntilLoop(LashParser.UntilLoopContext context)
    {
        return new UntilLoop
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
            IntoVariable = GetIntoVariable(intoBinding),
            IntoMode = GetIntoMode(intoBinding),
            RunInBackground = context.AMP() != null,
            Body = context.statement().Select(s => Visit(s) as Statement).Where(s => s != null).ToList()!
        };
    }

    public override AstNode VisitCoprocStatement(LashParser.CoprocStatementContext context)
    {
        var intoBinding = context.intoBinding();

        return new CoprocStatement
        {
            Line = context.Start.Line,
            Column = context.Start.Column,
            IntoVariable = GetIntoVariable(intoBinding),
            IntoMode = GetIntoMode(intoBinding),
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
            IntoVariable = GetIntoVariable(context.intoBinding()),
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

    public override AstNode VisitTestStatement(LashParser.TestStatementContext context)
    {
        return new TestStatement
        {
            Line = context.Start.Line,
            Column = context.Start.Column,
            Condition = Visit(context.expression()) as Expression ?? new NullLiteral()
        };
    }

    public override AstNode VisitTrapStatement(LashParser.TrapStatementContext context)
    {
        FunctionCallExpression? handler = null;
        Expression? command = null;

        if (context.functionCall() != null)
            handler = Visit(context.functionCall()) as FunctionCallExpression;
        else
            command = Visit(context.expression()) as Expression ?? new NullLiteral();

        return new TrapStatement
        {
            Line = context.Start.Line,
            Column = context.Start.Column,
            Signal = context.IDENTIFIER().GetText(),
            Handler = handler,
            Command = command
        };
    }

    public override AstNode VisitUntrapStatement(LashParser.UntrapStatementContext context)
    {
        return new UntrapStatement
        {
            Line = context.Start.Line,
            Column = context.Start.Column,
            Signal = context.IDENTIFIER().GetText()
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

        if (context.variableReference() != null)
            return Visit(context.variableReference());

        if (context.functionCall() != null)
            return Visit(context.functionCall());

        if (context.arrayLiteral() != null)
            return Visit(context.arrayLiteral());

        if (context.expression() != null)
            return Visit(context.expression());

        return new NullLiteral();
    }

    public override AstNode VisitVariableReference(LashParser.VariableReferenceContext context)
    {
        var identifier = context.IDENTIFIER().Symbol;
        return new IdentifierExpression
        {
            Name = context.IDENTIFIER().GetText(),
            Line = identifier.Line,
            Column = identifier.Column,
            Type = ExpressionTypes.Unknown
        };
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
        if (TryBuildRawCaptureExpression(context, out var captureExpression))
            return captureExpression;

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

    private bool TryBuildRawCaptureExpression(
        LashParser.FunctionCallContext context,
        out AstNode expression)
    {
        expression = null!;

        if (!string.Equals(context.IDENTIFIER().GetText(), RawCaptureEncoding.HelperName, StringComparison.Ordinal))
            return false;

        var args = context.argumentList()?.expression();
        if (args == null || args.Length != 1)
            return false;

        if (Visit(args[0]) is not LiteralExpression
            {
                LiteralType: PrimitiveType { PrimitiveKind: PrimitiveType.Kind.String },
                Value: string encoded
            })
        {
            return false;
        }

        if (!RawCaptureEncoding.TryDecodePayload(encoded, out var payload))
            return false;

        var trimmed = payload.TrimStart();
        if (trimmed.StartsWith("test ", StringComparison.Ordinal))
        {
            var rawCondition = trimmed[5..];
            if (TryBuildShellLiteral(rawCondition, context.Start.Line, context.Start.Column, out var conditionLiteral))
            {
                expression = new TestCaptureExpression
                {
                    Line = context.Start.Line,
                    Column = context.Start.Column,
                    Condition = conditionLiteral,
                    Type = ExpressionTypes.Unknown
                };
                return true;
            }

            expression = new TestCaptureExpression
            {
                Line = context.Start.Line,
                Column = context.Start.Column,
                Condition = new LiteralExpression
                {
                    Line = context.Start.Line,
                    Column = context.Start.Column,
                    Value = trimmed[5..],
                    LiteralType = new PrimitiveType { PrimitiveKind = PrimitiveType.Kind.String },
                    Type = ExpressionTypes.String
                },
                Type = ExpressionTypes.Unknown
            };
            return true;
        }

        if (string.Equals(trimmed, "test", StringComparison.Ordinal))
        {
            expression = new TestCaptureExpression
            {
                Line = context.Start.Line,
                Column = context.Start.Column,
                Condition = new LiteralExpression
                {
                    Line = context.Start.Line,
                    Column = context.Start.Column,
                    Value = string.Empty,
                    LiteralType = new PrimitiveType { PrimitiveKind = PrimitiveType.Kind.String },
                    Type = ExpressionTypes.String
                },
                Type = ExpressionTypes.Unknown
            };
            return true;
        }

        expression = new ShellCaptureExpression
        {
            Line = context.Start.Line,
            Column = context.Start.Column,
            Command = new LiteralExpression
            {
                Line = context.Start.Line,
                Column = context.Start.Column,
                Value = RewriteInlineInterpolatedSegments(payload),
                LiteralType = new PrimitiveType { PrimitiveKind = PrimitiveType.Kind.String },
                Type = ExpressionTypes.String
            },
            Type = ExpressionTypes.Unknown
        };
        return true;
    }

    private static bool TryBuildShellLiteral(string raw, int line, int column, out LiteralExpression literal)
    {
        literal = null!;

        if (raw.Length < 2)
            return false;

        var isInterpolated = raw.StartsWith("$\"", StringComparison.Ordinal) && raw.EndsWith("\"", StringComparison.Ordinal);
        var isString = raw.StartsWith("\"", StringComparison.Ordinal) && raw.EndsWith("\"", StringComparison.Ordinal);
        var isMultiline = raw.StartsWith("[[", StringComparison.Ordinal) && raw.EndsWith("]]", StringComparison.Ordinal);
        if (!isInterpolated && !isString && !isMultiline)
            return false;

        literal = new LiteralExpression
        {
            Line = line,
            Column = column,
            Value = UnquoteStringLiteral(raw),
            LiteralType = new PrimitiveType { PrimitiveKind = PrimitiveType.Kind.String },
            Type = ExpressionTypes.String,
            IsInterpolated = isInterpolated,
            IsMultiline = isMultiline
        };
        return true;
    }

    private static string RewriteInlineInterpolatedSegments(string payload)
    {
        if (!payload.Contains("$\"", StringComparison.Ordinal))
            return payload;

        var output = new System.Text.StringBuilder(payload.Length);
        for (var i = 0; i < payload.Length; i++)
        {
            if (payload[i] != '$' || i + 1 >= payload.Length || payload[i + 1] != '"')
            {
                output.Append(payload[i]);
                continue;
            }

            var start = i + 2;
            var end = start;
            var escaped = false;
            while (end < payload.Length)
            {
                var ch = payload[end];
                if (escaped)
                {
                    escaped = false;
                    end++;
                    continue;
                }

                if (ch == '\\')
                {
                    escaped = true;
                    end++;
                    continue;
                }

                if (ch == '"')
                    break;

                end++;
            }

            if (end >= payload.Length)
            {
                output.Append(payload[i]);
                continue;
            }

            var template = payload[start..end];
            output.Append('"');
            output.Append(ConvertTemplateToBashInterpolation(template));
            output.Append('"');
            i = end;
        }

        return output.ToString();
    }

    private static string ConvertTemplateToBashInterpolation(string template)
    {
        var output = new System.Text.StringBuilder(template.Length + 8);

        for (var i = 0; i < template.Length; i++)
        {
            var ch = template[i];
            if (ch != '{')
            {
                output.Append(ch);
                continue;
            }

            var close = template.IndexOf('}', i + 1);
            if (close < 0)
            {
                output.Append(ch);
                continue;
            }

            var name = template[(i + 1)..close].Trim();
            if (IsSimpleIdentifier(name))
            {
                output.Append("${");
                output.Append(name);
                output.Append('}');
                i = close;
                continue;
            }

            output.Append(template, i, close - i + 1);
            i = close;
        }

        return output.ToString();
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
            return IntoBindingMode.Assign;

        if (intoBinding.variableReference() is not null)
            return IntoBindingMode.Assign;

        if (intoBinding.ChildCount >= 3)
        {
            var modeText = intoBinding.GetChild(1).GetText();
            if (string.Equals(modeText, "const", StringComparison.Ordinal))
                return IntoBindingMode.Const;
            if (string.Equals(modeText, "let", StringComparison.Ordinal))
                return IntoBindingMode.Let;
        }

        return IntoBindingMode.Assign;
    }

    private static string? GetIntoVariable(LashParser.IntoBindingContext? intoBinding)
    {
        if (intoBinding == null)
            return null;

        if (intoBinding.variableReference() is { } variableRef)
            return variableRef.IDENTIFIER().GetText();

        return intoBinding.IDENTIFIER().GetText();
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

    private static bool IsSimpleIdentifier(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return false;
        if (!(char.IsLetter(value[0]) || value[0] == '_'))
            return false;

        for (var i = 1; i < value.Length; i++)
        {
            var ch = value[i];
            if (!(char.IsLetterOrDigit(ch) || ch == '_'))
                return false;
        }

        return true;
    }
}
