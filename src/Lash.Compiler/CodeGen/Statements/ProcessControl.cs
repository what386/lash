namespace Lash.Compiler.CodeGen;

using Lash.Compiler.Ast.Statements;

internal sealed partial class StatementGenerator
{
    private void GenerateShiftStatement(ShiftStatement shiftStatement)
    {
        var amount = shiftStatement.Amount == null
            ? "1"
            : owner.GenerateArithmeticExpression(shiftStatement.Amount);

        owner.Emit("__lash_shift_n=$(( " + amount + " ))");
        owner.EmitLine();
        owner.Emit("if (( __lash_shift_n > 0 )); then");
        owner.EmitLine();
        owner.Emit("if (( __lash_shift_n >= $# )); then set --; else shift \"${__lash_shift_n}\"; fi");
        owner.EmitLine();
        owner.Emit("fi");
    }

    private void GenerateSubshellStatement(SubshellStatement subshellStatement)
    {
        owner.Emit("(");
        owner.IndentLevel++;

        foreach (var stmt in subshellStatement.Body)
        {
            owner.EmitLine();
            GenerateStatement(stmt);
        }

        owner.IndentLevel--;
        owner.EmitLine();
        owner.Emit(")");

        if (subshellStatement.RunInBackground)
        {
            owner.Emit(" &");

            if (!string.IsNullOrEmpty(subshellStatement.IntoVariable))
            {
                owner.EmitLine();
                EmitIntoCaptureAssignment(
                    subshellStatement.IntoVariable!,
                    "$!",
                    subshellStatement.IntoCreatesVariable,
                    subshellStatement.IntoCreatesConst);
            }

            if (owner.NeedsTrackedJobs)
            {
                owner.EmitLine();
                owner.Emit($"{BashGenerator.TrackedJobsName}+=(\"$!\")");
            }

            return;
        }

        if (!string.IsNullOrEmpty(subshellStatement.IntoVariable))
        {
            owner.EmitLine();
            EmitIntoCaptureAssignment(
                subshellStatement.IntoVariable!,
                "$?",
                subshellStatement.IntoCreatesVariable,
                subshellStatement.IntoCreatesConst);
        }
    }

    private void GenerateCoprocStatement(CoprocStatement coprocStatement)
    {
        owner.Emit("coproc {");
        owner.IndentLevel++;

        foreach (var stmt in coprocStatement.Body)
        {
            owner.EmitLine();
            GenerateStatement(stmt);
        }

        owner.IndentLevel--;
        owner.EmitLine();
        owner.Emit("}");

        if (!string.IsNullOrEmpty(coprocStatement.IntoVariable))
        {
            owner.EmitLine();
            EmitIntoCaptureAssignment(
                coprocStatement.IntoVariable!,
                "${COPROC_PID}",
                coprocStatement.IntoCreatesVariable,
                coprocStatement.IntoCreatesConst);
        }

        if (owner.NeedsTrackedJobs)
        {
            owner.EmitLine();
            owner.Emit($"{BashGenerator.TrackedJobsName}+=(\"${{COPROC_PID}}\")");
        }
    }

    private void GenerateWaitStatement(WaitStatement waitStatement)
    {
        switch (waitStatement.TargetKind)
        {
            case WaitTargetKind.Jobs:
                GenerateWaitJobsStatement(waitStatement);
                return;

            case WaitTargetKind.Target:
                if (waitStatement.Target != null)
                {
                    var target = GenerateSingleShellArg(string.Empty, waitStatement.Target, -1);
                    owner.Emit($"wait {target}");
                }
                else
                {
                    owner.Emit("wait");
                }
                break;

            default:
                owner.Emit("wait");
                break;
        }

        if (!string.IsNullOrEmpty(waitStatement.IntoVariable))
        {
            owner.EmitLine();
            EmitIntoCaptureAssignment(
                waitStatement.IntoVariable!,
                "$?",
                waitStatement.IntoCreatesVariable,
                waitStatement.IntoCreatesConst);
        }
    }

    private void GenerateWaitJobsStatement(WaitStatement waitStatement)
    {
        if (!string.IsNullOrEmpty(waitStatement.IntoVariable))
        {
            EmitIntoCaptureAssignment(
                waitStatement.IntoVariable!,
                "0",
                waitStatement.IntoCreatesVariable,
                waitStatement.IntoCreatesConst);
            owner.EmitLine();
        }

        owner.Emit($"for {BashGenerator.WaitPidName} in \"${{{BashGenerator.TrackedJobsName}[@]}}\"; do");
        owner.IndentLevel++;
        owner.EmitLine();
        owner.Emit($"wait \"${{{BashGenerator.WaitPidName}}}\"");

        if (!string.IsNullOrEmpty(waitStatement.IntoVariable))
        {
            owner.EmitLine();
            owner.Emit($"{waitStatement.IntoVariable}=$?");
        }

        owner.IndentLevel--;
        owner.EmitLine();
        owner.Emit("done");
        owner.EmitLine();
        owner.Emit($"{BashGenerator.TrackedJobsName}=()");
    }

    private void EmitIntoCaptureAssignment(string name, string value, bool createsVariable, bool createsConst)
    {
        if (createsVariable)
        {
            if (createsConst)
            {
                if (owner.CurrentFunctionName != null)
                {
                    owner.Emit($"local -r {name}={value}");
                    return;
                }

                owner.Emit($"readonly {name}={value}");
                return;
            }

            if (owner.CurrentFunctionName != null)
            {
                owner.Emit($"local {name}={value}");
                return;
            }
        }

        owner.Emit($"{name}={value}");
    }
}
