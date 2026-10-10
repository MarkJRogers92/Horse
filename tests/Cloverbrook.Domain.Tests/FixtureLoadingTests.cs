using System;
using System.Linq;
using Cloverbrook.Domain;
using Xunit;

namespace Cloverbrook.Domain.Tests
{
    public sealed class FixtureLoadingTests
    {
        [Fact]
        public void LoadsTheFrozenGeneticsFixtureFile()
        {
            using var document = FixtureFiles.LoadGenetics();
            var root = document.RootElement;
            Assert.Equal("0.1.0-proposal", root.GetProperty("formatVersion").GetString());
            var ids = root.GetProperty("geneticsFixtures").EnumerateArray()
                .Select(f => f.GetProperty("id").GetString()).ToArray();
            Assert.Equal(6, ids.Length);
            Assert.Equal(ids.Length, ids.Distinct().Count());
            Assert.Contains("syntheticLinkedBlock", ids);
            Assert.Equal(6, root.GetProperty("pedigreeFixtures")
                .GetProperty("cases").GetArrayLength());
        }

        [Fact]
        public void DomainHasNoEngineAssemblyDependency()
        {
            var references = typeof(DomainAssembly).Assembly.GetReferencedAssemblies();
            Assert.DoesNotContain(references, a =>
                (a.Name ?? string.Empty).StartsWith("Unity", StringComparison.OrdinalIgnoreCase)
                || (a.Name ?? string.Empty).StartsWith("Godot", StringComparison.OrdinalIgnoreCase));
        }
    }
}
