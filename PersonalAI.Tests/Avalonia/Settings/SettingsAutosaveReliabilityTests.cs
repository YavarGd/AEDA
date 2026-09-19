using PersonalAI.Core.Chat;
using PersonalAI.Core.Settings;
using PersonalAI.Desktop.Presentation.Services;
using PersonalAI.Desktop.Presentation.ViewModels;

namespace PersonalAI.Tests.Avalonia.Settings;

public sealed class SettingsAutosaveReliabilityTests
{
    [Fact]
    public async Task AutosaveFailureIsObservedAndALaterEditCanSave()
    {
        var store = new ControlledSettingsService(Settings());
        var failed = store.PlanSave();
        var recovered = store.PlanSave();
        var applied = new List<ApplicationSettings>();
        var viewModel = CreateViewModel(store, applied.Add);

        viewModel.ShowMessageMetadata = true;
        await failed.Started.Task.WaitAsync(TimeSpan.FromSeconds(1));
        failed.Fail(new IOException("settings path unavailable"));
        await viewModel.DrainAutosavesAsync().WaitAsync(TimeSpan.FromSeconds(1));

        Assert.Equal("Settings could not be saved. Try again.", viewModel.StatusMessage);
        Assert.False(store.Current.Appearance.ShowMessageMetadata);
        Assert.Empty(applied);

        viewModel.CompactSidebar = true;
        await recovered.Started.Task.WaitAsync(TimeSpan.FromSeconds(1));
        recovered.Succeed();
        await viewModel.DrainAutosavesAsync().WaitAsync(TimeSpan.FromSeconds(1));

        Assert.True(store.Current.Appearance.ShowMessageMetadata);
        Assert.True(store.Current.Appearance.CompactSidebar);
        Assert.Equal("Settings saved.", viewModel.StatusMessage);
        Assert.Single(applied);
    }

    [Fact]
    public async Task OverlappingAutosavesCannotPersistAnOlderSnapshotLast()
    {
        var store = new ControlledSettingsService(Settings());
        var first = store.PlanSave();
        var second = store.PlanSave();
        var viewModel = CreateViewModel(store);

        viewModel.ShowMessageMetadata = true;
        await first.Started.Task.WaitAsync(TimeSpan.FromSeconds(1));
        viewModel.CompactSidebar = true;

        second.Succeed();
        Assert.False(second.Started.Task.IsCompleted);
        first.Succeed();
        await viewModel.DrainAutosavesAsync().WaitAsync(TimeSpan.FromSeconds(1));

        Assert.True(store.Current.Appearance.ShowMessageMetadata);
        Assert.True(store.Current.Appearance.CompactSidebar);
        Assert.Equal(2, store.SaveCount);
    }

    [Fact]
    public async Task RapidEditsCoalesceToTheLatestSnapshotAndRuntimeApply()
    {
        var store = new ControlledSettingsService(Settings());
        var first = store.PlanSave();
        var latest = store.PlanSave();
        var applied = new List<ApplicationSettings>();
        var viewModel = CreateViewModel(store, applied.Add);

        viewModel.ShowMessageMetadata = true;
        await first.Started.Task.WaitAsync(TimeSpan.FromSeconds(1));
        viewModel.CompactSidebar = true;
        viewModel.Theme = ThemePreference.Graphite;

        latest.Succeed();
        first.Succeed();
        await viewModel.DrainAutosavesAsync().WaitAsync(TimeSpan.FromSeconds(1));

        Assert.Equal(2, store.SaveCount);
        Assert.True(store.Current.Appearance.ShowMessageMetadata);
        Assert.True(store.Current.Appearance.CompactSidebar);
        Assert.Equal(ThemePreference.Graphite, store.Current.Appearance.Theme);
        var runtime = Assert.Single(applied);
        Assert.Equal(store.Current, runtime);
    }

    [Fact]
    public async Task StaleFailureCannotOverwriteNewerAutosaveFeedback()
    {
        var store = new ControlledSettingsService(Settings());
        var stale = store.PlanSave();
        var latest = store.PlanSave();
        var viewModel = CreateViewModel(store);

        viewModel.ShowMessageMetadata = true;
        await stale.Started.Task.WaitAsync(TimeSpan.FromSeconds(1));
        viewModel.CompactSidebar = true;
        stale.Fail(new UnauthorizedAccessException("obsolete failure"));
        await latest.Started.Task.WaitAsync(TimeSpan.FromSeconds(1));

        Assert.Equal("Settings ready.", viewModel.StatusMessage);

        latest.Succeed();
        await viewModel.DrainAutosavesAsync().WaitAsync(TimeSpan.FromSeconds(1));

        Assert.Equal("Settings saved.", viewModel.StatusMessage);
        Assert.True(store.Current.Appearance.CompactSidebar);
    }

