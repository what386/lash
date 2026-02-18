# TODO — lash v0.7.1

@created: 2026-02-06
@modified: 2026-02-18

## Tasks

- [ ] Parser: implement full AST coverage for all grammar expression/statement variants (method call, member/index access, pipe, await, command forms, literals) (high) #parser #ast #feature
      @created 2026-02-06 23:08

- [ ] Semantic: complete type inference and nullability flow analysis across variables, calls, collections, and control-flow branches (high) #semantic #types #feature
      @created 2026-02-06 23:08

- [ ] Semantic: enforce mutability, scope, and self/impl method rules (including strict variable/parameter mutability and loop/function context checks) (high) #semantic #scope #feature
      @created 2026-02-06 23:08

- [ ] Modules: implement import resolution and multi-file symbol linking for module, named, and default imports (high) #modules #frontend #feature
      @created 2026-02-06 23:08

- [ ] Runtime: define and implement standard library contract used by generated Bash (Process, cmd/exec helpers, collection helpers) (high) #runtime #stdlib #feature
      @created 2026-02-06 23:08

- [ ] Testing: add end-to-end golden tests that compile each examples/\*.lash file and assert diagnostics/codegen snapshots (high) #testing #e2e #examples #quality
      @created 2026-02-06 23:09

- [ ] Testing: add runtime integration tests that execute generated Bash for representative examples and validate outputs/exit codes #testing #integration #runtime #quality
      @created 2026-02-06 23:09

- [ ] Release 0.1.0: define supported language surface and fail-fast diagnostics for unsupported features (high) #release #spec #quality
      @created 2026-02-07 02:06

- [ ] Codegen: implement try/catch/throw lowering or enforce compile-time errors in transpile mode (high) #codegen #bash #error-handling #feature
      @created 2026-02-07 02:06

- [ ] Testing: add golden snapshot tests for generated Bash across selected examples/* (high) #testing #e2e #examples #quality
      @created 2026-02-07 02:06

- [ ] Semantic: resolve nullable warnings and tighten method/self nullability/type checks in SymbolResolver #semantic #types #quality
      @created 2026-02-07 02:06

- [ ] Runtime: define Process handle contract for spawn (pid, wait/exit semantics) and add Bash helper coverage (high) #runtime #process #bash #feature
      @created 2026-02-07 02:07

- [ ] Refactor diagnostic code taxonomy and add compile-time feasibility/flow/safety analyzers (high) #feature #compiler #semantic #diagnostics
      @created 2026-02-18 20:41


## Completed

- [x] Simplify language surface to match examples/general-overview.lash and reject unsupported syntax (high) #release #spec #parser #codegen
      @created 2026-02-17 15:21
      @completed 2026-02-17 15:33

- [x] Align tree-sitter grammar with simplified Lash syntax and regenerate parser artifacts (high) #parser #treesitter #spec
      @created 2026-02-17 16:02
      @completed 2026-02-17 16:05

- [x] Remove runtime helper emission for direct Bash transpilation (high) #codegen #spec
      @created 2026-02-17 16:08
      @completed 2026-02-17 16:08

- [x] Remove semantic type system and preprocessor from compiler pipeline (high) #refactor #compiler #spec
      @created 2026-02-17 16:11
      @completed 2026-02-17 16:12

- [x] Lower Enum.Member to compile-time string literals in Bash codegen (high) #codegen #enums #spec
      @created 2026-02-17 16:14
      @completed 2026-02-17 16:14

- [x] Use native Bash array indexing/assignment in codegen (remove lash_index_* dependency) (high) #codegen #arrays #spec
      @created 2026-02-17 16:16
      @completed 2026-02-17 16:16

- [x] Remove dead AST type classes no longer used by syntax-swap compiler (high) #refactor #types #spec
      @created 2026-02-17 16:18
      @completed 2026-02-17 16:18

- [x] Remove truly unreferenced AST/diagnostics elements after syntax-swap simplification (high) #cleanup #refactor
      @created 2026-02-17 16:20
      @completed 2026-02-17 16:20

- [x] Aggressively strip legacy language constructs from grammar/AST/codegen for syntax-swap compiler (high) #cleanup #compiler #spec
      @created 2026-02-17 17:28
      @completed 2026-02-17 17:28

- [x] Implement focused v1 Bash parity: argv/shift, external capture, += concat, associative arrays, switch glob patterns (high) #feature #parser #semantic #codegen #testing
      @created 2026-02-18 03:28
      @completed 2026-02-18 03:38

- [x] Add here-string operator (<<<) support in Lash parser/codegen/tests (high) #feature #compiler
      @created 2026-02-18 19:34
      @completed 2026-02-18 19:36

- [x] Update tree-sitter grammar and highlights for here-string (<<<) redirection (high) #feature #treesitter
      @created 2026-02-18 19:37
      @completed 2026-02-18 19:38

- [x] Add non-append redirection support (>, <, 2>, &>, <>) in Lash compiler/formatter/tests (high) #feature #compiler
      @created 2026-02-18 19:54
      @completed 2026-02-18 19:57

- [x] Add fd-dup redirection support (n>&m, n>&-) in Lash compiler/formatter/tree-sitter/tests (high) #feature #compiler #treesitter
      @created 2026-02-18 20:03
      @completed 2026-02-18 20:04

- [x] Implement subshell + wait syntax/codegen/tests with 'into' capture (high) #feature #compiler #treesitter
      @created 2026-02-18 20:19
      @completed 2026-02-18 20:29

