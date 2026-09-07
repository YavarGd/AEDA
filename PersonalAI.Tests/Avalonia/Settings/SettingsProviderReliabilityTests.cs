using System.ComponentModel;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Threading;
using PersonalAI.Core.Providers;
using PersonalAI.Core.Settings;
using PersonalAI.Core.Workspaces;
using PersonalAI.Desktop.Avalonia.Views.Settings;
using PersonalAI.Desktop.Presentation.Services;
using PersonalAI.Desktop.Presentation.ViewModels;

namespace PersonalAI.Tests.Avalonia.Settings;

[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class SettingsProviderUiCollection
{
    public const string Name = "Settings provider UI";
}

[Collection(SettingsProviderUiCollection.Name)]
public sealed class SettingsProviderReliabilityTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ProviderSaveFailureIsContainedAndTheSelectionCanRecover(
        bool cancel)
    {
        var store = new ControlledSettingsService(Settings("alpha"));
        var save = store.PlanProviderSave();
        var modelCalls = 0;
        var viewModel = CreateViewModel(store, _ =>
        {
            modelCalls++;
            return Task.FromResult<IReadOnlyList<string>>(["gemma4"]);
        });

        using var cancellation = new CancellationTokenSource();
        var selecting = viewModel.SelectProviderAsync("beta", cancellation.Token);
        await save.Started.Task.WaitAsync(TimeSpan.FromSeconds(1));
        if (cancel)
        {
            cancellation.Cancel();
        }
        else
        {
            save.Fail(new IOException("settings path unavailable"));
        }

        Assert.False(await selecting.WaitAsync(TimeSpan.FromSeconds(1)));
        Assert.Equal("alpha", store.Current.ProviderRouting.SelectedChatProvider);
        Assert.Equal(
            cancel
                ? "Chat provider update cancelled."
                : "Chat provider could not be saved. Try again.",
            viewModel.StatusMessage);
        Assert.NotEqual("Chat provider updated.", viewModel.StatusMessage);
        Assert.Equal(0, modelCalls);

        var retrySave = store.PlanProviderSave();
        var retry = viewModel.SelectProviderAsync("beta");
        await retrySave.Started.Task.WaitAsync(TimeSpan.FromSeconds(1));
        retrySave.Succeed();

        Assert.True(await retry.WaitAsync(TimeSpan.FromSeconds(1)));
        Assert.Equal("beta", store.Current.ProviderRouting.SelectedChatProvider);
        Assert.Equal("Chat provider updated.", viewModel.StatusMessage);
        Assert.Equal(1, modelCalls);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ProviderSaveCompletionSignalsInEitherOrderEndAtLatestSelection(
        bool signalNewerFirst)
    {
        var store = new ControlledSettingsService(Settings("alpha"));
        var betaSave = store.PlanProviderSave();
        var gammaSave = store.PlanProviderSave();
        var modelCalls = 0;
        var viewModel = CreateViewModel(store, _ =>
        {
            modelCalls++;
            return Task.FromResult<IReadOnlyList<string>>(["gemma4", "gamma-model"]);
        });

        var beta = viewModel.SelectProviderAsync("beta");
        await betaSave.Started.Task.WaitAsync(TimeSpan.FromSeconds(1));
        var gamma = viewModel.SelectProviderAsync("gamma");

        if (signalNewerFirst)
        {
            gammaSave.Succeed();
            Assert.False(gammaSave.Started.Task.IsCompleted);
        }

        betaSave.Succeed();
        await gammaSave.Started.Task.WaitAsync(TimeSpan.FromSeconds(1));
        gammaSave.Succeed();
        await Task.WhenAll(beta, gamma).WaitAsync(TimeSpan.FromSeconds(1));

        Assert.Equal("gamma", store.Current.ProviderRouting.SelectedChatProvider);
        Assert.Equal(2, store.ProviderSaveCount);
        Assert.Equal(1, modelCalls);
        Assert.Equal("Chat provider updated.", viewModel.StatusMessage);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task StaleProviderCompletionCannotPublishStatusOrModels(bool olderFails)
    {
        var store = new ControlledSettingsService(Settings("alpha"));
        var betaSave = store.PlanProviderSave();
        var gammaSave = store.PlanProviderSave();
        var modelCalls = 0;
        var viewModel = CreateViewModel(store, _ =>
        {
            modelCalls++;
            return Task.FromResult<IReadOnlyList<string>>(["gemma4"]);
        });
        var initialStatus = viewModel.StatusMessage;

        var beta = viewModel.SelectProviderAsync("beta");
        await betaSave.Started.Task.WaitAsync(TimeSpan.FromSeconds(1));
        var gamma = viewModel.SelectProviderAsync("gamma");
        if (olderFails)
        {
            betaSave.Fail(new IOException("obsolete failure"));
        }
        else
        {
            betaSave.Succeed();
        }

        await beta.WaitAsync(TimeSpan.FromSeconds(1));
        Assert.Equal(initialStatus, viewModel.StatusMessage);
        Assert.Equal(0, modelCalls);

        await gammaSave.Started.Task.WaitAsync(TimeSpan.FromSeconds(1));
        gammaSave.Succeed();
        await gamma.WaitAsync(TimeSpan.FromSeconds(1));

        Assert.Equal("gamma", store.Current.ProviderRouting.SelectedChatProvider);
        Assert.Equal("Chat provider updated.", viewModel.StatusMessage);
        Assert.Equal(1, modelCalls);
    }

    [Fact]
    public async Task SelectingAThenBThenAPersistsTheFinalA()
    {
        var store = new ControlledSettingsService(Settings("alpha"));
        var betaSave = store.PlanProviderSave();
        var alphaSave = store.PlanProviderSave();
        var modelCalls = 0;
        var viewModel = CreateViewModel(store, _ =>
        {
            modelCalls++;
            return Task.FromResult<IReadOnlyList<string>>(["gemma4"]);
        });

        var beta = viewModel.SelectProviderAsync("beta");
        await betaSave.Started.Task.WaitAsync(TimeSpan.FromSeconds(1));
        var alpha = viewModel.SelectProviderAsync("alpha");
        betaSave.Succeed();
        await alphaSave.Started.Task.WaitAsync(TimeSpan.FromSeconds(1));
        alphaSave.Succeed();
        await Task.WhenAll(beta, alpha).WaitAsync(TimeSpan.FromSeconds(1));

        Assert.Equal("alpha", store.Current.ProviderRouting.SelectedChatProvider);
        Assert.Equal(2, store.ProviderSaveCount);
        Assert.Equal(1, modelCalls);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task OverlappingModelRefreshesPublishOnlyTheNewestRequest(
        bool completeNewestFirst)
    {
        var store = new ControlledSettingsService(Settings("alpha"));
        var oldModels = new ModelPlan();
        var newModels = new ModelPlan();
        var plans = new Queue<ModelPlan>([oldModels, newModels]);
        var viewModel = CreateViewModel(store, _ => plans.Dequeue().RunAsync());

        var oldRefresh = viewModel.RefreshModelsAsync();
        var newRefresh = viewModel.RefreshModelsAsync();
        if (completeNewestFirst)
        {
            newModels.Succeed(["gemma4", "new-model"]);
            await newRefresh;
            oldModels.Succeed(["gemma4", "old-model"]);
        }
        else
        {
            oldModels.Succeed(["gemma4", "old-model"]);
            await oldRefresh;
            Assert.Empty(viewModel.InstalledModels);
            newModels.Succeed(["gemma4", "new-model"]);
        }

        await Task.WhenAll(oldRefresh, newRefresh);

        Assert.Equal(["gemma4", "new-model"], viewModel.InstalledModels);
        Assert.DoesNotContain("old-model", viewModel.InstalledModels);
    }

    [Fact]
    public async Task ProviderRefreshFromAnOldSelectionCannotOverwriteNewerModels()
    {
        var store = new ControlledSettingsService(Settings("alpha"));
        var betaSave = store.PlanProviderSave();
        var gammaSave = store.PlanProviderSave();
        var betaModels = new ModelPlan();
        var gammaModels = new ModelPlan();
        var models = new Queue<ModelPlan>([betaModels, gammaModels]);
        var viewModel = CreateViewModel(store, _ => models.Dequeue().RunAsync());

        var beta = viewModel.SelectProviderAsync("beta");
        await betaSave.Started.Task.WaitAsync(TimeSpan.FromSeconds(1));
        betaSave.Succeed();
        await betaModels.Started.Task.WaitAsync(TimeSpan.FromSeconds(1));

        var gamma = viewModel.SelectProviderAsync("gamma");
        await gammaSave.Started.Task.WaitAsync(TimeSpan.FromSeconds(1));
        gammaSave.Succeed();
        await gammaModels.Started.Task.WaitAsync(TimeSpan.FromSeconds(1));
        gammaModels.Succeed(["gemma4", "gamma-model"]);
        await gamma;
        betaModels.Succeed(["gemma4", "beta-model"]);
        await beta;

        Assert.Equal("gamma", store.Current.ProviderRouting.SelectedChatProvider);
        Assert.Equal(["gemma4", "gamma-model"], viewModel.InstalledModels);
    }

    [Fact]
    public async Task LatestProviderRefreshFailureIsContainedAndCurrent()
    {
        var store = new ControlledSettingsService(Settings("alpha"));
        var save = store.PlanProviderSave();
        var models = new ModelPlan();
        var viewModel = CreateViewModel(store, _ => models.RunAsync());

        var selecting = viewModel.SelectProviderAsync("beta");
        await save.Started.Task.WaitAsync(TimeSpan.FromSeconds(1));
        save.Succeed();
        await models.Started.Task.WaitAsync(TimeSpan.FromSeconds(1));
        models.Fail(new HttpRequestException("offline"));

        Assert.True(await selecting.WaitAsync(TimeSpan.FromSeconds(1)));
        Assert.Equal("beta", store.Current.ProviderRouting.SelectedChatProvider);
        Assert.Equal("Chat provider updated.", viewModel.StatusMessage);
        Assert.Equal("Ollama models unavailable: offline", viewModel.ModelRefreshStatus);
    }

    [Fact]
    public async Task ManualModelRefreshStillPublishesItsResult()
    {
        var viewModel = CreateViewModel(
            new ControlledSettingsService(Settings("alpha")),
            _ => Task.FromResult<IReadOnlyList<string>>(["gemma4", "manual-model"]));

        await viewModel.RefreshModelsAsync();

        Assert.Equal(["gemma4", "manual-model"], viewModel.InstalledModels);
        Assert.Equal("Loaded 2 installed model(s).", viewModel.ModelRefreshStatus);
    }

    [Fact]
    public async Task ActualProviderEventContainsSaveFailureAndRestoresThePicker()
    {
        const string probeVariable = "AEDA_SETTINGS_PROVIDER_FAILURE_PROBE";
        if (Environment.GetEnvironmentVariable(probeVariable) == "1")
        {
            RunProviderEventProbe();
            return;
        }

        var testAssembly = typeof(SettingsProviderReliabilityTests).Assembly.Location;
        var startInfo = new System.Diagnostics.ProcessStartInfo
        {
            FileName = Environment.GetEnvironmentVariable("DOTNET_HOST_PATH") ?? "dotnet",
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };
        startInfo.ArgumentList.Add("vstest");
        startInfo.ArgumentList.Add(testAssembly);
        startInfo.ArgumentList.Add(
            "--TestCaseFilter:FullyQualifiedName=" +
            typeof(SettingsProviderReliabilityTests).FullName +
            "." + nameof(ActualProviderEventContainsSaveFailureAndRestoresThePicker));
        startInfo.Environment[probeVariable] = "1";

        using var process = System.Diagnostics.Process.Start(startInfo)!;
        var standardOutput = process.StandardOutput.ReadToEndAsync();
        var standardError = process.StandardError.ReadToEndAsync();
        await process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(15));
        var output = await standardOutput + await standardError;

        Assert.True(process.ExitCode == 0, output);
    }

    private static void RunProviderEventProbe()
    {
        Exception? failure = null;
        var finished = new ManualResetEventSlim();
        var thread = new Thread(() =>
        {
            try
            {
                AppBuilder.Configure<Application>().UsePlatformDetect().SetupWithoutStarting();
                SynchronizationContext.SetSynchronizationContext(
                    new AvaloniaSynchronizationContext());
                var store = new ControlledSettingsService(Settings("alpha"));
                var save = store.PlanProviderSave();
                var modelCalls = 0;
                var viewModel = CreateViewModel(store, _ =>
                {
                    modelCalls++;
                    return Task.FromResult<IReadOnlyList<string>>(["gemma4"]);
                });
                using var statusChanged = new ManualResetEventSlim();
                viewModel.PropertyChanged += (_, e) =>
                {
                    if (e.PropertyName == nameof(SettingsViewModel.StatusMessage) &&
                        viewModel.StatusMessage.Contains("could not be saved", StringComparison.Ordinal))
                    {
                        statusChanged.Set();
                    }
                };
                var view = new SettingsView();
                typeof(SettingsView).GetField(
                        "_settingsService",
                        System.Reflection.BindingFlags.Instance |
                        System.Reflection.BindingFlags.NonPublic)!
                    .SetValue(view, store);
                view.DataContext = viewModel;
                var picker = view.FindControl<ComboBox>("ProviderPicker")!;
                Assert.Equal("Alpha", picker.SelectedItem);
                Assert.Equal(0, store.ProviderSaveCount);

                Dispatcher.UIThread.Post(() => picker.SelectedItem = "Beta");
                Dispatcher.UIThread.RunJobs();
                Assert.True(save.Started.Task.IsCompleted);
                save.Fail(new IOException("settings path unavailable"));
                var timeout = DateTime.UtcNow + TimeSpan.FromSeconds(2);
                while (!statusChanged.IsSet && DateTime.UtcNow < timeout)
                {
                    Dispatcher.UIThread.RunJobs();
                    Thread.Sleep(1);
                }

                Assert.True(statusChanged.IsSet);
                Assert.Equal("Alpha", picker.SelectedItem);
                Assert.Equal("alpha", store.Current.ProviderRouting.SelectedChatProvider);
                Assert.Equal(0, modelCalls);
                Assert.Equal("Chat provider could not be saved. Try again.", viewModel.StatusMessage);
            }
            catch (Exception exception)
            {
                failure = exception;
            }
            finally
            {
                finished.Set();
            }
        });

        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        Assert.True(finished.Wait(TimeSpan.FromSeconds(5)));
        Assert.Null(failure);
    }

    private static SettingsViewModel CreateViewModel(
        IApplicationSettingsService settings,
        Func<CancellationToken, Task<IReadOnlyList<string>>> models) =>
        new(
            settings,
            new StartupRegistrationService(),
            _ => Task.FromResult(new SettingsApplyResult(true, "ok")),
            _ => { },
            () => { },
            models,
            new WorkspaceManagementViewModel(
                new WorkspaceRegistrationService(),
                new FolderPickerService()));

    private static ApplicationSettings Settings(string selectedProvider)
    {
        ProviderProfileSetting Provider(string id, string name) => new(
            id,
            ProviderKind.TestFake,
            name,
            $"http://{id}",
            IsEnabled: true,
            ChatModel: "gemma4",
            EmbeddingModel: null,
            SecretReference: null);

        var settings = ApplicationSettings.CreateDefault();
        return settings with
        {
            ProviderRouting = settings.ProviderRouting with
            {
                ProviderProfiles =
                [Provider("alpha", "Alpha"), Provider("beta", "Beta"), Provider("gamma", "Gamma")],
                SelectedChatProvider = selectedProvider,
                SelectedEmbeddingProvider = "alpha",
                DefaultLocalProvider = "alpha"
            }
        };
    }

    private sealed class ControlledSettingsService(ApplicationSettings settings)
        : IApplicationSettingsService
    {
        private readonly Queue<SavePlan> _providerSaves = [];

        public ApplicationSettings Current { get; private set; } = settings;
        public string SettingsPath => "settings.json";
        public int ProviderSaveCount { get; private set; }

        public SavePlan PlanProviderSave()
        {
            var plan = new SavePlan();
            _providerSaves.Enqueue(plan);
            return plan;
        }

        public Task InitializeAsync(CancellationToken cancellationToken = default) =>
            Task.CompletedTask;

        public async Task SaveAsync(
            ApplicationSettings updated,
            CancellationToken cancellationToken = default)
        {
            if (updated.ProviderRouting.SelectedChatProvider ==
                Current.ProviderRouting.SelectedChatProvider)
            {
                Current = updated;
                return;
            }

            ProviderSaveCount++;
            var plan = _providerSaves.Dequeue();
            plan.Started.TrySetResult(updated);
            await plan.Completion.Task.WaitAsync(cancellationToken);
            Current = updated;
        }

        public Task ResetAsync(CancellationToken cancellationToken = default) =>
            Task.CompletedTask;
    }

    private sealed class SavePlan
    {
        public TaskCompletionSource<ApplicationSettings> Started { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Completion { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public void Succeed() => Completion.TrySetResult();
        public void Fail(Exception exception) => Completion.TrySetException(exception);
    }

    private sealed class ModelPlan
    {
        public TaskCompletionSource Started { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        private TaskCompletionSource<IReadOnlyList<string>> Completion { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public Task<IReadOnlyList<string>> RunAsync()
        {
            Started.TrySetResult();
            return Completion.Task;
        }

        public void Succeed(IReadOnlyList<string> models) =>
            Completion.TrySetResult(models);

        public void Fail(Exception exception) =>
            Completion.TrySetException(exception);
    }

    private sealed class StartupRegistrationService : IStartupRegistrationService
    {
        public bool IsSupported => false;
        public bool IsEnabled() => false;
        public StartupRegistrationResult SetEnabled(bool enabled) =>
            new(false, "Unavailable");
    }

    private sealed class FolderPickerService : IFolderPickerService
    {
        public Task<string?> PickSingleFolderAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult<string?>(null);
    }

    private sealed class WorkspaceRegistrationService : IWorkspaceRegistrationService
    {
        public Task InitializeAsync(CancellationToken cancellationToken = default) =>
            Task.CompletedTask;
        public Task<IReadOnlyList<PersistedWorkspace>> ListAsync(
            CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<PersistedWorkspace>>([]);
        public Task<PersistedWorkspace> RegisterAsync(
            string rootPath,
            string displayName,
            string source,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
        public Task RemoveAsync(
            WorkspaceId workspaceId,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
        public Task<PersistedWorkspace?> UpdateDisplayNameAsync(
            WorkspaceId workspaceId,
            string displayName,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
        public Task<PersistedWorkspace?> RevalidateAsync(
            WorkspaceId workspaceId,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
        public Task RevalidateAllAsync(CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
    }
}
