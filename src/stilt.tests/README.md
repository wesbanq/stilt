# Stilt compiler test suite

This project contains golden tests for the Stilt compiler: AST, IR, and (in the future) generated code.

## Running tests

From the repository root:

```bash
dotnet test
```

Or run only the test project:

```bash
dotnet test src/stilt.Tests/stilt.Tests.csproj
```

## Regenerating golden files

When you change the compiler’s AST or IR output (or add new test cases), update the expected JSON by running tests with the `RegenerateGoldens` property:

```bash
dotnet test -p:RegenerateGoldens=true
```

This overwrites the golden files in `src/stilt.Tests/TestData/` with the current compiler output and the tests pass.
