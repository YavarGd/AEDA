namespace PersonalAI.Desktop.Presentation.Services;

public interface IFolderPickerService
{
    Task<string?> PickSingleFolderAsync(CancellationToken cancellationToken = default);
}
