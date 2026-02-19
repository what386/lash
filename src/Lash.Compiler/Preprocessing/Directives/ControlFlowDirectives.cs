namespace Lash.Compiler.Preprocessing.Directives;

internal sealed class IfDirective : IPreprocessorDirective
{
    public string Name => "if";

    public void Apply(Directive directive, PreprocessorState state)
    {
        if (string.IsNullOrWhiteSpace(directive.Arguments))
        {
            state.AddError("@if requires a condition expression.");
            state.Conditionals.Push(new ConditionalFrame(state.IsCurrentActive, false, false, false, state.CurrentLine, state.CurrentColumn));
            return;
        }

        if (!state.TryEvaluateCondition(directive.Arguments, out var condition, out var error))
        {
            state.AddError($"Invalid @if expression: {error}");
            state.Conditionals.Push(new ConditionalFrame(state.IsCurrentActive, false, false, false, state.CurrentLine, state.CurrentColumn));
            return;
        }

        var parentActive = state.IsCurrentActive;
        var ifActive = parentActive && condition;
        state.Conditionals.Push(new ConditionalFrame(parentActive, ifActive, ifActive, false, state.CurrentLine, state.CurrentColumn));
    }
}

internal sealed class ElifDirective : IPreprocessorDirective
{
    public string Name => "elif";

    public void Apply(Directive directive, PreprocessorState state)
    {
        if (!state.Conditionals.TryPop(out var frame))
        {
            state.AddError("@elif without matching @if.");
            return;
        }

        if (string.IsNullOrWhiteSpace(directive.Arguments))
        {
            state.AddError("@elif requires a condition expression.");
            state.Conditionals.Push(frame);
            return;
        }

        if (frame.ElseSeen)
        {
            state.AddError("@elif cannot appear after @else in the same conditional block.");
            state.Conditionals.Push(frame with { IsActive = false });
            return;
        }

        if (!state.TryEvaluateCondition(directive.Arguments, out var condition, out var error))
        {
            state.AddError($"Invalid @elif expression: {error}");
            state.Conditionals.Push(frame with { IsActive = false });
            return;
        }

        var elifActive = frame.ParentActive && !frame.AnyBranchMatched && condition;
        state.Conditionals.Push(frame with
        {
            AnyBranchMatched = frame.AnyBranchMatched || elifActive,
            IsActive = elifActive
        });
    }
}

internal sealed class ElseDirective : IPreprocessorDirective
{
    public string Name => "else";

    public void Apply(Directive directive, PreprocessorState state)
    {
        if (!state.Conditionals.TryPop(out var frame))
        {
            state.AddError("@else without matching @if.");
            return;
        }

        if (frame.ElseSeen)
        {
            state.AddError("Only one @else is allowed per @if block.");
            state.Conditionals.Push(frame);
            return;
        }

        var elseActive = frame.ParentActive && !frame.AnyBranchMatched;
        state.Conditionals.Push(frame with
        {
            AnyBranchMatched = frame.AnyBranchMatched || elseActive,
            IsActive = elseActive,
            ElseSeen = true
        });
    }
}

internal sealed class EndifDirective : IPreprocessorDirective
{
    public string Name => "endif";

    public void Apply(Directive directive, PreprocessorState state)
    {
        if (!state.Conditionals.TryPop(out _))
            state.AddError("@endif without matching @if.");
    }
}
