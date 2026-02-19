namespace Lash.Compiler.Preprocessing.Directives;

internal sealed class ImportDirective : IPreprocessorDirective
{
    public string Name => "import";

    public void Apply(Directive directive, PreprocessorState state)
    {
        if (!state.IsCurrentActive)
            return;

        if (state.RuntimeBlockDepth > 0)
        {
            state.AddError("@import is only allowed at file/preprocessor scope, not inside runtime blocks.");
            return;
        }

        if (!DirectiveProcessor.TryParseImportArguments(directive.Arguments, out var pathExpression, out var intoVariable, out var error))
        {
            state.AddError($"Invalid @import directive: {error}");
            return;
        }

        state.EnqueueImport(new ImportRequest(pathExpression, intoVariable));
    }
}

internal sealed class RawDirective : IPreprocessorDirective
{
    public string Name => "raw";

    public void Apply(Directive directive, PreprocessorState state)
    {
        if (!string.IsNullOrWhiteSpace(directive.Arguments))
            state.AddError("@raw does not accept arguments.");

        state.EnterRaw(state.IsCurrentActive);
    }
}
