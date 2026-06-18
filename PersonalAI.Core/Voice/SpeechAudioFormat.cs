namespace PersonalAI.Core.Voice;

public sealed record SpeechAudioFormat(
    string Encoding,
    int SampleRate,
    int ChannelCount);
