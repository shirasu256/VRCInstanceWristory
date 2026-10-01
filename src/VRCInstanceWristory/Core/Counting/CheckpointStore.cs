using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace VRCInstanceWristory.Core.Counting;

/// <summary>
/// チェックポイントの読み書き。保存は一時ファイルからの置換で行い、
/// 途中で落ちても古い状態を新しい状態として扱わないようにする。
/// </summary>
public sealed class CheckpointStore(string path)
{
    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    public string Path { get; } = path;

    public string? LastError { get; private set; }

    /// <summary>読めない・壊れている場合は null を返す（呼び出し側がログから再構築する）。</summary>
    public CheckpointDto? Load()
    {
        LastError = null;

        try
        {
            if (!File.Exists(Path))
                return null;

            using var stream = File.OpenRead(Path);
            var dto = JsonSerializer.Deserialize<CheckpointDto>(stream, Options);

            if (dto is null)
            {
                LastError = "checkpoint is empty";
                return null;
            }

            if (dto.SchemaVersion != CheckpointDto.CurrentSchemaVersion)
            {
                LastError = $"unsupported schemaVersion {dto.SchemaVersion}";
                return null;
            }

            return dto;
        }
        catch (Exception ex)
        {
            LastError = ex.Message;
            return null;
        }
    }

    public bool Save(CheckpointDto dto)
    {
        LastError = null;

        try
        {
            Infrastructure.AtomicFile.Write(Path, stream => JsonSerializer.Serialize(stream, dto, Options));
            return true;
        }
        catch (Exception ex)
        {
            LastError = ex.Message;
            return false;
        }
    }
}
