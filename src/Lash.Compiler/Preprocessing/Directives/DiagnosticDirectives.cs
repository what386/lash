namespace Lash.Compiler.Preprocessing.Directives;

internal sealed class WarningDirective : IPreprocessorDirective
{
    public string Name => "warning";

    public void Apply(Directive directive, PreprocessorState state)
    {
        if (!state.IsCurrentActive)
            return;

        var message = string.IsNullOrWhiteSpace(directive.Arguments)
            ? "@warning directive triggered."
            : directive.Arguments;

        state.AddWarning(message);
    }
}

internal sealed class ErrorDirective : IPreprocessorDirective
{
    public string Name => "error";

    public void Apply(Directive directive, PreprocessorState state)
    {
        if (!state.IsCurrentActive)
            return;

        var message = string.IsNullOrWhiteSpace(directive.Arguments)
            ? "@error directive triggered."
            : directive.Arguments;

        state.AddError(message);
    }
}
