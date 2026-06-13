using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using Microsoft.UI.Dispatching;
using PersonalAI.Core.Tasks;

namespace PersonalAI.Desktop.WinUI.ViewModels;

public sealed partial class TaskTimelineViewModel : ObservableObject, IDisposable
{
    private readonly ITaskEventBus _eventBus;
    private readonly DispatcherQueue _dispatcherQueue;
    private CancellationTokenSource? _subscriptionCancellation;

    public TaskTimelineViewModel(
        ITaskEventBus eventBus,
        DispatcherQueue dispatcherQueue)
    {
        _eventBus = eventBus ?? throw new ArgumentNullException(nameof(eventBus));
        _dispatcherQueue = dispatcherQueue ??
            throw new ArgumentNullException(nameof(dispatcherQueue));
    }

    public ObservableCollection<TaskEventItemViewModel> Events { get; } = [];

    [ObservableProperty]
    private string _currentState = "No task activity";

    [ObservableProperty]
    private bool _hasEvents;

    public void ObserveTask(TaskId taskId)
    {
        _subscriptionCancellation?.Cancel();
        _subscriptionCancellation?.Dispose();
        _subscriptionCancellation = new CancellationTokenSource();
        SetOnUiThread(() =>
        {
            Events.Clear();
            HasEvents = false;
            CurrentState = "Waiting for task activity";
        });

        var cancellationToken = _subscriptionCancellation.Token;
        _ = Task.Run(
            async () =>
            {
                try
                {
                    await foreach (var taskEvent in _eventBus.SubscribeAsync(
                                       taskId,
                                       cancellationToken))
                    {
                        SetOnUiThread(() => AddEvent(taskEvent));
                    }
                }
                catch (OperationCanceledException)
                {
                }
            },
            CancellationToken.None);
    }

    public void Dispose()
    {
        _subscriptionCancellation?.Cancel();
        _subscriptionCancellation?.Dispose();
        _subscriptionCancellation = null;
    }

    private void AddEvent(TaskEvent taskEvent)
    {
        Events.Add(new TaskEventItemViewModel(taskEvent));
        HasEvents = true;
        CurrentState = taskEvent.Summary;
    }

    private void SetOnUiThread(Action action)
    {
        if (!_dispatcherQueue.HasThreadAccess)
        {
            _dispatcherQueue.TryEnqueue(() => action());
            return;
        }

        action();
    }
}
