using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Numerics;
using System.Security.Cryptography;
using System.Text;

namespace Cloverbrook.Domain
{
    public interface ISavePayloadMigration
    {
        string Id { get; }
        int FromWireVersion { get; }
        int ToWireVersion { get; }
        byte[] Upgrade(byte[] verifiedOldPayload);
    }

    // Explicit bounded data, never BinaryFormatter, runtime type names, reflection
    // activation or genotype regeneration. Existing immutable constructors and
    // cross-record invariants validate every rehydrated record.
    public sealed class StableSaveCodec
    {
        public const int WireVersion = 1;
        public const int FrameHeaderBytes = 48;
        public const int MaxPayloadBytes = 32 * 1024 * 1024;
        public const int MaxCollectionCount = 4096;
        public const int MaxTextBytes = 16384;
        private static readonly byte[] magic = Encoding.ASCII.GetBytes("CLVRSAVE");
        private static readonly UTF8Encoding utf8 = new UTF8Encoding(false, true);
        private readonly IReadOnlyDictionary<int, ISavePayloadMigration> migrations;
        public StableSaveCodec(IEnumerable<ISavePayloadMigration>? migrations = null)
        {
            var map = new Dictionary<int, ISavePayloadMigration>();
            foreach (var migration in migrations ?? Array.Empty<ISavePayloadMigration>())
            {
                Check.NotNull(migration, nameof(migrations)); Check.Text(migration.Id, nameof(migrations));
                if (migration.FromWireVersion < 0 || migration.ToWireVersion <= migration.FromWireVersion || migration.ToWireVersion > WireVersion
                    || map.ContainsKey(migration.FromWireVersion) || map.Values.Any(m => m.Id == migration.Id)) throw new ArgumentException("Migration routes must be unique, named and strictly forward.");
                map.Add(migration.FromWireVersion, migration);
            }
            this.migrations = map;
        }
        public byte[] Encode(StableSnapshot state)
        {
            Check.NotNull(state, nameof(state));
            if (state.Header.SchemaVersion != StableSnapshot.SchemaVersion) throw new SaveCompatibilityException("No writer is installed for schema " + state.Header.SchemaVersion);
            var payload = EncodePayload(state);
            using var output = new MemoryStream();
            using (var writer = new BinaryWriter(output, utf8, true))
            { writer.Write(magic); writer.Write(WireVersion); writer.Write(payload.Length); writer.Write(Digest(payload)); writer.Write(payload); }
            return output.ToArray();
        }
        public StableSnapshot Decode(byte[] fileBytes)
        {
            Check.NotNull(fileBytes, nameof(fileBytes));
            if (fileBytes.Length < FrameHeaderBytes || fileBytes.Length > MaxPayloadBytes + FrameHeaderBytes) throw new SaveIntegrityException("Invalid save frame length.");
            try
            {
                using var input = new MemoryStream(fileBytes, false); using var reader = new BinaryReader(input, utf8, true);
                if (!reader.ReadBytes(magic.Length).SequenceEqual(magic)) throw new SaveIntegrityException("Unrecognized save signature.");
                var version = reader.ReadInt32(); var length = reader.ReadInt32();
                if (version < 0 || length < 0 || length > MaxPayloadBytes || length != fileBytes.Length - FrameHeaderBytes) throw new SaveIntegrityException("Invalid save version or payload length.");
                var checksum = reader.ReadBytes(32); var payload = ReadBytes(reader, length);
                if (!checksum.SequenceEqual(Digest(payload))) throw new SaveIntegrityException("Save checksum mismatch; the payload will not be guessed or repaired.");
                if (version > WireVersion) throw new SaveCompatibilityException("This save was written by a newer wire format. Keep it intact and use a compatible build.");
                var applied = new List<string>();
                while (version < WireVersion)
                {
                    if (!migrations.TryGetValue(version, out var migration)) throw new SaveCompatibilityException("No reviewed migration is installed from wire format " + version.ToString(CultureInfo.InvariantCulture));
                    payload = Check.NotNull(migration.Upgrade((byte[])payload.Clone()), nameof(migrations));
                    if (payload.Length > MaxPayloadBytes) throw new SaveIntegrityException("A migration produced an oversized payload.");
                    applied.Add(migration.Id); version = migration.ToWireVersion;
                }
                var state = DecodePayload(payload);
                if (applied.Count != 0)
                {
                    if (applied.Any(id => state.Header.MigrationHistory.Contains(id))) throw new SaveIntegrityException("A migration was already applied to this payload.");
                    state = state.WithHeader(SaveHeaders.Copy(state.Header, history: state.Header.MigrationHistory.Concat(applied)));
                }
                // PayloadHash is derived from bytes and omitted from those same
                // bytes, avoiding a recursive checksum. No game state is rerolled.
                var currentPayload = applied.Count == 0 ? payload : EncodePayload(state);
                return state.WithHeader(SaveHeaders.Copy(state.Header, payloadHash: Hex(Digest(currentPayload))));
            }
            catch (Exception e) when (e is EndOfStreamException || e is ArgumentException || e is FormatException || e is OverflowException || e is GeneticsSupportException)
            { throw new SaveIntegrityException("Malformed saved data; no partial state was loaded.", e); }
        }
        private static byte[] Digest(byte[] bytes) { using var sha = SHA256.Create(); return sha.ComputeHash(bytes); }
        private static string Hex(byte[] bytes) => string.Concat(bytes.Select(b => b.ToString("x2", CultureInfo.InvariantCulture)));
        private static void Budget(BinaryWriter w) { if (w.BaseStream.Length > MaxPayloadBytes) throw new SaveIntegrityException("Save payload exceeds the bounded format."); }
        private static void Text(BinaryWriter w, string value)
        {
            if (value == null) throw new ArgumentNullException(nameof(value));
            if (utf8.GetByteCount(value) > MaxTextBytes) throw new SaveIntegrityException("A text field is too long.");
            var bytes = utf8.GetBytes(value); w.Write(bytes.Length); w.Write(bytes); Budget(w);
        }
        private static string Text(BinaryReader r, int maximum = MaxTextBytes)
        {
            var length = r.ReadInt32();
            if (length < 0 || length > maximum || length > r.BaseStream.Length - r.BaseStream.Position) throw new SaveIntegrityException("Invalid bounded UTF-8 field.");
            return utf8.GetString(ReadBytes(r, length));
        }
        private static byte[] ReadBytes(BinaryReader r, int length)
        { var bytes = r.ReadBytes(length); if (bytes.Length != length) throw new EndOfStreamException(); return bytes; }
        private static bool Flag(BinaryReader r)
        { var value = r.ReadByte(); if (value > 1) throw new SaveIntegrityException("Invalid boolean encoding."); return value == 1; }
        private static void OptionalText(BinaryWriter w, string? value) { w.Write(value != null); if (value != null) Text(w, value); }
        private static string? OptionalText(BinaryReader r) => Flag(r) ? Text(r) : null;
        private static void OptionalDay(BinaryWriter w, int? value) { w.Write(value.HasValue); if (value.HasValue) w.Write(value.Value); }
        private static int? OptionalDay(BinaryReader r) => Flag(r) ? r.ReadInt32() : (int?)null;
        private static Guid Id(BinaryReader r) => Guid.Parse(Text(r, 64));
        private static void List<T>(BinaryWriter w, IEnumerable<T> source, Action<BinaryWriter, T> write, int maximum = MaxCollectionCount)
        {
            var values = Snapshot.List(source); if (values.Count > maximum) throw new SaveIntegrityException("Collection exceeds the save-format limit.");
            w.Write(values.Count); foreach (var value in values) { write(w, value); Budget(w); }
        }
        private static T[] List<T>(BinaryReader r, Func<BinaryReader, T> read, int maximum = MaxCollectionCount)
        {
            var count = r.ReadInt32(); if (count < 0 || count > maximum) throw new SaveIntegrityException("Invalid bounded collection length.");
            var values = new T[count]; for (var i = 0; i < count; i++) values[i] = read(r); return values;
        }
        private static T EnumValue<T>(BinaryReader r) where T : struct, Enum
        { var value = r.ReadInt32(); if (!Enum.IsDefined(typeof(T), value)) throw new SaveIntegrityException("Unknown enum value in current schema."); return (T)Enum.ToObject(typeof(T), value); }
        private static void Seed(BinaryWriter w, Seed128 s) { w.Write(s.High); w.Write(s.Low); }
        private static Seed128 Seed(BinaryReader r) => new Seed128(r.ReadUInt64(), r.ReadUInt64());
        private static void Fraction(BinaryWriter w, Rational value) { Text(w, value.Numerator.ToString(CultureInfo.InvariantCulture)); Text(w, value.Denominator.ToString(CultureInfo.InvariantCulture)); }
        private static Rational Fraction(BinaryReader r) => new Rational(BigInteger.Parse(Text(r, 512), CultureInfo.InvariantCulture), BigInteger.Parse(Text(r, 512), CultureInfo.InvariantCulture));
        private static void StreamState(BinaryWriter w, RngStreamState state) { Seed(w, state.Seed); Text(w, state.Purpose); w.Write(state.EventCounter); Text(w, state.AlgorithmVersion); }
        private static RngStreamState StreamState(BinaryReader r) => new RngStreamState(Seed(r), Text(r), r.ReadUInt64(), Text(r));
        private static void Header(BinaryWriter w, SaveHeader h)
        {
            Text(w, h.SaveId.ToString()); w.Write((int)h.SaveMode); Text(w, h.SchemaVersion); Text(w, h.GameBuildVersion); w.Write(h.SaveRevision);
            w.Write(h.CalendarDay); Text(w, h.CalendarProfileVersion); w.Write((int)h.CareMode); Text(w, h.ActiveVariantCatalogVersion);
            Text(w, h.ActiveInheritanceModelVersion); Text(w, h.DefaultOffspringResolverVersion); List(w, h.RetainedResolverVersions, Text);
            StreamState(w, h.RngStreams.Breeding); StreamState(w, h.RngStreams.Cosmetic); StreamState(w, h.RngStreams.World); StreamState(w, h.RngStreams.Competition);
            w.Write(h.LastCommittedBreedingEventIndex); List(w, h.MigrationHistory, Text);
        }
        private static SaveHeader Header(BinaryReader r)
        {
            var id = new SaveId(Id(r)); var mode = EnumValue<SaveMode>(r); var schema = Text(r);
            if (schema != StableSnapshot.SchemaVersion) throw new SaveCompatibilityException("No payload reader is installed for schema " + schema);
            var build = Text(r); var revision = r.ReadUInt64(); var day = r.ReadInt32(); var calendar = Text(r); var care = EnumValue<CareMode>(r);
            var catalog = Text(r); var inheritance = Text(r); var resolver = Text(r); var retained = List(r, x => Text(x));
            var streams = new RngStreams(StreamState(r), StreamState(r), StreamState(r), StreamState(r));
            return new SaveHeader(id, mode, schema, build, revision, day, calendar, care, catalog, inheritance, resolver, retained, streams,
                r.ReadUInt64(), List(r, x => Text(x)), "unverified-until-frame-check");
        }
        private static void Vector(BinaryWriter w, QuantizedVector vector) => List(w, vector.Values, (x, p) => { Text(x, p.Key); x.Write(p.Value); }, 256);
        private static QuantizedVector Vector(BinaryReader r) => new QuantizedVector(List(r, x => new KeyValuePair<string, int>(Text(x), x.ReadInt32()), 256));
        private static void Genome(BinaryWriter w, GenomeSnapshot genome)
        {
            Text(w, genome.VariantCatalogVersion); Text(w, genome.SyntheticModelVersion);
            List(w, genome.Blocks, (x, b) => { Text(x, b.BlockId); x.Write(b.IsSynthetic); List(x, b.LocusIds, Text, 128); List(x, b.HomologA, Text, 128); List(x, b.HomologB, Text, 128); }, 128);
            Text(w, genome.GenomeHash);
        }
        private static GenomeSnapshot Genome(BinaryReader r)
        {
            var catalog = Text(r); var synthetic = Text(r);
            var blocks = List(r, x => new PhasedBlockPair(Text(x), Flag(x), List(x, y => Text(y), 128), List(x, y => Text(y), 128), List(x, y => Text(y), 128)), 128);
            var genome = new GenomeSnapshot(blocks, catalog, synthetic);
            if (genome.GenomeHash != Text(r, 128)) throw new SaveIntegrityException("Stored genome digest disagrees with its phased alleles.");
            if (catalog == CoreGeneticsCatalog.CatalogVersion && synthetic == CoreGeneticsCatalog.SyntheticVersion) CoreGeneticsCatalog.Model.ValidateGenome(genome);
            return genome;
        }
        private static void Gamete(BinaryWriter w, GameteSnapshot gamete)
        {
            Text(w, gamete.ParentId.ToString()); Text(w, gamete.VariantCatalogVersion); Text(w, gamete.SyntheticModelVersion); Text(w, gamete.InheritanceModelVersion);
            List(w, gamete.Blocks, (x, b) => { Text(x, b.BlockId); x.Write(b.IsSynthetic); List(x, b.LocusIds, Text, 128); List(x, b.Alleles, Text, 128); List(x, b.SourceHomologIndices, (y, v) => y.Write(v), 128); }, 128);
            Text(w, gamete.ProvenanceHash);
        }
        private static GameteSnapshot Gamete(BinaryReader r)
        {
            var parent = new HorseId(Id(r)); var catalog = Text(r); var synthetic = Text(r); var inheritance = Text(r);
            var blocks = List(r, x => new GameteBlock(Text(x), Flag(x), List(x, y => Text(y), 128), List(x, y => Text(y), 128), List(x, y => y.ReadInt32(), 128)), 128);
            var gamete = new GameteSnapshot(parent, blocks, catalog, synthetic, inheritance);
            if (gamete.ProvenanceHash != Text(r, 128)) throw new SaveIntegrityException("Stored gamete provenance digest mismatch.");
            return gamete;
        }
        private static void Horse(BinaryWriter w, HorseRecord h)
        {
            Text(w, h.HorseId.ToString()); Text(w, h.Name); OptionalText(w, h.Parentage.ParentAId?.ToString()); OptionalText(w, h.Parentage.ParentBId?.ToString());
            w.Write(h.Parentage.FounderBackground != null);
            if (h.Parentage.FounderBackground != null) { Text(w, h.Parentage.FounderBackground.AssumptionId); Fraction(w, h.Parentage.FounderBackground.InbreedingCoefficient); }
            Genome(w, h.Genome); Text(w, h.ResolverVersion); Seed(w, h.AppearanceSeed); Text(w, h.OwnerId.ToString());
            w.Write((int)h.LifeState.Stage); w.Write(h.LifeState.DaysSinceConception); w.Write((int)h.LifeState.VisualAge);
            w.Write((int)h.ReproductionState.Role); OptionalText(w, h.ReproductionState.PregnancyId?.ToString()); w.Write(h.ReproductionState.RecoveryUntilDay);
            Vector(w, h.DevelopmentalFactors); Vector(w, h.InheritedGamePotential); Vector(w, h.TrainingState);
            List(w, h.PlayerKnowledge.RevealedCalls, (x, p) => { Text(x, p.Key); Text(x, p.Value.A); Text(x, p.Value.B); }, 256);
            List(w, h.PlayerKnowledge.KnownAncestors, (x, id) => Text(x, id.ToString()));
            List(w, h.CosmeticState, (x, p) => { Text(x, p.Key); Text(x, p.Value); }, 256);
            w.Write((int)h.HomeAssignment); Text(w, h.OriginRecord); OptionalText(w, h.OriginCommitId?.ToString()); OptionalDay(w, h.BirthDay); w.Write(h.Revision);
        }
        private static HorseRecord Horse(BinaryReader r)
        {
            var id = new HorseId(Id(r)); var name = Text(r); var pa = OptionalText(r); var pb = OptionalText(r);
            var background = Flag(r) ? new FounderBackground(Text(r), Fraction(r)) : null;
            var parentage = new Parentage(pa == null ? null : new HorseId(Guid.Parse(pa)), pb == null ? null : new HorseId(Guid.Parse(pb)), background);
            var genome = Genome(r); var resolver = Text(r); var appearance = Seed(r); var owner = new OwnerId(Id(r));
            var life = new LifeState(EnumValue<LifeStage>(r), r.ReadInt32(), EnumValue<VisualAge>(r));
            var role = EnumValue<ReproductiveRole>(r); var pregnancy = OptionalText(r); var recovery = r.ReadInt32();
            var reproduction = new ReproductionState(role, pregnancy == null ? null : new BreedingCommitId(Guid.Parse(pregnancy)), recovery);
            var development = Vector(r); var potential = Vector(r); var training = Vector(r);
            var calls = List(r, x => new KeyValuePair<string, AllelePair>(Text(x), new AllelePair(Text(x), Text(x))), 256);
            var ancestors = List(r, x => new HorseId(Id(x))); var knowledge = new PlayerKnowledgeRecord(calls, ancestors);
            var cosmetics = List(r, x => new KeyValuePair<string, string>(Text(x), Text(x)), 256); var home = EnumValue<HomeKind>(r);
            var origin = Text(r); var originCommit = OptionalText(r); var birth = OptionalDay(r); var revision = r.ReadUInt64();
            return new HorseRecord(id, name, parentage, genome, resolver, appearance, owner, life, reproduction, development, potential, training, knowledge,
                cosmetics, home, origin, originCommit == null ? null : new BreedingCommitId(Guid.Parse(originCommit)), birth, revision);
        }
        private static void Capacity(BinaryWriter w, BreedingCapacityPlan plan)
        {
            Text(w, plan.BreedingId.ToString()); Text(w, plan.ReservationId.ToString()); w.Write(plan.CapacityRevision);
            List(w, plan.Windows, (x, v) => { x.Write((int)v.Stage); x.Write(v.StartDay); OptionalDay(x, v.EndDayExclusive); x.Write(v.TotalSlots); x.Write(v.OccupiedSlots); x.Write(v.ReservationsForOthers); x.Write(v.ReservedForThisBreeding); });
        }
        private static BreedingCapacityPlan Capacity(BinaryReader r) => new BreedingCapacityPlan(new BreedingCommitId(Id(r)), new CapacityReservationId(Id(r)), r.ReadUInt64(),
            List(r, x => new ReservedCapacityWindow(EnumValue<CapacityStage>(x), x.ReadInt32(), OptionalDay(x), x.ReadInt32(), x.ReadInt32(), x.ReadInt32(), Flag(x))));
        private static void Request(BinaryWriter w, ConceptionRequest q)
        {
            Text(w, q.EventId.ToString()); Text(w, q.Requester.ToString()); Text(w, q.ParentAId.ToString()); Text(w, q.ParentBId.ToString());
            w.Write(q.ExpectedSaveRevision); w.Write(q.ExpectedParentARevision); w.Write(q.ExpectedParentBRevision); w.Write(q.ExpectedCapacityRevision);
            Text(w, q.ReservationId.ToString()); Text(w, q.TermsVersion); Text(w, q.RequestHash);
        }
        private static ConceptionRequest Request(BinaryReader r)
        {
            var q = new ConceptionRequest(new BreedingCommitId(Id(r)), new OwnerId(Id(r)), new HorseId(Id(r)), new HorseId(Id(r)), r.ReadUInt64(), r.ReadUInt64(), r.ReadUInt64(), r.ReadUInt64(), new CapacityReservationId(Id(r)), Text(r));
            if (q.RequestHash != Text(r, 128)) throw new SaveIntegrityException("Stored command identity does not match its parameters."); return q;
        }
        private static void Receipt(BinaryWriter w, PreparedConception receipt)
        {
            Request(w, receipt.Request); Text(w, receipt.Terms.Version); w.Write(receipt.Terms.CostCoins); Capacity(w, receipt.Capacity);
            Horse(w, receipt.ParentAAtCommit); Horse(w, receipt.ParentBAtCommit); Horse(w, receipt.UnbornAtCommit);
            var c = receipt.Commit; Gamete(w, c.GameteA); Gamete(w, c.GameteB); Seed(w, c.BreedingSeed); w.Write(c.BreedingEventIndex);
            w.Write(c.ConceptionDay); w.Write(c.PlannedBirthDay); Text(w, c.CalendarProfileVersion);
            List(w, c.ReservationIds, (x, id) => Text(x, id.ToString())); Text(w, c.CostTransactionId.ToString()); Text(w, c.CompatibilityRuleVersion);
            OptionalText(w, c.ForecastReference?.ToString()); StreamState(w, receipt.BeforeBreedingStream); Text(w, c.CommitHash); Text(w, receipt.ReceiptHash);
        }
        private static PreparedConception Receipt(BinaryReader r)
        {
            var request = Request(r); var terms = new ConceptionTerms(Text(r), r.ReadInt64()); var capacity = Capacity(r);
            var a = Horse(r); var b = Horse(r); var child = Horse(r); var ga = Gamete(r); var gb = Gamete(r); var seed = Seed(r); var index = r.ReadUInt64();
            var day = r.ReadInt32(); var birth = r.ReadInt32(); var calendar = Text(r); var holds = List(r, x => new CapacityReservationId(Id(x)));
            var cost = new TransactionId(Id(r)); var policy = Text(r); var forecast = OptionalText(r); var before = StreamState(r);
            var commitHash = Text(r, 128); var receiptHash = Text(r, 128);
            if (ga.InheritanceModelVersion != CoreGeneticsCatalog.InheritanceVersion || gb.InheritanceModelVersion != CoreGeneticsCatalog.InheritanceVersion
                || a.Genome.VariantCatalogVersion != CoreGeneticsCatalog.CatalogVersion || b.Genome.VariantCatalogVersion != CoreGeneticsCatalog.CatalogVersion
                || a.Genome.SyntheticModelVersion != CoreGeneticsCatalog.SyntheticVersion || b.Genome.SyntheticModelVersion != CoreGeneticsCatalog.SyntheticVersion
                || before.AlgorithmVersion != Seeds.AlgorithmVersion)
                throw new SaveCompatibilityException("The save requires an older or newer conception implementation. No implicit genetics migration is permitted.");
            var commit = new BreedingCommit(request.EventId, a, b, child, ga, gb, seed, index, day, birth, calendar, holds, cost, policy,
                forecast == null ? null : new ForecastId(Guid.Parse(forecast)));
            var receipt = new PreparedConception(request, terms, capacity, a, b, child, commit, before);
            if (commit.CommitHash != commitHash || receipt.ReceiptHash != receiptHash) throw new SaveIntegrityException("Conception evidence did not round-trip exactly.");
            return receipt;
        }
        private static byte[] EncodePayload(StableSnapshot state)
        {
            using var output = new MemoryStream(); using (var w = new BinaryWriter(output, utf8, true))
            {
                Header(w, state.Header); Text(w, state.PlayerOwnerId.ToString()); w.Write(state.OpeningCoinBalance); w.Write(state.CapacityRevision);
                List(w, state.Horses, Horse); List(w, state.Ledger, (x, entry) => { Text(x, entry.Id.ToString()); x.Write(entry.DeltaCoins); Text(x, entry.Kind); OptionalText(x, entry.BreedingId?.ToString()); });
                List(w, state.PendingCapacityPlans, Capacity); List(w, state.Conceptions, Receipt); Budget(w);
            }
            return output.ToArray();
        }
        private static StableSnapshot DecodePayload(byte[] payload)
        {
            using var input = new MemoryStream(payload, false); using var r = new BinaryReader(input, utf8, true);
            var header = Header(r); var owner = new OwnerId(Id(r)); var opening = r.ReadInt64(); var capacityRevision = r.ReadUInt64(); var horses = List(r, Horse);
            var ledger = List(r, x => { var id = new TransactionId(Id(x)); var delta = x.ReadInt64(); var kind = Text(x); var breeding = OptionalText(x);
                return new CoinLedgerEntry(id, delta, kind, breeding == null ? null : new BreedingCommitId(Guid.Parse(breeding))); });
            var pending = List(r, Capacity); var conceptions = List(r, Receipt);
            if (input.Position != input.Length) throw new SaveIntegrityException("Unexpected trailing payload data; no fields are silently discarded.");
            return new StableSnapshot(header, owner, horses, opening, capacityRevision, ledger, pending, conceptions);
        }
    }
}
