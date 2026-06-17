# Stilt Language Compiler

An experimental compiler for the **Stilt** programming language — a small, statically-typed
language that compiles to **Minecraft datapacks**.

> **Status — early, work in progress**
>
> The front end (preprocessing, lexing, parsing) is the most developed part and has golden
> tests. The linker and IR generator have been ported to the new `SymbolReference` API to
> restore the build, but their resolution/lowering logic is still **unfinished and untested**
> (see the [Roadmap](#roadmap)). Everything is subject to change.

## About

Stilt is an experimental, statically-typed language designed to compile into Minecraft
datapacks. The compiler is written in C# (`net10.0`) and split into a reusable core library
(`stilt.core`) and a command-line front end (`stilt.cli`).

## Compiler pipeline

A `.stilt` source file flows through these stages:

1. **Preprocess** — strip comments, normalize newlines, expand tabs, join `\` line-continuations
   (line counts are preserved so diagnostics stay on the right line).
2. **Lex** — turn text into tokens. The token vocabulary is declared by attributes on the
   `TokenType` enum, which the lexer reads via reflection; those same attributes also drive
   operator precedence in the parser.
3. **Parse** — a recursive-descent parser builds the AST, assembling expressions into a
   precedence-correct tree. Names are captured as *unresolved* references and scopes are built,
   but resolution happens later.
4. **Link** — resolve names against scopes and pull in imports. *(mid-refactor)*
5. **IR generation** — lower the AST to a tree of instruction `Block`s. Control flow becomes
   (conditional) function calls and loops become tail-recursive blocks, mirroring how a datapack
   actually executes (a named block becomes one `.mcfunction`). *(mid-refactor / partial)*
6. **Code generation** — emit the datapack. *(stub)*

## Capabilities

### Working
- **Preprocessing** — line (`#`) and block (`## … ##`) comments, CRLF→LF, tab expansion,
  `\` line-continuations.
- **Lexing** — identifiers, keywords, numeric literals (decimal, whole, hex `0x`, octal `0o`,
  binary `0b`, and scientific, each with optional `b`/`s`/`i`/`l`/`f`/`d` type suffixes), strings
  (with `r`/`f`/`t`/`m` prefixes), operators, and newline/bracket-aware statement separators.
- **Parsing** — variable declarations (including multiple targets, `var (a, b) = …`), functions,
  `if`/`elif`/`else`, `while`/`for`/`foreach`/`repeat … until` loops, `return`, `break`/`continue`,
  `import … as`, compile-time `version { … }` blocks, embedded `execute` command blocks,
  decorators (`[[ … ]]`), and operator-precedence expressions.

Golden tests for the lexer, parser (AST), and IR live in `src/stilt.tests` (they can't run until
the core compiles again — see below).

### Partial / not yet wired up
- **Linking** — name resolution and imports are written and now compile against the
  `SymbolReference` API, but the resolution logic is unfinished and untested.
- **IR generation** — statements, control flow, and most expressions are lowered, but array/table
  literals, member access, lambdas, `foreach`, and `break`/`continue` are not; the whole stage is
  untested end-to-end.
- **Type checking** — not implemented; `TypeChecker.cs` / `Analyzer.cs` are scaffolds.
- **Code generation** — not implemented; `CodeGen/Generator.cs` is a stub, so IR is never lowered
  to commands.
- **Declarations** — `type` and `trait` are lexed but their parser cases are stubbed out; table
  literals throw `NotImplementedException`.

## Language at a glance

```stilt
var a = 2
var b = 4

func add(x: int, y: int): int {
    return x + y
}

if a < b {
    execute
        /say "Hello, world!"
        /tp @p ~2 ~1 ~2
} else {
    a++
}
```

## Using the compiler

The CLI exposes the pipeline at increasing depth, which is handy for inspecting each stage:

| Command | Does |
| --- | --- |
| `preprocess` | preprocessing only |
| `token` | preprocess + lex |
| `tree` | preprocess + lex + parse (dumps the AST) |
| `ir` | the above + IR generation |
| `build` | full build |

Common options: `-i`/`--input <file>`, `-o`/`--output <file>`, `-v`/`--mc-version <java/1.x.y>`,
`--no-std`, and `-j <dir>` to dump a stage's output as JSON. Pass `--help` for the full list.

## Project layout

- `src/stilt.core` — the compiler library (lexer, parser, AST, linker, IR, codegen).
- `src/stilt.cli` — command-line front end.
- `src/stilt.tests` — xUnit golden tests.
- `examples/` — sample `.stilt` programs.

## Roadmap

### Critical (blocking a buildable compiler)

- **Finish linking & IR** — both now compile against the `SymbolReference` API, but their
  resolution/lowering logic is unfinished and untested; make them actually work.
- **Type checking** — implement the `TypeChecker`/`Analyzer` stage (currently empty).
- **Datapack code generation** — `CodeGen/Generator.cs` is a stub; lower IR to Minecraft commands.
- **IR generation gaps** — array/table literals, member access (`obj.field`), lambdas, `foreach`,
  and `break`/`continue` with labels.

### Language features

- **Declarations** — finish `type` and `trait` parsing (cases are stubbed); table literals.
- **Unimplemented keywords** — `enum`, `extend`, `signal`, `shared`, `await`, `match`/`case`
  (tokenized but not parsed); `impl`, `target`, `select`, `where`, `with`, `use` (not yet tokenized).
- **Unimplemented operators** — range (`..`), signal connect (`->`), emit signal (`<-`),
  update (`|>`), overwrite (`!>`), composition (`.>`), named tuple (`=>`).

### Compiler infrastructure

- **Object files** — incremental compilation via `.o` caching is implemented but disabled.
- **Constant folding** — evaluate constant expressions at compile time.
- **`ParseExpr` refactor** — remove recursion; support multiline expressions.
- **Error reporting** — virtual file ranges and richer diagnostics.
- **Symbol source tracking** — replace filepath-based `Source` with a more robust scheme.

### Code quality

- **`Errors.cs`** — build error text with `StringBuilder` instead of string concatenation.
