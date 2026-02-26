# tree-sitter-lash

Tree-sitter grammar for Lash.

## Files

- `grammar.js`: grammar definition.
- `tree-sitter.json`: parser metadata.
- `queries/highlights.scm`: basic highlight query.
- `src/parser.c`: generated parser.
- `src/node-types.json`: generated node types.

## Generate parser

From `treesitter/`:

```bash
tree-sitter generate
```

Optional test run (requires a working C toolchain on the host):

```bash
tree-sitter test
```
