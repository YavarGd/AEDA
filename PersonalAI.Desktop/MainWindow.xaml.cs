using System.IO;
using System.Net.Http;
using System.Windows;
using PersonalAI.Core.Chat;
using PersonalAI.Infrastructure.Chat;

namespace PersonalAI.Desktop;

public partial class MainWindow : Window
{
    private readonly IChatProvider _chatProvider;
    private CancellationTokenSource? _generationCancellation;
    private bool _isGenerating;

    public MainWindow()
        : this(ChatProviderFactory.CreateDefaultLocalProvider())
    {
    }

    public MainWindow(IChatProvider chatProvider)
    {
        ArgumentNullException.ThrowIfNull(chatProvider);

        _chatProvider = chatProvider;
        InitializeComponent();
        SetStatus(ChatUiStatus.Idle);
        UpdateGenerationControls(isGenerating: false);
    }

    private async void SendButton_Click(object sender, RoutedEventArgs e)
    {
        if (_isGenerating)
        {
            return;
        }

        var prompt = PromptTextBox.Text.Trim();
        var model = ModelTextBox.Text.Trim();

        if (string.IsNullOrWhiteSpace(prompt))
        {
            SetStatus(ChatUiStatus.Error);
            ResponseTextBox.Text = "Enter a prompt before sending.";
            return;
        }

        if (string.IsNullOrWhiteSpace(model))
        {
            SetStatus(ChatUiStatus.Error);
            ResponseTextBox.Text = "Enter a model name before sending.";
            return;
        }

        _generationCancellation = new CancellationTokenSource();
        UpdateGenerationControls(isGenerating: true);
        SetStatus(ChatUiStatus.Generating);
        ResponseTextBox.Clear();

        try
        {
            var request = new ChatRequest(
                model,
                [new ChatMessage(ChatRole.User, prompt)]);

            await foreach (var chunk in _chatProvider.StreamAsync(
                               request,
                               _generationCancellation.Token))
            {
                if (!string.IsNullOrEmpty(chunk.Content))
                {
                    ResponseTextBox.AppendText(chunk.Content);
                    ResponseTextBox.ScrollToEnd();
                }

                if (chunk.IsComplete)
                {
                    break;
                }
            }

            SetStatus(ChatUiStatus.Completed);
        }
        catch (OperationCanceledException)
        {
            SetStatus(ChatUiStatus.Cancelled);
        }
        catch (HttpRequestException exception)
        {
            SetStatus(ChatUiStatus.Error);
            ResponseTextBox.Text = $"Could not connect to Ollama. {exception.Message}";
        }
        catch (InvalidDataException exception)
        {
            SetStatus(ChatUiStatus.Error);
            ResponseTextBox.Text = exception.Message;
        }
        catch (Exception exception)
        {
            SetStatus(ChatUiStatus.Error);
            ResponseTextBox.Text = exception.Message;
        }
        finally
        {
            _generationCancellation?.Dispose();
            _generationCancellation = null;
            UpdateGenerationControls(isGenerating: false);
        }
    }

    private void CancelButton_Click(object sender, RoutedEventArgs e)
    {
        _generationCancellation?.Cancel();
    }

    private void UpdateGenerationControls(bool isGenerating)
    {
        _isGenerating = isGenerating;
        SendButton.IsEnabled = !isGenerating;
        CancelButton.IsEnabled = isGenerating;
    }

    private void SetStatus(ChatUiStatus status)
    {
        StatusTextBlock.Text = status.ToString();
    }
}
