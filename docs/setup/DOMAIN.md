# Headless domain development

P0-02a uses `src/Cloverbrook.Domain` (netstandard2.1, C# 9, nullable) and
`tests/Cloverbrook.Domain.Tests` (net8.0, xUnit). No Unity installation is needed.
Install a supported .NET 8 SDK and allow NuGet package restore on your machine.
From the repository root:

```sh
dotnet --info
dotnet test Cloverbrook.sln --configuration Release
python3 tools/validate_handoff.py
```

Test dependencies are pinned to the .NET 8 example documented by xUnit:
https://xunit.net/docs/getting-started/v2/getting-started
They are test-only. The domain has no external package dependency.

The test project copies the frozen genetics fixtures and current coat matrix to
its output directory. Tests read those copies relative to AppContext.BaseDirectory,
so running from an IDE or another working directory uses the same source data.
Do not regenerate or weaken the fixtures to make implementation results pass.

CI: `.github/workflows/domain.yml` runs the validator and `dotnet test` on every push
(owner-approved 2026-10-10). Its run is the accepted evidence when a local environment
has no .NET SDK. Check the Actions tab, or the commit's check status, for the result.
