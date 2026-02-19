# Lash

Lash is a Lua-like scripting language that transpiles to Bash.

This repository contains:

- `lash`: CLI entrypoint
- `lashc`: compiler (`.lash` -> `.sh`)
- `lashfmt`: formatter

## Status

Lash is under active development. Language details can evolve between versions.

Current language reference: `language-spec.md`.

## Requirements

- .NET SDK (projects currently target `net10.0`)
- Bash (for running generated scripts)

## Quick Start

Run an example:

```bash
dotnet run --project src/Lash.Cli/Lash.Cli.csproj -- run examples/overview.lash
```

Run the prime sieve example with a CLI arg:

```bash
dotnet run --project src/Lash.Cli/Lash.Cli.csproj -- run examples/prime-sieve.lash 100
```

Compile a Lash file to Bash:

```bash
dotnet run --project src/Lash.Cli/Lash.Cli.csproj -- compile examples/prime-sieve.lash -o prime-sieve.sh
bash prime-sieve.sh 100
```

Check only (no output file):

```bash
dotnet run --project src/Lash.Cli/Lash.Cli.csproj -- check examples/prime-sieve.lash
```

Format files:

```bash
dotnet run --project src/Lash.Cli/Lash.Cli.csproj -- format examples
dotnet run --project src/Lash.Cli/Lash.Cli.csproj -- format examples --check
```

## CLI Commands

From `lash --help`:

- `compile <file>`: compile `.lash` to Bash (`-o/--output` optional)
- `check <file>`: validate a `.lash` file without emission
- `format <paths>...`: format files/directories (`--check` supported)
- `run <file> [args...]`: compile to temp Bash and execute

Use `--verbose` on commands for extra phase/progress logs.
