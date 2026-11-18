# Plico

A local paperwork inbox for Windows, macOS, and Linux. Scan or import documents and find them with tags and full-text search. The library holds ordinary PDFs you can open in your usual viewer.

Plico is in development. See [the product brief](PRODUCT.md) and [roadmap](PLAN.md).

## Development

Use the .NET SDK in [global.json](global.json).

```sh
dotnet run --project src/Plico.Desktop
dotnet test Plico.slnx -c Release
```

Native verification commands are in [keel.json](keel.json).
