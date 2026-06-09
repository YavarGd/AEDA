namespace PersonalAI.Desktop.WinUI.Services;

public sealed record GenerationStopConfirmationRequest(
    string PrimaryButtonText,
    string CloseButtonText);
