using System;
using System.IO;
using System.Text.Json;

namespace Cloverbrook.Domain.Tests
{
    internal static class FixtureFiles
    {
        // MSBuild copies the authoritative repository files; never depend on CWD.
        internal static JsonDocument LoadGenetics()
        {
            return Load("GENETICS_FIXTURES.json");
        }

        internal static JsonDocument Load(string name)
        {
            return JsonDocument.Parse(File.ReadAllText(
                Path.Combine(AppContext.BaseDirectory, "Fixtures", name)));
        }
    }
}
