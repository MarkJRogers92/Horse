using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace Cloverbrook.Domain
{
    public sealed class GameTraitProfile
    {
        internal GameTraitProfile(QuantizedVector inherited, QuantizedVector developed, QuantizedVector morphology, string bodyFamily)
        { Inherited = inherited; Developed = developed; Morphology = morphology; BodyFamily = bodyFamily; }
        public QuantizedVector Inherited { get; }
        public QuantizedVector Developed { get; }
        public QuantizedVector Morphology { get; }
        public string BodyFamily { get; }
    }
    /// <summary>Invented polygenic-style game parameters, not real equine gene discoveries.</summary>
    public static class GameTraitResolver
    {
        public const string Version = "game.traits.v1";
        public static IReadOnlyList<string> TraitIds { get; } = Array.AsReadOnly(new[] {
            "height", "body_length", "chest_width", "neck_length", "muscle", "acceleration", "speed", "endurance",
            "turning", "balance", "power", "composure", "sensitivity", "sociability", "mane_density", "tail_density" });
        private static int Bound(int value, int minimum, int maximum) => Math.Min(maximum, Math.Max(minimum, value));
        public static QuantizedVector CreateDevelopment(Seed128 developmentSeed)
        {
            return new QuantizedVector(TraitIds.Select(t => new KeyValuePair<string, int>(t,
                (int)new CounterRng(developmentSeed, "development/" + t).NextBelow(401) - 200)));
        }
        public static GameTraitProfile Resolve(GenomeSnapshot genome, QuantizedVector fixedDevelopment)
        {
            CoreGeneticsCatalog.Model.ValidateGenome(genome); Check.NotNull(fixedDevelopment, nameof(fixedDevelopment));
            if (fixedDevelopment.Values.Any(p => !TraitIds.Contains(p.Key) || p.Value < -500 || p.Value > 500))
                throw new GeneticsSupportException("Unknown developmental factor or value outside this trait model's fixed bounds.");
            var inherited = TraitIds.ToDictionary(t => t, _ => 5000, StringComparer.Ordinal);
            for (var i = 0; i < 32; i++)
            {
                var dose = genome.GetAlleles("game.marker." + i.ToString("D2", CultureInfo.InvariantCulture)).Count("1") - 1;
                var trait = TraitIds[i / 2]; inherited[trait] += dose * (i % 2 == 0 ? 500 : 350);
                // Explicit fictional trade-offs; no universally best allele or generation bonus.
                if (trait == "speed") inherited["turning"] -= dose * 150;
                if (trait == "power") inherited["endurance"] -= dose * 100;
            }
            foreach (var trait in TraitIds) inherited[trait] = Bound(inherited[trait], 0, 10000);
            var developed = TraitIds.ToDictionary(t => t, t => Bound(inherited[t] + fixedDevelopment.Get(t), 0, 10000), StringComparer.Ordinal);
            var body = TraitIds.Take(5).Concat(new[] { "mane_density", "tail_density" }).ToDictionary(t => t,
                t => Bound(developed[t] - 5000, -2000, 2000), StringComparer.Ordinal);
            // Logical asset family IDs; actual mesh approval is the art track's job.
            var family = body["chest_width"] - body["body_length"] > 350 ? "horse.compact" : "horse.standard";
            return new GameTraitProfile(new QuantizedVector(inherited), new QuantizedVector(developed), new QuantizedVector(body), family);
        }
    }
}
