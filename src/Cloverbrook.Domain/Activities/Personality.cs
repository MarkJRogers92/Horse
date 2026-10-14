using System;
using System.Collections.Generic;
using System.Linq;

namespace Cloverbrook.Domain
{
    /// <summary>One personality's presentation and effects. All text is emoji-free; the game adds any glyphs.</summary>
    public sealed class PersonalityInfo
    {
        internal PersonalityInfo(string id, string label, string description, bool lovesCarrots, IReadOnlyList<string> pipLines, IReadOnlyList<string> quirks)
        { Id = id; Label = label; Description = description; LovesCarrots = lovesCarrots; PipLines = pipLines; QuirkLines = quirks; }
        public string Id { get; }
        public string Label { get; }
        /// <summary>For example "Cuddly — loves pats and brushing".</summary>
        public string Description { get; }
        /// <summary>Greedy and Cheeky horses love carrots most.</summary>
        public bool LovesCarrots { get; }
        /// <summary>Pip tips; use {name} for the horse's name.</summary>
        public IReadOnlyList<string> PipLines { get; }
        /// <summary>Idle quirks for the yard and cards; use {name} for the horse's name.</summary>
        public IReadOnlyList<string> QuirkLines { get; }
    }

    /// <summary>
    /// D-035 horse personalities. Derived, never stored: a horse's trait comes from its id and appearance seed through Canonical.Hash,
    /// and a foal takes one parent's trait 60% of the time (deterministic from its own id), otherwise a fresh roll.
    /// Effects are small: bond gain multipliers (see <see cref="BondPermille(string, string, int)"/>) and a +1 ring-judge whim for Brave horses in the show.
    /// </summary>
    public static class Personality
    {
        public const string Version = "personality.v1";
        public const string Cuddly = "cuddly", Cheeky = "cheeky", Shy = "shy", Greedy = "greedy", Brave = "brave", Dreamy = "dreamy";
        /// <summary>Percent of foals that inherit a parent's trait.</summary>
        public const int InheritPercent = 60;
        /// <summary>A Shy horse bonds slowly up to this much bond (2 hearts), then faster.</summary>
        public const int ShyTrustBond = 4000;

        public static IReadOnlyList<string> Ids { get; } = Array.AsReadOnly(new[] { Cuddly, Cheeky, Shy, Greedy, Brave, Dreamy });

        private static readonly Dictionary<string, PersonalityInfo> Infos = new Dictionary<string, PersonalityInfo>(StringComparer.Ordinal)
        {
            [Cuddly] = new PersonalityInfo(Cuddly, "Cuddly", "Cuddly — loves pats and brushing", false,
                new[] { "{name} is Cuddly — pats and brushing grow your bond fastest.", "Cuddly horses like {name} melt for a good brush." },
                new[] { "{name} leans into you for a cuddle.", "{name} nuzzles the nearest fence post, just in case." }),
            [Cheeky] = new PersonalityInfo(Cheeky, "Cheeky", "Cheeky — loves carrots and a little mischief", true,
                new[] { "{name} is Cheeky — keep a carrot handy, and your sleeves safe.", "Cheeky horses like {name} always check your pockets." },
                new[] { "{name} tugs at your sleeve.", "{name} swishes a tail at nothing in particular." }),
            [Shy] = new PersonalityInfo(Shy, "Shy", "Shy — slow to trust, then very loyal", false,
                new[] { "{name} is Shy — bonding is slow at first, then it blossoms.", "Be patient with Shy horses like {name}: trust grows." },
                new[] { "{name} peeks out from behind the stall door.", "{name} edges a little closer." }),
            [Greedy] = new PersonalityInfo(Greedy, "Greedy", "Greedy — loves carrots and treats", true,
                new[] { "{name} is Greedy — carrots grow your bond fastest.", "Greedy horses like {name} will do anything for a festival treat." },
                new[] { "{name} sniffs hopefully for snacks.", "{name} eyes the hay with great interest." }),
            [Brave] = new PersonalityInfo(Brave, "Brave", "Brave — bold in the show ring", false,
                new[] { "{name} is Brave — the show judge likes a confident horse.", "Brave horses like {name} stand tall in the ring." },
                new[] { "{name} stands tall and proud.", "{name} trots to the gate first." }),
            [Dreamy] = new PersonalityInfo(Dreamy, "Dreamy", "Dreamy — gazes at the clouds", false,
                new[] { "{name} is Dreamy — quietly happy with any kind of company.", "Dreamy horses like {name} love a quiet afternoon." },
                new[] { "{name} gazes dreamily at the clouds.", "{name} lets out a slow, contented sigh." }),
        };

