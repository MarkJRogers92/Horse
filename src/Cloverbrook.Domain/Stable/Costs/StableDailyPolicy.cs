namespace Cloverbrook.Domain
{
    /// <summary>Versioned prototype costs. Purchases are consumed daily; no item inventory is modeled yet.</summary>
    public sealed class StableDailyPolicy
    {
        public const string Version = "stable.dailycare.v1";
        public const long FeedCoinsPerHorse = 6;
        public const long OperationsCoinsPerDay = 24;
        public string PolicyVersion => Version;
    }
}
