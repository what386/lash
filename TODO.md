# TODO — lash-lang v0.8.0

@created: 2026-02-24
@modified: 2026-03-01

## Tasks

- [ ] Add shell-command registry for set/export/shopt/alias/source with AST-backed validation diagnostics (high) #feature #language #compiler
      @created 2026-03-01 00:31

- [ ] Rename registered-command terminology to shell-command across AST, frontend registry, and tests #feature #compiler
      @created 2026-03-01 00:31


## Completed

- [x] Add Lash LSP server with diagnostics, hover, and go-to-definition #feature #lsp
      @created 2026-02-27 03:07
      @completed 2026-02-27 03:07
      @completed_version 0.6.0

- [x] Refactor compiler analysis into a reusable API for editor tooling #feature #compiler #lsp
      @created 2026-02-27 03:07
      @completed 2026-02-27 03:07
      @completed_version 0.6.0

- [x] Add Lash LSP completion with keywords, symbols, directives, and core snippets #feature #lsp
      @created 2026-02-27 20:17
      @completed 2026-02-27 20:17
      @completed_version 0.7.0

- [x] Add safe rename and prepare-rename support to Lash LSP #feature #lsp
      @created 2026-02-27 20:17
      @completed 2026-02-27 20:17
      @completed_version 0.7.0

- [x] Improve Lash LSP hover docs for language tokens and symbols #feature #lsp
      @created 2026-02-27 20:17
      @completed 2026-02-27 20:17
      @completed_version 0.7.0

- [x] Harden Lash LSP local symbol resolution and add LSP test suite #feature #lsp
      @created 2026-02-27 20:17
      @completed 2026-02-27 20:17
      @completed_version 0.7.0

- [x] Make into bindings explicit: 'into ' assigns existing vars, 'into let/const name' creates vars (high) #feature #language
      @created 2026-02-28 23:27
      @completed 2026-02-28 23:28
      @completed_version 0.8.0

- [x] Add Bash bootstrap build script (scripts/build/build.sh) mirroring Lash build flags and output layout #feature #tooling
      @created 2026-02-28 23:27
      @completed 2026-02-28 23:28
      @completed_version 0.8.0

- [x] Add Bash bootstrap pack script (scripts/build/pack.sh) to archive release bundles into dist/*.tar.gz #feature #tooling
      @created 2026-02-28 23:27
      @completed 2026-02-28 23:28
      @completed_version 0.8.0

- [x] Use captured test expressions for file/dir checks in scripts/build/build.lash #bug #tooling
      @created 2026-02-28 23:27
      @completed 2026-02-28 23:28
      @completed_version 0.8.0