    [Fact]
    public async Task ModelAutosaveRejectsInvalidVisionAndSavesTheNextValidAssignment()
    {
        var store = new ControlledSettingsService(Settings());
        var saved = store.PlanSave();
        var viewModel = CreateViewModel(store);
        viewModel.InstalledModels.Add("notgemma4text");
        viewModel.InstalledModels.Add("gemma4:latest");

        viewModel.VisionModel = "notgemma4text";

        Assert.Equal(0, store.SaveCount);
        Assert.Contains("is not configured as vision-capable", viewModel.StatusMessage);

        viewModel.VisionModel = "gemma4:latest";
        await saved.Started.Task.WaitAsync(TimeSpan.FromSeconds(1));
        saved.Succeed();
        await viewModel.DrainAutosavesAsync().WaitAsync(TimeSpan.FromSeconds(1));

        Assert.Equal("gemma4:latest", Assignment(
            store.Current.Models,
            ModelRoutingCategory.Vision));
        Assert.Equal("Settings saved.", viewModel.StatusMessage);
    }

    [Fact]
    public async Task StartupCommandStillAwaitsItsExplicitSave()
    {
        var store = new ControlledSettingsService(Settings());
        var save = store.PlanSave();
        var viewModel = CreateViewModel(
            store,
            startup: new StartupRegistrationService(true, "Startup updated."));
        viewModel.LaunchAtSignIn = true;

        var toggling = viewModel.ToggleStartupAsync();
        await save.Started.Task.WaitAsync(TimeSpan.FromSeconds(1));

        Assert.False(toggling.IsCompleted);

        save.Succeed();
        await toggling.WaitAsync(TimeSpan.FromSeconds(1));

        Assert.True(store.Current.Window.LaunchAtSignIn);
        Assert.Equal("Startup updated.", viewModel.StatusMessage);
    }

    [Fact]
    public async Task DrainWaitsForTheLatestIssuedAutosave()
    {
        var store = new ControlledSettingsService(Settings());
        var save = store.PlanSave();
        var viewModel = CreateViewModel(store);
        viewModel.ShowMessageMetadata = true;
        await save.Started.Task.WaitAsync(TimeSpan.FromSeconds(1));

        var draining = viewModel.DrainAutosavesAsync();

        Assert.False(draining.IsCompleted);

        save.Succeed();
        await draining.WaitAsync(TimeSpan.FromSeconds(1));
        Assert.True(store.Current.Appearance.ShowMessageMetadata);
    }

    private static SettingsViewModel CreateViewModel(
        IApplicationSettingsService settings,
        Action<ApplicationSettings>? applyRuntimeSettings = null,
        IStartupRegistrationService? startup = null) =>
        new(
            settings,
            startup ?? new StartupRegistrationService(),
            _ => Task.FromResult(new SettingsApplyResult(true, "ok")),
            applyRuntimeSettings ?? (_ => { }),
            () => { },
            _ => Task.FromResult<IReadOnlyList<string>>([]),
            null!);

    private static ApplicationSettings Settings()
    {
        var settings = ApplicationSettings.CreateDefault();
        return settings with
        {
            Appearance = settings.Appearance with
            {
                CompactSidebar = false,
                ShowMessageMetadata = false
            }
        };
    }

    private static string Assignment(ModelSettings settings, ModelRoutingCategory category) =>
        settings.Assignments.Single(item => item.Category == category).Model;

    private sealed class ControlledSettingsService(ApplicationSettings settings)
        : IApplicationSettingsService
    {
        private readonly Queue<SavePlan> _plans = new();

        public ApplicationSettings Current { get; private set; } = settings;
        public string SettingsPath => "settings.json";
        public int SaveCount { get; private set; }

        public SavePlan PlanSave()
        {
            var plan = new SavePlan();
            _plans.Enqueue(plan);
            return plan;
        }

        public Task InitializeAsync(CancellationToken cancellationToken = default) =>
            Task.CompletedTask;

        public async Task SaveAsync(
            ApplicationSettings updated,
            CancellationToken cancellationToken = default)
        {
            var plan = _plans.Dequeue();
            SaveCount++;
            plan.Settings = updated;
            plan.Started.SetResult();
            try
            {
                await plan.Release.Task.WaitAsync(cancellationToken);
                Current = updated;
            }
            finally
            {
                plan.Completed.SetResult();
            }
        }

        public Task ResetAsync(CancellationToken cancellationToken = default) =>
            Task.CompletedTask;
    }

    private sealed class SavePlan
    {
        public TaskCompletionSource Started { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Release { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Completed { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        public ApplicationSettings? Settings { get; set; }

        public void Succeed() => Release.SetResult();
        public void Fail(Exception exception) => Release.SetException(exception);
    }

    private sealed class StartupRegistrationService(
        bool succeeded = false,
        string message = "Unavailable") : IStartupRegistrationService
    {
        public bool IsSupported => true;
        public bool IsEnabled() => false;
        public StartupRegistrationResult SetEnabled(bool enabled) =>
            new(succeeded, message);
    }
}
