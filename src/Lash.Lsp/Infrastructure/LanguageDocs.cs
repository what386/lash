namespace Lash.Lsp.Infrastructure;

internal sealed class LanguageDocs
{
    private static readonly IReadOnlyDictionary<string, string> Docs = new Dictionary<string, string>(StringComparer.Ordinal)
    {
        ["if"] = "`if` starts a conditional block. Use `elif` and optional `else`, then close with `end`.\\n\\n```lash\\nif cond\\n    echo \"yes\"\\nend\\n```",
        ["elif"] = "`elif` adds another conditional branch after `if`.",
        ["else"] = "`else` is the fallback branch in an `if` block.",
        ["end"] = "`end` closes block constructs (`if`, `fn`, `for`, `while`, `switch`, `enum`, `subshell`) and preprocessor blocks (`@if`, `@raw`).",
        ["fn"] = "`fn` declares a function. Parameters may include defaults.",
        ["for"] = "`for` iterates arrays/ranges/globs: `for x in expr ... end`.",
        ["while"] = "`while` repeats while condition is truthy.",
        ["until"] = "`until` repeats until condition becomes truthy (inverse of `while`).",
        ["switch"] = "`switch` matches a value against one or more `case` clauses.",
        ["case"] = "`case` declares a switch branch: `case expr:`.",
        ["let"] = "`let` declares a mutable variable.",
        ["const"] = "`const` declares an immutable variable.",
        ["global"] = "`global` declares/assigns in the global scope.",
        ["subshell"] = "`subshell ... end` runs a Bash subshell. Use `&` to run in background and `into` to capture PID.",
        ["wait"] = "`wait` waits for a pid/expression or tracked `jobs`. Use `into` to capture exit status.",
        ["return"] = "`return` exits a function, optionally with a value.",
        ["break"] = "`break` exits the current loop.",
        ["continue"] = "`continue` skips to the next loop iteration.",
        ["shift"] = "`shift [n]` shifts `argv` by `n` (default 1).",
        ["in"] = "`in` appears in `for name in expr` loops.",
        ["step"] = "`step` sets explicit for-loop increments/decrements for ranges.",
        ["into"] = "`into` captures values from `subshell`, `wait`, and `@import ... into`.",
        ["jobs"] = "`jobs` in `wait jobs` waits for tracked background subshells.",
        ["sh"] = "`sh expr` emits shell command payload as a statement. `$sh expr` captures shell output as an expression.",
        ["argv"] = "`argv` is the script argument array.",

        ["@if"] = "Preprocessor conditional start. Evaluated at compile-time.",
        ["@elif"] = "Preprocessor conditional branch.",
        ["@else"] = "Preprocessor conditional fallback branch.",
        ["@end"] = "Closes preprocessor blocks (`@if`, `@raw`).",
        ["@import"] = "`@import` textually includes another file at compile-time. `into` can assign imported text to a variable.",
        ["@raw"] = "`@raw ... @end` copies enclosed lines directly into generated Bash.",
        ["@define"] = "Defines a preprocessor symbol.",
        ["@undef"] = "Undefines a preprocessor symbol.",
        ["@warning"] = "Emits a compile-time warning diagnostic.",
        ["@error"] = "Emits a compile-time error diagnostic.",

        ["#"] = "Unary length operator. Works on arrays/strings.",
        [".."] = "Range operator. Example: `1..10`.",
        ["|"] = "Pipe operator for Lash value flow patterns.",
        ["+="] = "Array append/concatenation assignment.",
        [">"] = "Redirect stdout to file (truncate).",
        [">>"] = "Redirect stdout to file (append).",
        ["<<"] = "Heredoc redirection with a literal payload block.",
        ["2>"] = "Redirect stderr to file (truncate).",
        ["2>>"] = "Redirect stderr to file (append).",
        ["&>"] = "Redirect stdout and stderr to file (truncate).",
        ["&>>"] = "Redirect stdout and stderr to file (append).",
        ["<<<"] = "Here-string redirection.",
        ["<>"] = "Read/write redirection.",
        ["n>&m"] = "Duplicate file descriptor `n` to `m`.",
        ["n>&-"] = "Close file descriptor `n`.",
        ["::"] = "Enum member access operator (`Enum::Member`)."
    };

    public bool TryGet(string token, out string markdown)
    {
        markdown = string.Empty;
        if (string.IsNullOrWhiteSpace(token))
            return false;

        if (TryNormalizeFdDup(token, out var fdDupNormalized)
            && Docs.TryGetValue(fdDupNormalized, out var fdDupDoc)
            && fdDupDoc is not null)
        {
            markdown = fdDupDoc;
            return true;
        }

        if (Docs.TryGetValue(token, out var tokenDoc) && tokenDoc is not null)
        {
            markdown = tokenDoc;
            return true;
        }

        var lower = token.ToLowerInvariant();
        if (Docs.TryGetValue(lower, out var lowerDoc) && lowerDoc is not null)
        {
            markdown = lowerDoc;
            return true;
        }

        return false;
    }

    private static bool TryNormalizeFdDup(string token, out string normalized)
    {
        normalized = string.Empty;
        var marker = token.IndexOf(">&", StringComparison.Ordinal);
        if (marker <= 0)
            return false;

        var left = token[..marker];
        var right = token[(marker + 2)..];

        if (!left.All(char.IsDigit))
            return false;

        if (right == "-")
        {
            normalized = "n>&-";
            return true;
        }

        if (right.All(char.IsDigit))
        {
            normalized = "n>&m";
            return true;
        }

        return false;
    }
}
