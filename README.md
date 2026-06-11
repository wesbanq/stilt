# Stilt Language Compiler

An experimental compiler for the Stilt programming language, designed to be compiled into Minecraft datapacks.

> **WARNING**  
> The project is currently incomplete and is not in a workable state.  
> Everything is subject to change.

## About

Stilt is a small experimental language designed to compile into Minecraft datapacks.

## Roadmap

### Critical (blocking a workable compiler)

- **Datapack code generation** — `CodeGen/Generator.cs` is a stub; IR is never lowered to Minecraft commands
- **IR generation gaps** — Array/table literals, member access (`obj.field`), lambda functions, `foreach` loops, `break`/`continue` with labels

### Language features (parser/lexer)

- **Unimplemented keywords** — `trait`, `impl`, `extend`, `target`, `signal`, `select`, `enum`, `match`/`case`, `await`, `where`, `with`, `use`, `shared`
- **Unimplemented operators** — Range (`..`), signal connect (`->`), target (`@`), server (`$`), update (`|>`), overwrite (`!>`), composition (`.>`), swap (><), copy-to (`=>`)

### Compiler infrastructure

- **Object files** — Incremental compilation via `.obj` caching is implemented but disabled
- **Constant folding** — Evaluate constant expressions at compile time
- **ParseExpr refactor** — Remove recursion; support multiline expressions
- **Error reporting** — Virtual file ranges and richer diagnostics
- **Symbol source tracking** — Replace filepath-based `Source` with a more robust scheme

### Code quality

- **Errors.cs** — Rewrite string concatenation with `StringBuilder` for error formatting

