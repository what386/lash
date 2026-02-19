namespace Lash.Compiler.Preprocessing.Directives;

internal sealed class DefineDirective : IPreprocessorDirective
{
    public string Name => "define";

    public void Apply(Directive directive, PreprocessorState state)
    {
        if (!state.IsCurrentActive)
            return;

        if (!DirectiveProcessor.TryParseDefinition(directive.Arguments, out var name, out var value, out var error))
        {
            state.AddError($"Invalid @define directive: {error}");
            return;
        }

        state.Symbols[name] = value;
    }
}

internal sealed class UndefDirective : IPreprocessorDirective
{
    public string Name => "undef";

    public void Apply(Directive directive, PreprocessorState state)
    {
        if (!state.IsCurrentActive)
            return;

        if (!DirectiveProcessor.TryParseSymbolName(directive.Arguments, out var name, out var error))
        {
            state.AddError($"Invalid @undef directive: {error}");
            return;
        }

        state.Symbols.Remove(name);
    }
}
