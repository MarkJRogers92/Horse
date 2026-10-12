using System;

namespace Cloverbrook.Domain
{
    /// <summary>Versioned prototype costs and income. Purchases are consumed daily; no item inventory is modeled yet.</summary>
    /// <remarks>
    /// v1 charged upkeep only. v2 (D-008) adds the workflow's recurring club support arrangement:
    /// a fixed daily credit for a nonempty active stable, paid at End Day alongside upkeep. Receipts
    /// keep their own policy version, so saved v1 days stay income-free and verify unchanged.
    /// </remarks>
    public sealed class StableDailyPolicy
    {
        public const string LegacyVersion = "stable.dailycare.v1";
        public const string Version = "stable.dailycare.v2";
        public const long FeedCoinsPerHorse = 6;
        public const long OperationsCoinsPerDay = 24;
        /// <summary>The workflow's 350-coin weekly club support arrangement, paid per completed day.</summary>
        public const long ClubSupportCoinsPerDay = 50;

        public StableDailyPolicy() : this(Version) { }
        public StableDailyPolicy(string policyVersion)
        {
            if (!IsSupported(policyVersion)) throw new ArgumentException("Unknown daily-care policy version.", nameof(policyVersion));
            PolicyVersion = policyVersion;
        }
        public string PolicyVersion { get; }

        public static bool IsSupported(string? policyVersion) => policyVersion == Version || policyVersion == LegacyVersion;

        /// <summary>Club support earned for one completed day with the given number of cared-for active horses.</summary>
        public static long IncomeFor(string policyVersion, int activeHorseCount) =>
            policyVersion == Version && activeHorseCount > 0 ? ClubSupportCoinsPerDay : 0;
    }
}
