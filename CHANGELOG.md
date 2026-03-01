# Changelog — lash-lang

*Generated on 2026-03-01*

## 0.9.0 — 2026-03-01

### High Priority

- Add shell-command registry for set/export/shopt/alias/source with AST-backed validation diagnostics `feature`, `language`, `compiler`

### Changes

- Rename registered-command terminology to shell-command across AST, frontend registry, and tests `feature`, `compiler`


## 0.8.0 — 2026-02-28

### High Priority

- Make into bindings explicit: 'into ' assigns existing vars, 'into let/const name' creates vars `feature`, `language`

### Changes

- Add heredoc redirection and until loops to Lash with direct Bash lowering `feature`, `language`
- Use captured test expressions for file/dir checks in scripts/build/build.lash `bug`, `tooling`


## 0.7.0 — 2026-02-27

### Changes

- Add Lash LSP completion with keywords, symbols, directives, and core snippets `feature`, `lsp`
- Add safe rename and prepare-rename support to Lash LSP `feature`, `lsp`
- Improve Lash LSP hover docs for language tokens and symbols `feature`, `lsp`
- Harden Lash LSP local symbol resolution and add LSP test suite `feature`, `lsp`


## 0.6.0 — 2026-02-27

### Changes

- Add Lash LSP server with diagnostics, hover, and go-to-definition `feature`, `lsp`
- Refactor compiler analysis into a reusable API for editor tooling `feature`, `compiler`, `lsp`



