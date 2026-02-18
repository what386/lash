namespace Lash.Compiler.Frontend.Comptime;

using Lash.Compiler.Ast;

internal sealed class ComptimePipeline
{
    public void Run(ProgramNode program)
    {
        new ConstantFolder().Fold(program);
    }
}
