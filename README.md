# Printable Book

Printable Book is a Windows desktop application for preparing printable colouring-book assets. It uses a modular monolith architecture with a platform-independent Core, technical Infrastructure adapters, and a WPF/WebView2 presentation host.

## Development status

Phase 1 Foundation is in review. The repository now has platform-neutral domain/configuration models, technical adapter contracts, a pipeline/application boundary, an isolated WebView2 bridge, and test foundations. Real image/PDF processing remains deferred to Phase 2.

## Development prerequisites

- .NET 10 SDK
- Windows, for the WPF desktop host

## Test commands

```powershell
dotnet restore PrintableBook.sln
dotnet build PrintableBook.sln --configuration Release --no-restore
dotnet test tests/PrintableBook.Core.Tests/PrintableBook.Core.Tests.csproj --configuration Release --no-build
dotnet test tests/PrintableBook.Infrastructure.Tests/PrintableBook.Infrastructure.Tests.csproj --configuration Release --no-build
dotnet test tests/PrintableBook.Desktop.Tests/PrintableBook.Desktop.Tests.csproj --configuration Release --no-build
node --test tests/PrintableBook.Desktop.Bridge.Tests/app-bridge.test.mjs
```
