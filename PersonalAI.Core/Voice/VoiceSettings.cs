namespace PersonalAI.Core.Voice;

public sealed record VoiceSettings(
    string? MicrophoneDeviceId,
    string? OutputDeviceId,
    int SampleRate,
    int ChannelCount,
    string? PushToTalkHotkey,
    string? SpeechToTextProviderId,
    string? TextToSpeechProviderId,
    bool LocalOnly)
{
    public static VoiceSettings CreateDefault() =>
        new(
            MicrophoneDeviceId: null,
            OutputDeviceId: null,
            SampleRate: 16000,
            ChannelCount: 1,
            PushToTalkHotkey: null,
            SpeechToTextProviderId: null,
            TextToSpeechProviderId: null,
            LocalOnly: true);
}
