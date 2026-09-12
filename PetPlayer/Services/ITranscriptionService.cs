using PetPlayer.Models;

namespace PetPlayer.Services;

/// <summary>
/// Service boundary for a future automatic speech-to-text pipeline
/// (video -> extract audio -> STT engine -> timestamped transcript).
///
/// No implementation ships in v1; this interface exists purely so a local
/// engine (Whisper, whisper.cpp, faster-whisper via an external helper
/// process, etc.) can be plugged in later without touching MediaPlayerService,
/// TranscriptService or any view model.
/// </summary>
public interface ITranscriptionService
{
    Task<IReadOnlyList<TranscriptEntry>> TranscribeAsync(string mediaFilePath, CancellationToken cancellationToken);
}
