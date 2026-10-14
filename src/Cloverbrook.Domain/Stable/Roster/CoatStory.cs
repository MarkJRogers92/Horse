using System;
using System.Collections.Generic;
using System.Linq;

namespace Cloverbrook.Domain
{
    /// <summary>One parent as the coat story sees it: its name, whether it is the sire or dam, the traits it visibly shows, and
    /// the traits it is known to carry only when it has had a DNA test (otherwise <see cref="Carries"/> equals what is visible).</summary>
    public sealed class CoatStoryParent
    {
        public CoatStoryParent(string name, string role, IEnumerable<string> shows, IEnumerable<string>? carries = null, bool tested = false)
        {
            Name = Check.Text(name, nameof(name)); Role = Check.Text(role, nameof(role));
            Shows = new HashSet<string>(Check.NotNull(shows, nameof(shows)), StringComparer.Ordinal);
            Carries = new HashSet<string>(carries ?? Shows, StringComparer.Ordinal);
            foreach (var shown in Shows) Carries.Add(shown);
            Tested = tested;
        }
        public string Name { get; }
        /// <summary>"sire" or "dam".</summary>
        public string Role { get; }
        public ISet<string> Shows { get; }
        public ISet<string> Carries { get; }
        public bool Tested { get; }
    }

    /// <summary>
    /// D-034: one honest sentence about where a horse's visible coat traits came from. It uses only what the player can see on the
    /// parents (their visible coat), plus hidden genes of a parent only when that parent has had a DNA test.
    /// </summary>
    public static class CoatStory
    {
        public const string Tobiano = "tobiano", Roan = "roan", Grey = "grey", Leopard = "leopard", Dun = "dun", Cream = "cream", Chestnut = "chestnut";
        /// <summary>The traits told about, in the order they are looked at.</summary>
        public static IReadOnlyList<string> Traits { get; } = Array.AsReadOnly(new[] { Tobiano, Roan, Grey, Leopard, Dun, Cream, Chestnut });

        private static readonly Dictionary<string, string> Labels = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            [Tobiano] = "Tobiano", [Roan] = "Roan", [Grey] = "Grey", [Leopard] = "Leopard spotting", [Dun] = "Dun", [Cream] = "Cream", [Chestnut] = "Chestnut",
        };

        /// <summary>The traits visible on a born horse (nothing for an unborn or unresolvable one).</summary>
        public static IReadOnlyList<string> VisibleTraits(HorseRecord horse)
        {
            Check.NotNull(horse, nameof(horse));
            if (horse.LifeState.Stage == LifeStage.Unborn) return Array.Empty<string>();
            var p = new PhenotypeResolver().ResolveGenetics(horse).Phenotype;
            if (p == null) return Array.Empty<string>();
            var found = new List<string>();
            if (p.CoatLayers.Contains("white_tobiano")) found.Add(Tobiano);
            if (p.CoatLayers.Contains("roan")) found.Add(Roan);
            if (p.CoatLayers.Contains("grey_age")) found.Add(Grey);
            if (p.CoatLayers.Contains("leopard_complex")) found.Add(Leopard);
            if (p.CoatLayers.Contains("dun_dilution")) found.Add(Dun);
            if (p.CoatLayers.Contains("cream_single") || p.CoatLayers.Contains("cream_double")) found.Add(Cream);
            if (p.BaseCoatClass == "chestnut" && !p.CoatLayers.Contains("grey_age")) found.Add(Chestnut);
            return found;
        }

        /// <summary>Hidden carried traits from recorded DNA calls; empty unless the horse is tested.</summary>
        public static IReadOnlyList<string> TestedCarries(HorseRecord horse)
        {
            Check.NotNull(horse, nameof(horse));
            if (!DnaTest.IsTested(horse)) return Array.Empty<string>();
            var found = new List<string>();
            if (horse.PlayerKnowledge.RevealedCalls.TryGetValue("slc45a2", out var cr) && (cr.A == "Cr" || cr.B == "Cr")) found.Add(Cream);
            if (horse.PlayerKnowledge.RevealedCalls.TryGetValue("mc1r", out var e) && (e.A == "e" || e.B == "e")) found.Add(Chestnut);
            return found;
        }

        public static string Pronoun(ReproductiveRole role) => role == ReproductiveRole.Mare ? "her" : role == ReproductiveRole.Stallion ? "his" : "their";

        /// <summary>The coat story for a horse showing <paramref name="foalShows"/>, or null when there is nothing honest to say.
        /// A missing parent is passed as null; a founder (no parents at all) has no story.</summary>
        public static string? Pick(string pronoun, IEnumerable<string> foalShows, CoatStoryParent? sire, CoatStoryParent? dam)
        {
            Check.NotNull(foalShows, nameof(foalShows)); Check.Text(pronoun, nameof(pronoun));
            var shown = new HashSet<string>(foalShows, StringComparer.Ordinal);
            var parents = new[] { sire, dam }.Where(p => p != null).Select(p => p!).ToList();
            if (parents.Count == 0) return null;
            foreach (var trait in Traits.Where(shown.Contains))
            {
                var line = Story(trait, pronoun, parents, parents.Count == 2);
                if (line != null) return line;
            }
            return null;
        }

        private static string? Story(string trait, string pronoun, List<CoatStoryParent> parents, bool complete)
        {
            var label = Labels[trait];
            var showing = parents.Where(p => p.Shows.Contains(trait)).ToList();
            if (showing.Count == 2) return $"{label}, from both {showing[0].Name} and {showing[1].Name}.";
            if (trait == Chestnut)
            {
                // Red (e/e) needs a red gene from each parent: a showing parent gives one, so the other carried it.
                if (showing.Count == 1) return $"{label}, like {pronoun} {showing[0].Role} {showing[0].Name}, and the other parent carried red too.";
                if (!complete) return null;
                return parents.All(p => p.Tested && p.Carries.Contains(trait))
                    ? "A surprise chestnut: the DNA tests show both parents carried red."
                    : "A surprise chestnut: neither parent showed red, so both carried it hidden.";
            }
            if (showing.Count == 1) return $"{label}, like {pronoun} {showing[0].Role} {showing[0].Name}.";
            if (!complete) return null;
            if (trait == Cream)
            {
                var carrier = parents.FirstOrDefault(p => p.Tested && p.Carries.Contains(trait));
                return carrier != null ? $"A surprise cream: the DNA test shows {carrier.Name} carried it hidden."
                    : "A surprise cream: neither parent showed it, so it was carried hidden.";
            }
            return $"A surprise {label.ToLowerInvariant()}: neither parent showed it.";
        }
    }
}