        public static PersonalityInfo Info(string id) => Infos.TryGetValue(Check.NotNull(id, nameof(id)), out var info) ? info : throw new ArgumentException("Unknown personality: " + id, nameof(id));

        public static PersonalityInfo For(StableSnapshot state, HorseRecord horse) => Infos[TraitId(state, horse)];

        public static string TraitId(StableSnapshot state, HorseRecord horse)
        {
            Check.NotNull(state, nameof(state)); Check.NotNull(horse, nameof(horse));
            return TraitId(state, horse, 0);
        }

        private static string TraitId(StableSnapshot state, HorseRecord horse, int depth)
        {
            var parents = new[] { horse.Parentage.ParentAId, horse.Parentage.ParentBId }.Where(p => p != null).Select(p => p!).ToList();
            if (parents.Count > 0 && depth < 8 && Roll(horse, "inherit", 100) < InheritPercent)
            {
                var found = parents.Select(p => state.Horses.FirstOrDefault(h => h.HorseId.Equals(p))).Where(h => h != null).Select(h => h!).ToList();
                if (found.Count > 0)
                {
                    var parent = found[Roll(horse, "parent", found.Count)];
                    return TraitId(state, parent, depth + 1);
                }
            }
            return Fresh(horse);
        }

        /// <summary>The trait a horse would have with no inheritance: from its id and appearance seed alone.</summary>
        public static string Fresh(HorseRecord horse) => Ids[Roll(Check.NotNull(horse, nameof(horse)), "fresh", Ids.Count)];

        /// <summary>True when this horse takes a parent trait (when a parent is on record) instead of a fresh roll.</summary>
        public static bool Inherits(HorseRecord horse) => Roll(horse, "inherit", 100) < InheritPercent;

        private static int Roll(HorseRecord horse, string purpose, int modulo) =>
            (int)(Convert.ToUInt32(Canonical.Hash(Version, purpose, horse.HorseId.ToString(), horse.AppearanceSeed.ToString()).Substring(0, 8), 16) % (uint)modulo);

        /// <summary>
        /// Bond gain multiplier in permille (1000 = unchanged) for one interaction kind (pat, brush, carrot, treat).
        /// Cuddly 1250 for pat/brush; Greedy 1300 for carrot/treat; Shy 800 up to 2 hearts of bond, then 1200; everyone else 1000.
        /// </summary>
        public static int BondPermille(string trait, string kind, int currentBond)
        {
            switch (trait)
            {
                case Cuddly: return kind == "pat" || kind == "brush" ? 1250 : 1000;
                case Greedy: return kind == "carrot" || kind == "treat" ? 1300 : 1000;
                case Shy: return currentBond <= ShyTrustBond ? 800 : 1200;
                default: return 1000;
            }
        }

        public static int BondPermille(StableSnapshot state, HorseRecord horse, string kind) => BondPermille(TraitId(state, horse), kind, HorseBond.Bond(horse));

        /// <summary>The ring judge's whim (0-5): Brave horses get +1, capped at 5.</summary>
        public static int ShowJudgeBonus(string trait) => trait == Brave ? 1 : 0;

