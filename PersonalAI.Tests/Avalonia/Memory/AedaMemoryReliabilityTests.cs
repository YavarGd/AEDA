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
    public async Task InitializeFailurePreservesDashboard(bool cancel)
    {
        var pending = Pending<AedaMemoryDashboardModel>();
        var service = new ServiceState { Dashboard = _ => pending.Task };
        var viewModel = CreateViewModel(service);
        var dashboard = Dashboard();
        viewModel.Dashboard = dashboard;

        var initialize = viewModel.InitializeAsync();
        if (cancel)
        {
            pending.SetCanceled();
        }
        else
        {
            pending.SetException(StorageFailure());
        }

        await initialize.WaitAsync(TimeSpan.FromSeconds(1));

        Assert.Same(dashboard, viewModel.Dashboard);
        Assert.Equal(1, service.DashboardCalls);
        Assert.Equal(
            cancel ? "Memory load cancelled." : "Memory could not be loaded. Try again.",
            viewModel.SafeStatusMessage);
        Assert.DoesNotContain(StorageFailureMessage, viewModel.SafeStatusMessage);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task SearchFailurePreservesResults(bool cancel)
    {
        var pending = Pending<IReadOnlyList<AedaMemoryRecordSummary>>();
        var service = new ServiceState();
        var viewModel = CreateViewModel(service);
        viewModel.SearchText = "saved search";
        await viewModel.SearchMemoriesAsync();
        var results = viewModel.SearchResults;
        service.Search = (_, _, _) => pending.Task;

        var search = viewModel.SearchMemoriesAsync();
        if (cancel)
        {
            pending.SetCanceled();
        }
        else
        {
            pending.SetException(StorageFailure());
        }

        await search.WaitAsync(TimeSpan.FromSeconds(1));

        Assert.Same(results, viewModel.SearchResults);
        Assert.Equal(2, service.SearchCalls);
        Assert.Equal(
            cancel
                ? "Memory search cancelled."
                : "Memory search is temporarily unavailable. Try again.",
            viewModel.SafeStatusMessage);
        Assert.NotEqual("No memories matched.", viewModel.SafeStatusMessage);
        Assert.NotEqual("Memory search complete.", viewModel.SafeStatusMessage);
        Assert.DoesNotContain(StorageFailureMessage, viewModel.SafeStatusMessage);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task DetailFailurePreservesSelection(bool cancel)
    {
        var pending = Pending<AedaMemoryRecordDetail?>();
        var service = new ServiceState();
        var viewModel = CreateViewModel(service);
        await viewModel.OpenMemoryDetailAsync(MemoryRow);
        var detail = viewModel.SelectedMemory;
        service.Detail = (_, _) => pending.Task;

        var load = viewModel.OpenMemoryDetailAsync(MemoryRow);
        if (cancel)
        {
            pending.SetCanceled();
        }
        else
        {
            pending.SetException(StorageFailure());
        }

        await load.WaitAsync(TimeSpan.FromSeconds(1));

        Assert.Same(detail, viewModel.SelectedMemory);
        Assert.Equal(2, service.DetailCalls);
        Assert.Equal(
            cancel
                ? "Memory detail load cancelled."
                : "Memory detail could not be loaded. Try again.",
            viewModel.SafeStatusMessage);
        Assert.NotEqual("Memory not found.", viewModel.SafeStatusMessage);
        Assert.NotEqual("Memory detail loaded.", viewModel.SafeStatusMessage);
        Assert.DoesNotContain(StorageFailureMessage, viewModel.SafeStatusMessage);
    }

    [Fact]
    public async Task SuccessfulMissingDetailStillReportsNotFound()
    {
        var service = new ServiceState
        {
            Detail = (_, _) => Task.FromResult<AedaMemoryRecordDetail?>(null)
        };
        var viewModel = CreateViewModel(service);
        viewModel.SelectedMemory = MemoryDetail();

        await viewModel.OpenMemoryDetailAsync(MemoryRow);

        Assert.Null(viewModel.SelectedMemory);
        Assert.Equal("Memory not found.", viewModel.SafeStatusMessage);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task DeleteFailureIsContainedWithoutRefreshOrFalseSuccess(bool cancel)
    {
        var pending = PendingOperation();
        var service = new ServiceState { Delete = (_, _) => pending.Task };
        var viewModel = CreateViewModel(service);
        var dashboard = Dashboard();
        viewModel.Dashboard = dashboard;

        var delete = viewModel.DeleteMemoryAsync(MemoryRow);
        if (cancel)
        {
            pending.SetCanceled();
        }
        else
        {
            pending.SetException(StorageFailure());
        }

        await delete.WaitAsync(TimeSpan.FromSeconds(1));

        Assert.Same(dashboard, viewModel.Dashboard);
        Assert.Equal(1, service.DeleteCalls);
        Assert.Equal(0, service.DashboardCalls);
        Assert.Equal(
            cancel
                ? "Memory delete cancelled."
                : "Memory could not be deleted. Try again.",
            viewModel.SafeStatusMessage);
        Assert.NotEqual("Memory deleted.", viewModel.SafeStatusMessage);
        Assert.DoesNotContain(StorageFailureMessage, viewModel.SafeStatusMessage);
    }

    [Fact]
    public async Task SuccessfulDeleteDeletesOnceAndRefreshesDashboardOnce()
    {
        var service = new ServiceState();
        var viewModel = CreateViewModel(service);

        await viewModel.DeleteMemoryAsync(MemoryRow);

        Assert.Equal(1, service.DeleteCalls);
        Assert.Equal(1, service.DashboardCalls);
        Assert.Same(service.DashboardResult, viewModel.Dashboard);
        Assert.Equal(service.DashboardResult.SafeStatusMessage, viewModel.SafeStatusMessage);
    }

    [Fact]
    public async Task SuccessfulDeletePreservesSuccessWhenDashboardRefreshFails()
    {
        var service = new ServiceState
        {
            Dashboard = _ => Task.FromException<AedaMemoryDashboardModel>(StorageFailure())
        };
        var viewModel = CreateViewModel(service);
        var dashboard = Dashboard();
        viewModel.Dashboard = dashboard;

        await viewModel.DeleteMemoryAsync(MemoryRow);

        Assert.Equal(1, service.DeleteCalls);
        Assert.Equal(1, service.DashboardCalls);
        Assert.Same(dashboard, viewModel.Dashboard);
        Assert.Equal(
            "Memory was deleted, but the dashboard could not be refreshed.",
            viewModel.SafeStatusMessage);
    }

    [Theory]
    [InlineData("create")]
    [InlineData("archive")]
    [InlineData("initialize")]
    [InlineData("search")]
    [InlineData("detail")]
    [InlineData("delete")]
    public async Task UnexpectedInvalidOperationIsNotSwallowed(string action)
    {
        var unexpected = new InvalidOperationException("programming error");
        var service = new ServiceState
        {
            Create = (_, _) => Task.FromException<AedaMemoryOperationResult>(unexpected),
            Archive = (_, _) => Task.FromException<AedaMemoryOperationResult>(unexpected),
            Dashboard = _ => Task.FromException<AedaMemoryDashboardModel>(unexpected),
            Search = (_, _, _) => Task.FromException<IReadOnlyList<AedaMemoryRecordSummary>>(unexpected),
            Detail = (_, _) => Task.FromException<AedaMemoryRecordDetail?>(unexpected),
            Delete = (_, _) => Task.FromException<AedaMemoryOperationResult>(unexpected)
        };
        var viewModel = CreateViewModel(service);
        viewModel.NewMemoryText = "draft A";

        var operation = action switch
        {
            "create" => viewModel.CreateExplicitMemoryAsync(),
            "archive" => viewModel.ArchiveMemoryAsync(MemoryRow),
            "initialize" => viewModel.InitializeAsync(),
            "search" => viewModel.SearchMemoriesAsync(),
            "detail" => viewModel.OpenMemoryDetailAsync(MemoryRow),
            _ => viewModel.DeleteMemoryAsync(MemoryRow)
        };
        var thrown = await Assert.ThrowsAsync<InvalidOperationException>(() => operation);

        Assert.Same(unexpected, thrown);
    }

    [Fact]
    public async Task ActualArchiveEventContainsStorageFailureInAnIsolatedProcess()
    {
        const string probeVariable = "AEDA_MEMORY_FAILURE_PROBE";
        var action = Environment.GetEnvironmentVariable(probeVariable);
        if (action is not null)
        {
            RunUiBoundaryProbe(action);
            return;
        }

        await RunBoundaryInIsolatedProcessAsync(
            "archive",
            nameof(ActualArchiveEventContainsStorageFailureInAnIsolatedProcess));
    }

    [Fact]
    public async Task ActualRemainingMemoryBoundariesContainStorageFailureInIsolatedProcesses()
    {
        const string probeVariable = "AEDA_MEMORY_FAILURE_PROBE";
        var action = Environment.GetEnvironmentVariable(probeVariable);
        if (action is not null)
        {
            RunUiBoundaryProbe(action);
            return;
        }

        foreach (var boundary in new[] { "initialize", "search", "detail", "delete" })
        {
            await RunBoundaryInIsolatedProcessAsync(
                boundary,
                nameof(ActualRemainingMemoryBoundariesContainStorageFailureInIsolatedProcesses));
        }
    }

    private static async Task RunBoundaryInIsolatedProcessAsync(
        string action,
        string testMethod)
    {
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
            "." + testMethod);
        startInfo.Environment["AEDA_MEMORY_FAILURE_PROBE"] = action;

        using var process = System.Diagnostics.Process.Start(startInfo)!;
        var standardOutput = process.StandardOutput.ReadToEndAsync();
        var standardError = process.StandardError.ReadToEndAsync();
        await process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(15));
        var output = await standardOutput + await standardError;

        Assert.True(process.ExitCode == 0, $"{action}: {output}");
    }

    private static void RunUiBoundaryProbe(string action)
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
                var pendingDashboard = Pending<AedaMemoryDashboardModel>();
                var pendingSearch = Pending<IReadOnlyList<AedaMemoryRecordSummary>>();
                var pendingDetail = Pending<AedaMemoryRecordDetail?>();
                var pendingOperation = PendingOperation();
                var service = new ServiceState();
                var viewModel = CreateViewModel(service);
                var dashboard = Dashboard();
                viewModel.Dashboard = dashboard;
                viewModel.SearchText = "remember";
                viewModel.SearchMemoriesAsync().GetAwaiter().GetResult();
                var searchResults = viewModel.SearchResults;
                viewModel.OpenMemoryDetailAsync(MemoryRow).GetAwaiter().GetResult();
                var selectedMemory = viewModel.SelectedMemory;
                service.Dashboard = _ => pendingDashboard.Task;
                service.Search = (_, _, _) => pendingSearch.Task;
                service.Detail = (_, _) => pendingDetail.Task;
                service.Archive = (_, _) => pendingOperation.Task;
                service.Delete = (_, _) => pendingOperation.Task;
                var expectedStatus = action switch
                {
                    "initialize" => "Memory could not be loaded. Try again.",
                    "search" => "Memory search is temporarily unavailable. Try again.",
                    "detail" => "Memory detail could not be loaded. Try again.",
                    "delete" => "Memory could not be deleted. Try again.",
                    _ => "Memory could not be archived. Try again."
                };
                using var statusChanged = new ManualResetEventSlim();
                viewModel.PropertyChanged += (_, e) =>
                {
                    if (e.PropertyName == nameof(AedaMemoryModuleViewModel.SafeStatusMessage) &&
                        viewModel.SafeStatusMessage == expectedStatus)
                    {
                        statusChanged.Set();
                    }
                };
                var view = new MemoryView { DataContext = viewModel };
                var button = new Button { DataContext = MemoryRow };
                Dispatcher.UIThread.Post(() =>
                {
                    if (action == "search")
                    {
                        viewModel.SearchMemoriesCommand.Execute(null);
                        return;
                    }

                    var handlerName = action switch
                    {
                        "initialize" => "OnAttachedToVisualTree",
                        "detail" => "OnOpenMemoryClick",
                        "delete" => "OnDeleteMemoryClick",
                        _ => "OnArchiveMemoryClick"
                    };
                    typeof(MemoryView).GetMethod(
                        handlerName,
                        BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.DeclaredOnly)!.Invoke(
                            view,
                            [action == "initialize" ? view : button, null]);
                });
                Dispatcher.UIThread.RunJobs();
                switch (action)
                {
                    case "initialize":
                        pendingDashboard.SetException(StorageFailure());
                        break;
                    case "search":
                        pendingSearch.SetException(StorageFailure());
                        break;
                    case "detail":
                        pendingDetail.SetException(StorageFailure());
                        break;
                    default:
                        pendingOperation.SetException(StorageFailure());
                        break;
                }

                var timeout = DateTime.UtcNow + TimeSpan.FromSeconds(2);
                while (!statusChanged.IsSet && DateTime.UtcNow < timeout)
                {
                    Dispatcher.UIThread.RunJobs();
                    Thread.Yield();
                }

                Assert.True(statusChanged.IsSet);
                Assert.Equal(expectedStatus, viewModel.SafeStatusMessage);
                Assert.DoesNotContain(StorageFailureMessage, viewModel.SafeStatusMessage);
                Assert.NotEqual("Memory search complete.", viewModel.SafeStatusMessage);
                Assert.NotEqual("Memory detail loaded.", viewModel.SafeStatusMessage);
                Assert.NotEqual("Memory deleted.", viewModel.SafeStatusMessage);
                if (action is "initialize" or "delete" or "archive")
                {
                    Assert.Same(dashboard, viewModel.Dashboard);
                }
                else if (action == "search")
                {
                    Assert.Same(searchResults, viewModel.SearchResults);
                }
                else
                {
                    Assert.Same(selectedMemory, viewModel.SelectedMemory);
                }
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
        new(service.CreateProxy(), new MemoryModuleRegistry());

    private static TaskCompletionSource<AedaMemoryOperationResult> PendingOperation() =>
        Pending<AedaMemoryOperationResult>();

    private static TaskCompletionSource<T> Pending<T>() =>
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

    private static AedaMemoryRecordDetail MemoryDetail() => new(
        MemoryRow.Id,
        MemoryRow.Kind,
        MemoryRow.Scope,
        MemoryRow.PreviewText,
        MemoryRow.Visibility,
        MemoryRow.SensitivityStatus,
        "High",
        new AedaMemorySourceSummary(
            "explicit_user_save",
            "Explicit user save",
            null,
            "Explicit user save",
            DateTimeOffset.UtcNow),
        DateTimeOffset.UtcNow,
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
        public Func<CancellationToken, Task<AedaMemoryDashboardModel>> Dashboard { get; set; } =
            _ => Task.FromResult(AedaMemoryReliabilityTests.Dashboard());
        public Func<string, int, CancellationToken, Task<IReadOnlyList<AedaMemoryRecordSummary>>> Search { get; set; } =
            (_, _, _) => Task.FromResult<IReadOnlyList<AedaMemoryRecordSummary>>([MemoryRow]);
        public Func<MemoryId, CancellationToken, Task<AedaMemoryRecordDetail?>> Detail { get; set; } =
            (_, _) => Task.FromResult<AedaMemoryRecordDetail?>(MemoryDetail());
        public Func<AedaMemoryCreateRequest, CancellationToken, Task<AedaMemoryOperationResult>> Create { get; set; } =
            (_, _) => Task.FromResult(Success);
        public Func<MemoryId, CancellationToken, Task<AedaMemoryOperationResult>> Archive { get; set; } =
            (_, _) => Task.FromResult(Success);
        public Func<MemoryId, CancellationToken, Task<AedaMemoryOperationResult>> Delete { get; set; } =
            (_, _) => Task.FromResult(Success);

        public AedaMemoryDashboardModel DashboardResult { get; private set; } = null!;
        public AedaMemoryCreateRequest? LastCreateRequest { get; private set; }
        public int DashboardCalls { get; private set; }
        public int SearchCalls { get; private set; }
        public int DetailCalls { get; private set; }
        public int CreateCalls { get; private set; }
        public int ArchiveCalls { get; private set; }
        public int DeleteCalls { get; private set; }

        public IAedaMemoryModuleService CreateProxy() =>
            Proxy<IAedaMemoryModuleService>((method, arguments) => method.Name switch
            {
                nameof(IAedaMemoryModuleService.GetDashboardAsync) => GetDashboard(
                    (CancellationToken)arguments![0]!),
                nameof(IAedaMemoryModuleService.SearchMemoriesAsync) => SearchMemories(
                    (string)arguments![0]!,
                    (int)arguments[1]!,
                    (CancellationToken)arguments[2]!),
                nameof(IAedaMemoryModuleService.GetMemoryDetailAsync) => GetMemoryDetail(
                    (MemoryId)arguments![0]!,
                    (CancellationToken)arguments[1]!),
                nameof(IAedaMemoryModuleService.CreateExplicitMemoryAsync) => CreateMemory(
                    (AedaMemoryCreateRequest)arguments![0]!,
                    (CancellationToken)arguments[1]!),
                nameof(IAedaMemoryModuleService.ArchiveMemoryAsync) => ArchiveMemory(
                    (MemoryId)arguments![0]!,
                    (CancellationToken)arguments[1]!),
                nameof(IAedaMemoryModuleService.DeleteMemoryAsync) => DeleteMemory(
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

        private Task<IReadOnlyList<AedaMemoryRecordSummary>> SearchMemories(
            string text,
            int limit,
            CancellationToken token)
        {
            SearchCalls++;
            return Search(text, limit, token);
        }

        private Task<AedaMemoryRecordDetail?> GetMemoryDetail(
            MemoryId memoryId,
            CancellationToken token)
        {
            DetailCalls++;
            return Detail(memoryId, token);
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

        private Task<AedaMemoryOperationResult> DeleteMemory(
            MemoryId memoryId,
            CancellationToken token)
        {
            DeleteCalls++;
            return Delete(memoryId, token);
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

    private sealed class MemoryModuleRegistry : IAedaModuleRegistry
    {
        private static readonly AedaModuleDescriptor MemoryModule = new(
            AedaModuleId.Memory,
            AedaModuleKind.Memory,
            "AEDA Memory",
            "Memory",
            "",
            AedaModuleStatus.Available,
            [
                new AedaModuleCapability(
                    "memory_search",
                    "Memory search",
                    AedaModuleCapabilityState.Available,
                    BackendCapability: PersonalAI.Core.Capabilities.BackendCapability.MemorySearch),
                new AedaModuleCapability(
                    "memory_edit",
                    "Memory edit",
                    AedaModuleCapabilityState.Available,
                    BackendCapability: PersonalAI.Core.Capabilities.BackendCapability.MemoryEdit)
            ],
            new AedaModuleRoute("aeda-memory"));

        public IReadOnlyList<AedaModuleDescriptor> ListModules() => [MemoryModule];
        public IReadOnlyList<AedaModuleDescriptor> ListEnabledModules() => [MemoryModule];
        public bool TryGetModule(AedaModuleId moduleId, out AedaModuleDescriptor module)
        {
            module = MemoryModule;
            return moduleId == AedaModuleId.Memory;
        }

        public IReadOnlyList<AedaModuleDescriptor> GetModulesByCapability(string capabilityId) =>
            [MemoryModule];
        public AedaModuleStatus GetAvailability(AedaModuleId moduleId) =>
            AedaModuleStatus.Available;
    }
}
