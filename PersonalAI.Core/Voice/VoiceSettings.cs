namespace PersonalAI.Core.Voice;

public sealed record VoiceSettings(
    bool VoiceInputEnabled,
    bool VoiceOutputEnabled,
    string? MicrophoneDeviceId,
    string? OutputDeviceId,
    int SampleRate,
    int ChannelCount,
    string? PushToTalkHotkey,
    string? SpeechToTextProviderId,
    string? TextToSpeechProviderId,
    string? SpeechToTextWorkerId,
    string? TextToSpeechWorkerId,
    string? LanguageHint,
    int MaxRecordingDurationSeconds,
    string? SelectedVoiceId,
    double SpeakingRate,
    bool DeleteTemporaryAudioByDefault,
    bool LocalOnly)
{
    public static readonly TimeSpan DefaultMaxRecordingDuration =
        TimeSpan.FromSeconds(60);

    public static VoiceSettings CreateDefault() =>
        new(
            VoiceInputEnabled: false,
            VoiceOutputEnabled: false,
            MicrophoneDeviceId: null,
            OutputDeviceId: null,
            SampleRate: 16000,
            ChannelCount: 1,
            PushToTalkHotkey: null,
            SpeechToTextProviderId: null,
            TextToSpeechProviderId: null,
            SpeechToTextWorkerId: null,
            TextToSpeechWorkerId: null,
            LanguageHint: null,
            MaxRecordingDurationSeconds: (int)DefaultMaxRecordingDuration.TotalSeconds,
            SelectedVoiceId: null,
            SpeakingRate: 1.0,
            DeleteTemporaryAudioByDefault: true,
            LocalOnly: true);
}