        /// <summary>True when this interaction is the horse's favourite (Greedy and Cheeky love carrots and festival treats).</summary>
        public static bool LovesInteraction(string trait, string kind) => (trait == Greedy || trait == Cheeky) && (kind == "carrot" || kind == "treat");

        /// <summary>Three deterministic reaction lines for an interaction; the caller picks by day. {name} is the horse's name.</summary>
        public static IReadOnlyList<string> Reactions(string trait, string kind)
        {
            var k = kind == "treat" ? "carrot" : kind;
            switch (trait + "/" + k)
            {
                case "cuddly/pat": return new[] { "{name} leans right into your hand.", "{name} closes both eyes and sighs happily.", "{name} nuzzles your shoulder for more." };
                case "cuddly/brush": return new[] { "{name} hums contentedly as you brush.", "{name} leans into every stroke.", "{name} is a puddle of happiness." };
                case "cuddly/carrot": return new[] { "{name} crunches happily and nuzzles you.", "{name} takes the carrot gently, then asks for a cuddle.", "{name} munches with a soft snort." };
                case "cheeky/pat": return new[] { "{name} tosses their head and pats you back with a nose.", "{name} nibbles your sleeve while you pat.", "{name} snorts a cheeky puff into your hair." };
                case "cheeky/brush": return new[] { "{name} fidgets and tries to steal the brush.", "{name} swishes a tail at you, grinning.", "{name} stands still for exactly three strokes." };
                case "cheeky/carrot": return new[] { "{name} nibbles your sleeve and grabs the carrot! Loves carrots!", "{name} snatches the carrot and dances away. Loves carrots!", "{name} checks your pockets for a second one. Loves carrots!" };
                case "shy/pat": return new[] { "{name} leans in, a little braver today.", "{name} holds still, then relaxes under your hand.", "{name} blinks slowly. That is a very good sign." };
                case "shy/brush": return new[] { "{name} lets you brush a little further today.", "{name} peeks over their shoulder, then relaxes.", "{name} shivers, then stands quietly." };
                case "shy/carrot": return new[] { "{name} takes the carrot from your flat palm, very carefully.", "{name} edges forward and takes it, then retreats to chew.", "{name} nibbles the carrot, a little braver today." };
                case "greedy/pat": return new[] { "{name} accepts the pat but keeps eyeing your pockets.", "{name} tolerates pats, hoping for snacks.", "{name} sniffs your hands for crumbs." };
                case "greedy/brush": return new[] { "{name} stands still, sniffing for treats.", "{name} lets you brush while eyeing the feed bin.", "{name} perks up at the rustle of your apron." };
                case "greedy/carrot": return new[] { "{name} gobbles the carrot and begs for another - loves carrots!", "{name} loves carrots! Crunch, crunch, crunch.", "{name} whickers with delight - loves carrots!" };
                case "brave/pat": return new[] { "{name} stands tall and takes the pat proudly.", "{name} steps forward to meet you.", "{name} arches their neck proudly." };
                case "brave/brush": return new[] { "{name} stands like a statue while you brush.", "{name} shows off a gleaming shoulder.", "{name} holds a fine pose." };
                case "brave/carrot": return new[] { "{name} takes the carrot boldly.", "{name} crunches loudly and looks around, proud.", "{name} accepts the carrot like a medal." };
                case "dreamy/pat": return new[] { "{name} drifts off into a daydream under your hand.", "{name} gazes at the clouds while you pat.", "{name} blinks, as if waking from a lovely dream." };
                case "dreamy/brush": return new[] { "{name} sways gently as you brush.", "{name} half-closes both eyes, miles away.", "{name} sighs at the sky." };
                case "dreamy/carrot": return new[] { "{name} chews slowly, watching a cloud.", "{name} forgets the carrot, then remembers it.", "{name} munches thoughtfully." };
                default: return new[] { "{name} enjoys the attention." };
            }
        }
    }
}
