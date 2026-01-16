# Build & Test
- Build: `dotnet build`
- Run: `dotnet run` (requires .sln in directory)
- Test: `dotnet test`
- Single Test: `dotnet test --filter "FullyQualifiedName~<Namespace.Class.Method>"`

# Code Style
- **Framework:** .NET 10. C# 14. Use `Microsoft.Build` and `Spectre.Console`.
- **Formatting:** 4 spaces indentation. Allman style braces (new line).
- **Namespaces:** Use file-scoped namespaces (`namespace My.Namespace;`).
- **Naming:** 
  - Classes/Methods/Props: `PascalCase`. 
  - Local vars: `camelCase`. 
  - Private fields: `_camelCase`.
- **Async:** Use `async/await` and append `Async` to method names.
- **Types:** Use `record` for data objects. Use always `var` instead of explicit type.
- **Nullability:** Enable nullable reference types (`<Nullable>enable</Nullable>`).
- **UI:** Use `Spectre.Console` for TUI components. 
- **Error Handling:** Use `try/catch` blocks for external ops (file I/O, build).
