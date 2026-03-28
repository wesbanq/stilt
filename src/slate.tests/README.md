# Slate compiler test suite

This project contains golden tests for the Slate compiler: AST, IR, and (in the future) generated code.

## Running tests

From the repository root:

```bash
dotnet test
```

Or run only the test project:

```bash
dotnet test src/slate.Tests/slate.Tests.csproj
```

## Regenerating golden files

When you change the compiler’s AST or IR output (or add new test cases), update the expected JSON by running tests with the `RegenerateGoldens` property:

```bash
dotnet test -p:RegenerateGoldens=true
```

This overwrites the golden files in `src/slate.Tests/TestData/` with the current compiler output and the tests pass.
