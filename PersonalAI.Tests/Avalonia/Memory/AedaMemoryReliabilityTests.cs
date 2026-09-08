using System.Reflection;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Threading;
using PersonalAI.Core.Memory;
using PersonalAI.Core.Modules;
using PersonalAI.Desktop.Avalonia.Views.Memory;
using PersonalAI.Desktop.Presentation.ViewModels;

namespace PersonalAI.Tests.Avalonia.Memory;

public sealed class AedaMemoryReliabilityTests
{
    private const string StorageFailureMessage =
        "Memory records could not be loaded or saved.";

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ArchiveFailureIsContainedWithoutRefreshOrFalseSuccess(bool cancel)
    {
        var pending = PendingOperation();
        var service = new ServiceState
        {
            Archive = (_, _) => pending.Task
        };
        var viewModel = CreateViewModel(service);
        var dashboard = Dashboard();
        viewModel.Dashboard = dashboard;

        var archive = viewModel.ArchiveMemoryCommand.ExecuteAsync(MemoryRow);
        Assert.True(viewModel.ArchiveMemoryCommand.IsRunning);
        Assert.False(viewModel.ArchiveMemoryCommand.CanExecute(MemoryRow));
        if (cancel)
        {
            pending.SetCanceled();
        }
        else
        {
            pending.SetException(StorageFailure());
        }

        await archive.WaitAsync(TimeSpan.FromSeconds(1));

        Assert.Same(dashboard, viewModel.Dashboard);
        Assert.Equal(1, service.ArchiveCalls);
        Assert.Equal(0, service.DashboardCalls);
        Assert.Equal(
            cancel
                ? "Memory archive cancelled."
                : "Memory could not be archived. Try again.",
            viewModel.SafeStatusMessage);
        Assert.NotEqual("Memory archived.", viewModel.SafeStatusMessage);
        Assert.DoesNotContain(StorageFailureMessage, viewModel.SafeStatusMessage);
        Assert.False(viewModel.ArchiveMemoryCommand.IsRunning);
        Assert.True(viewModel.ArchiveMemoryCommand.CanExecute(MemoryRow));
    }

    [Fact]
    public async Task SuccessfulArchiveStillArchivesOnceAndRefreshesDashboard()
    {
        var service = new ServiceState();
        var viewModel = CreateViewModel(service);

        await viewModel.ArchiveMemoryAsync(MemoryRow);

        Assert.Equal(1, service.ArchiveCalls);
        Assert.Equal(1, service.DashboardCalls);
        Assert.Same(service.DashboardResult, viewModel.Dashboard);
        Assert.Equal(service.DashboardResult.SafeStatusMessage, viewModel.SafeStatusMessage);
    }

