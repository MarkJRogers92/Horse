using System;
using System.Collections.Generic;
using System.Linq;

namespace Cloverbrook.Domain
{
    /// <summary>One recorded horse in a family tree, with only what the player can see.</summary>
    public sealed class FamilyNode
    {
        internal FamilyNode(HorseRecord horse, string visibleCoat, bool sold)
        {
            HorseId = horse.HorseId; Name = horse.Name; VisibleCoat = visibleCoat; Hearts = HorseBond.Hearts(horse); IsSold = sold;
            IsFounder = horse.Parentage.ParentAId == null && horse.Parentage.ParentBId == null; Stage = horse.LifeState.Stage; Role = horse.ReproductionState.Role;
        }
        public HorseId HorseId { get; }
        public string Name { get; }
        public string VisibleCoat { get; }
        public int Hearts { get; }
        public bool IsSold { get; }
        /// <summary>No recorded parents: a founding horse.</summary>
        public bool IsFounder { get; }
        public LifeStage Stage { get; }
        public ReproductiveRole Role { get; }
    }

    /// <summary>Up to three generations: the horse, its sire and dam, and four grandparents. An unknown slot is null.</summary>
    public sealed class FamilyTree
    {
        internal FamilyTree(FamilyNode subject, FamilyNode? sire, FamilyNode? dam, FamilyNode? sireSire, FamilyNode? sireDam, FamilyNode? damSire, FamilyNode? damDam, string? story)
        {
            Subject = subject; Sire = sire; Dam = dam; SireSire = sireSire; SireDam = sireDam; DamSire = damSire; DamDam = damDam; Story = story;
        }
        public FamilyNode Subject { get; }
        public FamilyNode? Sire { get; }
        public FamilyNode? Dam { get; }
        public FamilyNode? SireSire { get; }
        public FamilyNode? SireDam { get; }
        public FamilyNode? DamSire { get; }
        public FamilyNode? DamDam { get; }
        /// <summary>One honest sentence on where the visible coat came from, or null.</summary>
        public string? Story { get; }
        /// <summary>The four grandparent slots in sire-side then dam-side order.</summary>
        public IReadOnlyList<FamilyNode?> Grandparents => Array.AsReadOnly(new[] { SireSire, SireDam, DamSire, DamDam });

        public static FamilyTree For(StableSnapshot state, HorseId horseId)
        {
            Check.NotNull(state, nameof(state)); Check.NotNull(horseId, nameof(horseId));
            var byId = state.Horses.ToDictionary(h => h.HorseId);
            if (!byId.TryGetValue(horseId, out var horse)) throw new ArgumentException("The horse is not in this stable.", nameof(horseId));
            var resolver = new PhenotypeResolver();
            FamilyNode Node(HorseRecord h)
            {
                string coat;
                if (h.LifeState.Stage == LifeStage.Unborn) coat = "Not born yet";
                else
                {
                    var resolved = resolver.ResolveGenetics(h);
                    coat = resolved.Phenotype != null ? PhenotypeResolver.VisibleLabel(resolved.Phenotype) : "Coat not available in this version";
                }
                return new FamilyNode(h, coat, HorseMarket.IsSold(h));
            }
            HorseRecord? Find(HorseId? id) => id != null && byId.TryGetValue(id, out var found) ? found : null;
            // The dam is the mare; if the roles do not tell them apart, the recorded order is kept (sire first).
            (HorseRecord? Sire, HorseRecord? Dam) Parents(HorseRecord h)
            {
                var a = Find(h.Parentage.ParentAId); var b = Find(h.Parentage.ParentBId);
                if (a != null && b != null) return a.ReproductionState.Role == ReproductiveRole.Mare && b.ReproductionState.Role != ReproductiveRole.Mare ? (b, a) : (a, b);
                if (a == null && b == null) return (null, null);
                var only = a ?? b!;
                return only.ReproductionState.Role == ReproductiveRole.Mare ? (null, only) : (only, null);
            }
            var (sire, dam) = Parents(horse);
            (HorseRecord? Sire, HorseRecord? Dam) none = (null, null);
            var (ss, sd) = sire == null ? none : Parents(sire);
            var (ds, dd) = dam == null ? none : Parents(dam);
            FamilyNode? N(HorseRecord? h) => h == null ? null : Node(h);
            return new FamilyTree(Node(horse), N(sire), N(dam), N(ss), N(sd), N(ds), N(dd), StoryFor(horse, sire, dam));
        }

        private static string? StoryFor(HorseRecord horse, HorseRecord? sire, HorseRecord? dam)
        {
            if (horse.LifeState.Stage == LifeStage.Unborn) return null;
            CoatStoryParent? Parent(HorseRecord? p, string role) => p == null || p.LifeState.Stage == LifeStage.Unborn ? null
                : new CoatStoryParent(p.Name, role, CoatStory.VisibleTraits(p), CoatStory.TestedCarries(p), DnaTest.IsTested(p));
            return CoatStory.Pick(CoatStory.Pronoun(horse.ReproductionState.Role), CoatStory.VisibleTraits(horse), Parent(sire, "sire"), Parent(dam, "dam"));
        }
    }
}
