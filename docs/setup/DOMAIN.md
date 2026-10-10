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

No GitHub Actions workflow is installed: owner decision B5 is still pending.
The other five owner questions also remain pending; the request contained the
placeholder rather than answers. This does not block the requested headless work.
The domain work does not mark D-001 accepted or any visual class released.

Validation in the current ChatGPT container is blocked: no `dotnet` executable,
no installed SDK found, DNS failure cloning GitHub, and an official installer
download failed. Authenticated GitHub API writes provide repository checkpoints;
they are not evidence of a successful build. Run the command above before treating
this checkpoint as accepted or consuming these contracts in production.
