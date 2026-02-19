namespace Lash.Compiler.Preprocessing.Directives;

internal interface IPreprocessorDirective
{
    string Name { get; }

    void Apply(Directive directive, PreprocessorState state);
}

internal readonly record struct Directive(string Name, string Arguments);
