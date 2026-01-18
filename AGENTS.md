# Build & Test
- Build: `dotnet clean && dotnet build`. **CRITICAL:** Fix all compiler warnings.
- Run: `dotnet run` (requires .sln in directory)

# Code Style
- **Framework:** .NET 10. C# 14. Use `Microsoft.Build` and `Spectre.Console`.
- **Formatting:** 4 spaces indentation. Allman style braces (new line).
- **Namespaces:** Use file-scoped namespaces (`namespace My.Namespace;`).
- **Async:** Use `async/await` and append `Async` to method names.
- **Types:** Use `record` for data objects. Use always `var` instead of explicit type.
- **Nullability:** Enable nullable reference types (`<Nullable>enable</Nullable>`).
- **UI:** Use `Spectre.Console` for TUI components. 
- **Error Handling:** Use `try/catch` blocks for external ops (file I/O, build).
