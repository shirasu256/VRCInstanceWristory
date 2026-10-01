using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;
using VRCInstanceWristory.Core.Locations;
using VRCInstanceWristory.Core.Visits;

namespace VRCInstanceWristory.Core.History;

/// <summary>
/// 訪問履歴の保存先（<c>%LocalAppData%\VRCInstanceWristory\history.json</c>・2026-10-01のユーザー指定→実装メモ5.108）。
///
/// 履歴はふだんログから組み直すが、VRChat は24時間より古いログを消すので、自動リセットを無効にして
/// ため込んだ行はログからは戻せない。そこで、いま持っている行をそのまま書いておき、自動リセットが無効のときは起動時に戻す。
/// 中身は行（<see cref="VisitRecord"/>）の写しで、一緒にいた人・写真・回数も含む。
///
/// 保存は一時ファイルからの置換で行い、途中で落ちても壊れた中身を残さない（<see cref="Counting.CheckpointStore"/> と同じ）。
/// </summary>
public sealed class HistoryFile(string path)
{
    public const int CurrentSchemaVersion = 1;

    // 行が多いと大きくなるので字下げしない。日本語の名前などはエスケープせずに書く（CheckpointStore と同じ）。
    private static readonly JsonSerializerOptions Options = new()
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Converters = { new JsonStringEnumConverter() },
    };

    public string Path { get; } = path;

    public string? LastError { get; private set; }

    /// <summary>まだない・読めない・壊れているときは null（理由は <see cref="LastError"/>。まだないだけなら null のまま）。</summary>
    public SavedHistory? Load()
    {
        LastError = null;

        try
        {
            if (!File.Exists(Path))
                return null;

            using var stream = File.OpenRead(Path);
            var dto = JsonSerializer.Deserialize<HistoryDto>(stream, Options);

            if (dto is null)
            {
                LastError = "history.json is empty";
                return null;
            }

            if (dto.SchemaVersion != CurrentSchemaVersion)
            {
                LastError = $"unsupported schemaVersion {dto.SchemaVersion}";
                return null;
            }

            // 欠けた行は落とす（手で書き換えられたときなど）。
            var records = (dto.Visits ?? [])
                .Where(v => v is not null)
                .Select(v => v!.ToRecord())
                .OfType<VisitRecord>()
                .ToList();

            return new SavedHistory(Utc(dto.SavedAtUtc), records);
        }
        catch (Exception ex)
        {
            LastError = ex.Message;
            return null;
        }
    }

    public bool Save(IEnumerable<VisitRecord> records, DateTime savedAtUtc)
    {
        LastError = null;

        try
        {
            var dto = new HistoryDto
            {
                SavedAtUtc = savedAtUtc,
                Visits = records.Select(r => (VisitDto?)VisitDto.From(r)).ToList(),
            };

            Infrastructure.AtomicFile.Write(Path, stream => JsonSerializer.Serialize(stream, dto, Options));
            return true;
        }
        catch (Exception ex)
        {
            LastError = ex.Message;
            return false;
        }
    }

    private static DateTime Utc(DateTime time)
        => time.Kind == DateTimeKind.Local ? time.ToUniversalTime() : DateTime.SpecifyKind(time, DateTimeKind.Utc);

    private static DateTime? Utc(DateTime? time) => time is { } t ? Utc(t) : null;

    private sealed class HistoryDto
    {
        public int SchemaVersion { get; set; } = CurrentSchemaVersion;

        /// <summary>保存した時刻。戻した行の退出がログから分からないときの、終わりの見積もりに使う。</summary>
        public DateTime SavedAtUtc { get; set; }

        public List<VisitDto?>? Visits { get; set; }
    }

    private sealed class VisitDto
    {
        public string? EventId { get; set; }
        public string? SourceSessionId { get; set; }
        public long SuccessByteOffset { get; set; }
        public int SessionOrder { get; set; }
        public string? LocationKey { get; set; }
        public string? WorldId { get; set; }
        public string? InstanceId { get; set; }
        public AccessType AccessType { get; set; }
        public string? WorldName { get; set; }
        public string? Region { get; set; }
        public string? GroupId { get; set; }
        public string? Location { get; set; }
        public DateTime VisitedAtUtc { get; set; }
        public DateTime? LeftAtUtc { get; set; }
        public int? PeopleCount { get; set; }
        public bool EndedByCrash { get; set; }
        public bool ExcludedBefore { get; set; }
        public bool ExcludedAfter { get; set; }
        public int? VisitOrdinal { get; set; }
        public string? CounterEpochId { get; set; }
        public List<CompanionDto?>? Companions { get; set; }
        public List<PhotoDto?>? Photos { get; set; }

        public static VisitDto From(VisitRecord r) => new()
        {
            EventId = r.EventId,
            SourceSessionId = r.SourceSessionId,
            SuccessByteOffset = r.SuccessByteOffset,
            SessionOrder = r.SessionOrder,
            LocationKey = r.LocationKey,
            WorldId = r.WorldId,
            InstanceId = r.InstanceId,
            AccessType = r.AccessType,
            WorldName = r.WorldName,
            Region = r.Region,
            GroupId = r.GroupId,
            Location = r.Location,
            VisitedAtUtc = r.VisitedAtUtc,
            LeftAtUtc = r.LeftAtUtc,
            PeopleCount = r.PeopleCount,
            EndedByCrash = r.EndedByCrash,
            ExcludedBefore = r.ExcludedBefore,
            ExcludedAfter = r.ExcludedAfter,
            VisitOrdinal = r.VisitOrdinal,
            CounterEpochId = r.CounterEpochId,
            Companions = r.Companions.Count > 0 ? r.Companions.Select(c => (CompanionDto?)new CompanionDto { UserId = c.UserId, Name = c.Name, FirstSeenUtc = c.FirstSeenUtc, LeftAtUtc = c.LeftAtUtc }).ToList() : null,
            Photos = r.Photos.Count > 0 ? r.Photos.Select(p => (PhotoDto?)new PhotoDto { Path = p.Path, TakenAtUtc = p.TakenAtUtc }).ToList() : null,
        };

        public VisitRecord? ToRecord()
        {
            if (string.IsNullOrEmpty(EventId) || string.IsNullOrEmpty(SourceSessionId) || string.IsNullOrEmpty(LocationKey)
                || string.IsNullOrEmpty(WorldId) || string.IsNullOrEmpty(InstanceId) || !Enum.IsDefined(AccessType))
            {
                return null;
            }

            var record = new VisitRecord
            {
                EventId = EventId,
                SourceSessionId = SourceSessionId,
                SuccessByteOffset = SuccessByteOffset,
                SessionOrder = SessionOrder,
                LocationKey = LocationKey,
                WorldId = WorldId,
                InstanceId = InstanceId,
                AccessType = AccessType,
                WorldName = WorldName,
                Region = Region,
                GroupId = GroupId,
                Location = Location,
                VisitedAtUtc = Utc(VisitedAtUtc),
                LeftAtUtc = Utc(LeftAtUtc),
                PeopleCount = PeopleCount,
                EndedByCrash = EndedByCrash,
                ExcludedBefore = ExcludedBefore,
                ExcludedAfter = ExcludedAfter,
                VisitOrdinal = VisitOrdinal,
                CounterEpochId = CounterEpochId,
            };

            foreach (var c in Companions ?? [])
            {
                if (c is { UserId: { Length: > 0 } userId })
                    record.Companions.Add(new Companion(userId, c.Name ?? string.Empty, Utc(c.FirstSeenUtc), Utc(c.LeftAtUtc)));
            }

            foreach (var p in Photos ?? [])
            {
                if (p is { Path: { Length: > 0 } photoPath })
                    record.Photos.Add(new VisitPhoto(photoPath, Utc(p.TakenAtUtc)));
            }

            return record;
        }
    }

    private sealed class CompanionDto
    {
        public string? UserId { get; set; }
        public string? Name { get; set; }
        public DateTime FirstSeenUtc { get; set; }
        public DateTime? LeftAtUtc { get; set; }
    }

    private sealed class PhotoDto
    {
        public string? Path { get; set; }
        public DateTime TakenAtUtc { get; set; }
    }
}

/// <summary>保存してあった訪問履歴（→<see cref="HistoryFile"/>）。</summary>
/// <param name="SavedAtUtc">保存した時刻。</param>
/// <param name="Records">行（並びは問わない）。</param>
public sealed record SavedHistory(DateTime SavedAtUtc, IReadOnlyList<VisitRecord> Records);
