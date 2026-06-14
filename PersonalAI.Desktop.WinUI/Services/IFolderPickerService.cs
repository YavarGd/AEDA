namespace PersonalAI.Desktop.WinUI.Services;

public interface IFolderPickerService
{
    Task<string?> PickSingleFolderAsync(CancellationToken cancellationToken = default);
}
