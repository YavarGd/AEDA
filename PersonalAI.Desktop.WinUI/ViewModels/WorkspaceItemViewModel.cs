using CommunityToolkit.Mvvm.ComponentModel;
using PersonalAI.Core.Workspaces;

namespace PersonalAI.Desktop.WinUI.ViewModels;

public sealed partial class WorkspaceItemViewModel(PersistedWorkspace workspace)
    : ObservableObject
{
    public WorkspaceId Id => Workspace.Id;

    public PersistedWorkspace Workspace { get; private set; } = workspace;

    public string WorkspaceId => Workspace.Id.ToString();

    public string DisplayName => Workspace.DisplayName;

    public string RootPath => Workspace.CanonicalRootPath;

    public string ShortRootPath => ShortenPath(Workspace.CanonicalRootPath);

    public string Source => Workspace.Source;

    public string AddedAt => Workspace.AddedAtUtc.ToLocalTime().ToString("g");

    public string LastValidated =>
        Workspace.LastValidatedAtUtc?.ToLocalTime().ToString("g") ?? "Not validated";

    public WorkspaceStatusPresentation Presentation =>
        WorkspaceStatusPresentationMapper.Map(Workspace);

    public string Status => Presentation.Label;

    public string StatusDetail => Presentation.Message;

    public string StatusSymbol => Presentation.Symbol;

    public string ReadOnlyBadge => Workspace.IsReadOnly ? "Read-only" : "Not read-only";

    public bool CanUseWorkspace =>
        Workspace.Status == WorkspaceRegistrationStatus.Available &&
        Workspace.IsActive;

    [ObservableProperty]
    private string _editableDisplayName = workspace.DisplayName;

    public void Update(PersistedWorkspace workspace)
    {
        Workspace = workspace;
        EditableDisplayName = workspace.DisplayName;
        OnPropertyChanged(nameof(Id));
        OnPropertyChanged(nameof(WorkspaceId));
        OnPropertyChanged(nameof(DisplayName));
        OnPropertyChanged(nameof(RootPath));
        OnPropertyChanged(nameof(ShortRootPath));
        OnPropertyChanged(nameof(Source));
        OnPropertyChanged(nameof(AddedAt));
        OnPropertyChanged(nameof(LastValidated));
        OnPropertyChanged(nameof(Presentation));
        OnPropertyChanged(nameof(Status));
        OnPropertyChanged(nameof(StatusDetail));
        OnPropertyChanged(nameof(StatusSymbol));
        OnPropertyChanged(nameof(ReadOnlyBadge));
        OnPropertyChanged(nameof(CanUseWorkspace));
    }

    private static string ShortenPath(string path)
    {
        const int maxLength = 64;

        if (string.IsNullOrWhiteSpace(path) || path.Length <= maxLength)
        {
            return path;
        }

        var root = Path.GetPathRoot(path) ?? string.Empty;
        var fileName = Path.GetFileName(path.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));

        if (!string.IsNullOrWhiteSpace(fileName) &&
            root.Length + fileName.Length + 5 < maxLength)
        {
            return $"{root}...\\{fileName}";
        }

        return $"...{path[^Math.Min(path.Length, maxLength - 3)..]}";
    }
}