    [Theory]
    [InlineData("changed")]
    [InlineData("changed-back")]
    [InlineData("unchanged")]
    [InlineData("reason")]
    public async Task SuccessfulCreateClearsOnlyAnUneditedSubmittedDraft(string edit)
    {
        var pending = PendingOperation();
        var service = new ServiceState
        {
            Create = (_, _) => pending.Task
        };
        var viewModel = CreateViewModel(service);
        viewModel.NewMemoryText = "draft A";
        viewModel.NewMemorySourceReason = "reason A";

        var create = viewModel.CreateExplicitMemoryAsync();
        Assert.Equal("draft A", service.LastCreateRequest!.Text);
        Assert.Equal("reason A", service.LastCreateRequest.SourceReason);
        switch (edit)
        {
            case "changed":
                viewModel.NewMemoryText = "draft B";
                break;
            case "changed-back":
                viewModel.NewMemoryText = "draft B";
                viewModel.NewMemoryText = "draft A";
                break;
            case "reason":
                viewModel.NewMemorySourceReason = "reason B";
                break;
        }

        pending.SetResult(Success);
        await create.WaitAsync(TimeSpan.FromSeconds(1));

        Assert.Equal(
            edit switch
            {
                "changed" => "draft B",
                "unchanged" => string.Empty,
                _ => "draft A"
            },
            viewModel.NewMemoryText);
        Assert.Equal(
            edit == "reason" ? "reason B" : "reason A",
            viewModel.NewMemorySourceReason);
        Assert.Equal(1, service.CreateCalls);
        Assert.Equal(1, service.DashboardCalls);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task CreateFailurePreservesNewerDraftWithoutRefreshOrFalseSuccess(bool cancel)
    {
        var pending = PendingOperation();
        var service = new ServiceState
        {
            Create = (_, _) => pending.Task
        };
        var viewModel = CreateViewModel(service);
        viewModel.NewMemoryText = "draft A";

        var create = viewModel.CreateExplicitMemoryAsync();
        viewModel.NewMemoryText = "draft B";
        if (cancel)
        {
            pending.SetCanceled();
        }
        else
        {
            pending.SetException(StorageFailure());
        }

        await create.WaitAsync(TimeSpan.FromSeconds(1));

        Assert.Equal("draft A", service.LastCreateRequest!.Text);
        Assert.Equal("draft B", viewModel.NewMemoryText);
        Assert.Equal(0, service.DashboardCalls);
        Assert.Equal(
            cancel
                ? "Memory save cancelled."
                : "Memory could not be saved. Try again.",
            viewModel.SafeStatusMessage);
        Assert.NotEqual("Explicit memory saved.", viewModel.SafeStatusMessage);
        Assert.DoesNotContain(StorageFailureMessage, viewModel.SafeStatusMessage);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task SuccessfulMutationContainsDashboardStorageFailure(bool create)
    {
        var service = new ServiceState
        {
            Dashboard = _ => Task.FromException<AedaMemoryDashboardModel>(
                StorageFailure())
        };
        var viewModel = CreateViewModel(service);
        viewModel.NewMemoryText = "draft A";

        if (create)
        {
            await viewModel.CreateExplicitMemoryAsync();
        }
        else
        {
            await viewModel.ArchiveMemoryAsync(MemoryRow);
        }

        Assert.Equal(1, service.DashboardCalls);
        Assert.Equal(
            create
                ? "Memory was saved, but the dashboard could not be refreshed."
                : "Memory was archived, but the dashboard could not be refreshed.",
            viewModel.SafeStatusMessage);
        Assert.Equal(create ? 1 : 0, service.CreateCalls);
        Assert.Equal(create ? 0 : 1, service.ArchiveCalls);
        if (create)
        {
            Assert.Equal(string.Empty, viewModel.NewMemoryText);
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task UnexpectedInvalidOperationIsNotSwallowed(bool create)
    {
        var unexpected = new InvalidOperationException("programming error");
        var service = new ServiceState
        {
            Create = (_, _) => Task.FromException<AedaMemoryOperationResult>(unexpected),
            Archive = (_, _) => Task.FromException<AedaMemoryOperationResult>(unexpected)
        };
        var viewModel = CreateViewModel(service);
        viewModel.NewMemoryText = "draft A";

        var thrown = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            create
                ? viewModel.CreateExplicitMemoryAsync()
                : viewModel.ArchiveMemoryAsync(MemoryRow));

        Assert.Same(unexpected, thrown);
        Assert.Equal(0, service.DashboardCalls);
    }

    [Fact]
    public async Task ActualArchiveEventContainsStorageFailureInAnIsolatedProcess()
    {
        const string probeVariable = "AEDA_MEMORY_ARCHIVE_FAILURE_PROBE";
        if (Environment.GetEnvironmentVariable(probeVariable) == "1")
        {
            RunArchiveEventProbe();
            return;
        }

        var startInfo = new System.Diagnostics.ProcessStartInfo
        {
            FileName = Environment.GetEnvironmentVariable("DOTNET_HOST_PATH") ?? "dotnet",
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };
        startInfo.ArgumentList.Add("vstest");
        startInfo.ArgumentList.Add(typeof(AedaMemoryReliabilityTests).Assembly.Location);
        startInfo.ArgumentList.Add(
            "--TestCaseFilter:FullyQualifiedName=" +
            typeof(AedaMemoryReliabilityTests).FullName +
            "." + nameof(ActualArchiveEventContainsStorageFailureInAnIsolatedProcess));
        startInfo.Environment[probeVariable] = "1";

        using var process = System.Diagnostics.Process.Start(startInfo)!;
        var standardOutput = process.StandardOutput.ReadToEndAsync();
        var standardError = process.StandardError.ReadToEndAsync();
        await process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(15));
        var output = await standardOutput + await standardError;

        Assert.True(process.ExitCode == 0, output);
    }

    private static void RunArchiveEventProbe()
    {
        Exception? failure = null;
        using var finished = new ManualResetEventSlim();
        var thread = new Thread(() =>
        {
            try
            {
                AppBuilder.Configure<Application>().UsePlatformDetect().SetupWithoutStarting();
                SynchronizationContext.SetSynchronizationContext(
                    new AvaloniaSynchronizationContext());
                var pending = PendingOperation();
                var service = new ServiceState
                {
                    Archive = (_, _) => pending.Task
                };
                var viewModel = CreateViewModel(service);
                var dashboard = Dashboard();
                viewModel.Dashboard = dashboard;
                using var statusChanged = new ManualResetEventSlim();
                viewModel.PropertyChanged += (_, e) =>
                {
                    if (e.PropertyName == nameof(AedaMemoryModuleViewModel.SafeStatusMessage) &&
                        viewModel.SafeStatusMessage.Contains(
                            "could not be archived",
                            StringComparison.Ordinal))
                    {
                        statusChanged.Set();
                    }
                };
                var view = new MemoryView { DataContext = viewModel };
                var button = new Button { DataContext = MemoryRow };
                var handler = typeof(MemoryView).GetMethod(
                    "OnArchiveMemoryClick",
                    BindingFlags.Instance | BindingFlags.NonPublic)!;

                Dispatcher.UIThread.Post(() => handler.Invoke(view, [button, null]));
                Dispatcher.UIThread.RunJobs();
                Assert.Equal(1, service.ArchiveCalls);
                pending.SetException(StorageFailure());
                var timeout = DateTime.UtcNow + TimeSpan.FromSeconds(2);
                while (!statusChanged.IsSet && DateTime.UtcNow < timeout)
                {
                    Dispatcher.UIThread.RunJobs();
                    Thread.Sleep(1);
                }

                Assert.True(statusChanged.IsSet);
                Assert.Same(dashboard, viewModel.Dashboard);
                Assert.Equal(0, service.DashboardCalls);
                Assert.Equal(
                    "Memory could not be archived. Try again.",
                    viewModel.SafeStatusMessage);
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

    private static AedaMemoryModuleViewModel CreateViewModel(ServiceState service) =>
        new(service.CreateProxy(), new UnavailableModuleRegistry());

    private static TaskCompletionSource<AedaMemoryOperationResult> PendingOperation() =>
        new(TaskCreationOptions.RunContinuationsAsynchronously);

    private static InvalidOperationException StorageFailure() =>
        new(StorageFailureMessage);

    private static readonly AedaMemoryOperationResult Success = new(true);

    private static readonly AedaMemoryRecordSummary MemoryRow = new(
        "memory-1",
        new AedaMemoryKindBadge("explicit", "Explicit"),
        new AedaMemoryScopeBadge("global", "Global"),
        "Remember this.",
        "Active",
        "Normal",
        "Explicit user save",
        DateTimeOffset.UtcNow);

    private static AedaMemoryDashboardModel Dashboard() => new(
        1,
        new Dictionary<string, int>(),
        new Dictionary<string, int>(),
        [MemoryRow],
        [],
        [],
        0,
        0,
        new AedaMemoryPolicySummary(true, true, false, true, true, true, true, 365, 0),
        new AedaMemoryPrivacyStatus("Local", "Off", "Safe", "Safe", []),
        true,
        false,
        false,
        "Memory dashboard loaded.");

    private sealed class ServiceState
    {
        public Func<CancellationToken, Task<AedaMemoryDashboardModel>> Dashboard { get; init; } =
            _ => Task.FromResult(AedaMemoryReliabilityTests.Dashboard());
        public Func<AedaMemoryCreateRequest, CancellationToken, Task<AedaMemoryOperationResult>> Create { get; init; } =
            (_, _) => Task.FromResult(Success);
        public Func<MemoryId, CancellationToken, Task<AedaMemoryOperationResult>> Archive { get; init; } =
            (_, _) => Task.FromResult(Success);

        public AedaMemoryDashboardModel DashboardResult { get; private set; } = null!;
        public AedaMemoryCreateRequest? LastCreateRequest { get; private set; }
        public int DashboardCalls { get; private set; }
        public int CreateCalls { get; private set; }
        public int ArchiveCalls { get; private set; }

        public IAedaMemoryModuleService CreateProxy() =>
            Proxy<IAedaMemoryModuleService>((method, arguments) => method.Name switch
            {
                nameof(IAedaMemoryModuleService.GetDashboardAsync) => GetDashboard(
                    (CancellationToken)arguments![0]!),
                nameof(IAedaMemoryModuleService.CreateExplicitMemoryAsync) => CreateMemory(
                    (AedaMemoryCreateRequest)arguments![0]!,
                    (CancellationToken)arguments[1]!),
                nameof(IAedaMemoryModuleService.ArchiveMemoryAsync) => ArchiveMemory(
                    (MemoryId)arguments![0]!,
                    (CancellationToken)arguments[1]!),
                _ => throw new NotSupportedException(method.Name)
            });

        private async Task<AedaMemoryDashboardModel> GetDashboard(CancellationToken token)
        {
            DashboardCalls++;
            DashboardResult = await Dashboard(token);
            return DashboardResult;
        }

        private Task<AedaMemoryOperationResult> CreateMemory(
            AedaMemoryCreateRequest request,
            CancellationToken token)
        {
            CreateCalls++;
            LastCreateRequest = request;
            return Create(request, token);
        }

        private Task<AedaMemoryOperationResult> ArchiveMemory(
            MemoryId memoryId,
            CancellationToken token)
        {
            ArchiveCalls++;
            return Archive(memoryId, token);
        }
    }

    public class TestProxy : DispatchProxy
    {
        public Func<MethodInfo, object?[]?, object?> Handler { get; set; } = null!;

        protected override object? Invoke(MethodInfo? targetMethod, object?[]? args) =>
            Handler(targetMethod!, args);
    }

    private static T Proxy<T>(Func<MethodInfo, object?[]?, object?> handler)
        where T : class
    {
        var proxy = DispatchProxy.Create<T, TestProxy>();
        ((TestProxy)(object)proxy).Handler = handler;
        return proxy;
    }

    private sealed class UnavailableModuleRegistry : IAedaModuleRegistry
    {
        public IReadOnlyList<AedaModuleDescriptor> ListModules() => [];
        public IReadOnlyList<AedaModuleDescriptor> ListEnabledModules() => [];
        public bool TryGetModule(AedaModuleId moduleId, out AedaModuleDescriptor module)
        {
            module = null!;
            return false;
        }

        public IReadOnlyList<AedaModuleDescriptor> GetModulesByCapability(string capabilityId) => [];
        public AedaModuleStatus GetAvailability(AedaModuleId moduleId) =>
            AedaModuleStatus.Unavailable;
    }
}
