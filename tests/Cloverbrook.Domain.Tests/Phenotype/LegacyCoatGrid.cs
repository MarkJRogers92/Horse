using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text;

namespace Cloverbrook.Domain.Tests
{
    /// <summary>
    /// D-037 compatibility grid: every genome the game could already resolve and release before the rarer families (no silver on a
    /// pigmented base, no champagne, no pearl expression). Their resolution must stay byte-identical. The digest was computed with the
    /// pre-D-037 resolver (commit f5ac025) from exactly this enumeration.
    /// </summary>
    internal static class LegacyCoatGrid
    {
        internal const string BaselineDigest = "0ebe22e517805d5124fce60c735e109f27ddeaaed1b630f1ccbac9c074f1a9c1";

        internal static (int Count, string Digest) Compute()
        {
            var resolver = new PhenotypeResolver(); var text = new StringBuilder(); var count = 0;
            var mc1r = new[] { ("e", "e"), ("E", "e"), ("E", "E") };
            var asip = new[] { ("A", "A"), ("a", "a") };
            var cream = new[] { ("N", "N"), ("N", "Cr"), ("Cr", "Cr"), ("N", "Prl") };
            var dun = new[] { ("nd2", "nd2"), ("D", "nd2"), ("nd1", "nd2") };
            var grey = new[] { ("N", "N"), ("G", "N") };
            var lp = new[] { ("N", "N", "N", "N"), ("LP", "N", "N", "N"), ("LP", "N", "PATN1", "N") };
            var kit = new[] { ("baseline", "baseline"), ("TO", "baseline"), ("Rn", "baseline"), ("TO", "Rn"), ("TO", "TO") };
            foreach (var m in mc1r) foreach (var a in asip) foreach (var c in cream) foreach (var d in dun) foreach (var g in grey) foreach (var l in lp) foreach (var k in kit)
            foreach (var age in new[] { VisualAge.Foal, VisualAge.Adult })
            {
                var genome = FrozenFixtureData.With(("mc1r", m.Item1, m.Item2), ("asip", a.Item1, a.Item2), ("slc45a2", c.Item1, c.Item2), ("tbx3", d.Item1, d.Item2),
                    ("stx17", g.Item1, g.Item2), ("trpm1", l.Item1, l.Item2), ("patn1", l.Item3, l.Item4), ("pattern.eca3", k.Item1, k.Item2));
                var horse = FrozenFixtureData.Horse(genome, stage: age == VisualAge.Foal ? LifeStage.Foal : LifeStage.Adult, age: age);
                var result = resolver.ResolveGenetics(horse);
                count++;
                if (result.Status != ResolutionStatus.Supported) { text.Append("UNSUPPORTED\n"); continue; }
                var p = result.Phenotype!;
                text.Append(string.Join("|", p.CoatClassId, p.DiagnosticCoatClass, p.BasePigmentRecipe, p.DilutionExpression, string.Join(",", p.CoatLayers), p.SkinExpression,
                    p.EyeExpression, p.HairExpression, p.GrayAmount, p.PrimitiveMarkingStrength, p.LpPatternPresent, p.Patn1Coverage, string.Join(",", p.BiologicalAssociationNotes),
                    string.Join(",", result.RequiredCoatClasses), PhenotypeResolver.VisibleLabel(p))).Append('\n');
            }
            var digest = string.Concat(SHA256.HashData(Encoding.UTF8.GetBytes(text.ToString())).Select(b => b.ToString("x2")));
            return (count, digest);
        }
    }
}
