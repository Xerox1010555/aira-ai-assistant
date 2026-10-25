using System;
using System.Collections.ObjectModel;
using System.Threading.Tasks;
using System.Windows.Input;
using AiraAssistant.Infrastructure.Logging;
using AiraAssistant.Models;
using AiraAssistant.Services.Ai;
using AiraAssistant.Services.History;

namespace AiraAssistant.UI.ViewModels;

/// <summary>
/// Root view model. Owns the chat history collection, the input box state,
/// and dispatches between the user, the AI service, and the speech/router
/// services via events that the <c>AppBootstrapper</c> subscribes to.
///
/// v1.1 additions:
///  - SQLite persistence through <see cref="HistoryStore"/>: conversations
///    survive restarts (v1 lost everything on close).
///  - Barge-in: user speech while Aira is speaking cancels the TTS.
///  - Tool executions are written both to the sidebar log and to the DB.
/// </summary>
public sealed class MainViewModel : ViewModelBase
{
    private readonly IAiService _ai;
    private readonly ILogger _logger;
    private readonly HistoryStore? _history;

    // SQLite session id of the active conversation (empty when persistence is unavailable).
    private string _sessionId = string.Empty;

    private string _userInput = string.Empty;
    private string _partialTranscript = string.Empty;
    private VoiceSessionState _sessionState = VoiceSessionState.Idle;
    private bool _isListening;
    private string _statusText = "Ready.";
    private bool _isElevated;
    private bool _isThinking;
    private string? _activeProvider;
    private string? _activeModel;

    public ObservableCollection<ChatMessageViewModel> Messages { get; } = new();
    public ObservableCollection<string> ToolLog { get; } = new();

    public string UserInput
    {
        get => _userInput;
        set
        {
            if (SetProperty(ref _userInput, value))
            {
                ((AsyncRelayCommand)SendCommand).RaiseCanExecuteChanged();
            }
        }
    }

    public string PartialTranscript
    {
        get => _partialTranscript;
        private set => SetProperty(ref _partialTranscript, value);
    }

    public VoiceSessionState SessionState
    {
        get => _sessionState;
        private set
        {
            if (SetProperty(ref _sessionState, value))
            {
                RaisePropertyChanged(nameof(IsListening));
                RaisePropertyChanged(nameof(SessionStateLabel));
            }
        }
    }

    public bool IsListening
    {
        get => _isListening;
        private set => SetProperty(ref _isListening, value);
    }

    public string SessionStateLabel => _sessionState switch
    {
        VoiceSessionState.Idle => "Idle",
        VoiceSessionState.Listening => "Listening…",
            VoiceSessionState.Thinking => "Thinking…",
        VoiceSessionState.Speaking => "Speaking…",
        VoiceSessionState.Error => "Error",
        _ => "Unknown"
    };

    public string StatusText
    {
        get => _statusText;
        private set => SetProperty(ref _statusText, value);
    }

    public bool IsElevated
    {
        get => _isElevated;
        private set => SetProperty(ref _isElevated, value);
    }

    public bool IsThinking
    {
        get => _isThinking;
        private set => SetProperty(ref _isThinking, value);
    }

    public string? ActiveProvider
    {
        get => _activeProvider;
        private set => SetProperty(ref _activeProvider, value);
    }

    public string? ActiveModel
    {
        get => _activeModel;
        private set => SetProperty(ref _activeModel, value);
    }

    public ICommand SendCommand { get; }
    public ICommand ClearCommand { get; }
    public ICommand ToggleListeningCommand { get; }

    /// <summary>Event the bootstrapper uses to ask the TTS engine to speak.</summary>
    public event EventHandler<string>? RequestSpeakAsync;
    /// <summary>Event the bootstrapper uses to cancel ongoing speech (barge-in).</summary>
    public event EventHandler? RequestStopSpeakingAsync;
    /// <summary>Event the bootstrapper uses to begin capturing + recognizing audio.</summary>
    public event EventHandler? RequestStartListeningAsync;
    /// <summary>Event the bootstrapper uses to stop the voice session.</summary>
    public event EventHandler? RequestStopListeningAsync;

    public MainViewModel(IAiService ai, ILogger logger, HistoryStore? history = null)
    {
        _ai = ai ?? throw new ArgumentNullException(nameof(ai));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _history = history;

        ActiveProvider = _ai.ProviderName;
        ActiveModel = _ai.ModelName;

        SendCommand = new AsyncRelayCommand(SendAsync, () => !string.IsNullOrWhiteSpace(UserInput) && !IsThinking);
        ClearCommand = new RelayCommand(ClearChat);
        ToggleListeningCommand = new AsyncRelayCommand(ToggleListeningAsync);

        // Seed the conversation with the system prompt from the active AI service.
        var sys = _ai.GetSystemPrompt();
        if (!string.IsNullOrWhiteSpace(sys))
        {
            _conversation.Add(new ChatMessage { Role = ChatRole.System, Content = sys });
        }

        LoadPersistedHistory();
    }

    // -----------------------------------------------------------------
    // Persistence
    // -----------------------------------------------------------------

    private Conversation _conversation { get; } = new();

    /// <summary>Restores the most recent session (or creates one). Fail-safe.</summary>
    private void LoadPersistedHistory()
    {
        if (_history is null) return;
        try
        {
            _history.TrimSessions(keep: 50); // housekeeping, best effort
            SwitchToSessionInternal(_history.GetOrCreateLatestSession(), announce: true);
        }
        catch (Exception ex)
        {
            _logger.Warning($"History restore failed; starting fresh. ({ex.Message})");
        }
    }

    /// <summary>Public hook for the Sessions dialog: open an existing session.</summary>
    public void SwitchToSession(string sessionId) => SwitchToSessionInternal(sessionId, announce: false);

    /// <summary>True when the given id is the session currently open in the chat.</summary>
    public bool IsCurrentSession(string sessionId) =>
        !string.IsNullOrEmpty(sessionId) && string.Equals(_sessionId, sessionId, StringComparison.Ordinal);

    /// <summary>Public hook for the Sessions dialog: discard the current chat and start empty.</summary>
    public void StartEmptySession()
    {
        var newId = _history is null ? Guid.NewGuid().ToString("N") : _history.StartNewSession();
        SwitchToSessionInternal(newId);
        StatusText = "New session started.";
    }

    private void SwitchToSessionInternal(string sessionId, bool announce = false)
    {
        // Cancel any in-flight speech before swapping contexts.
        RequestStopSpeakingAsync?.Invoke(this, EventArgs.Empty);

        Messages.Clear();
        ResetConversation();
        _sessionId = sessionId;

        var loaded = _history is null
            ? (IReadOnlyList<ChatMessage>)Array.Empty<ChatMessage>()
            : _history.LoadMessages(sessionId);
        foreach (var msg in loaded)
        {
            _conversation.Add(msg);
            if (msg.Role is ChatRole.User or ChatRole.Assistant && !string.IsNullOrWhiteSpace(msg.Content))
            {
                Messages.Add(new ChatMessageViewModel
                {
                    Author = msg.Role == ChatRole.User ? MessageAuthor.User : MessageAuthor.Assistant,
                    Text = msg.Content,
                    Timestamp = msg.Timestamp
                });
            }
        }

        SessionState = VoiceSessionState.Idle;
        IsListening = false;
        IsThinking = false;
        StatusText = announce
            ? $"Restored session ({Messages.Count} messages)."
            : $"Loaded session ({Messages.Count} messages).";
        AutoScrollRequested?.Invoke(this, EventArgs.Empty);
    }

    private void ResetConversation()
    {
        _conversation.Clear();
        var sys = _ai.GetSystemPrompt();
        if (!string.IsNullOrWhiteSpace(sys))
        {
            _conversation.Add(new ChatMessage { Role = ChatRole.System, Content = sys });
        }
    }

    private void SaveToHistory(ChatMessage message)
    {
        if (_history is null || string.IsNullOrEmpty(_sessionId)) return;
        _ = System.Threading.Tasks.Task.Run(() => _history.AppendMessage(_sessionId, message));
    }

    /// <summary>UI asks the view to scroll to the newest bubble.</summary>
    public event EventHandler? AutoScrollRequested;

    // -----------------------------------------------------------------
    // Public hooks called by the bootstrapper on background service events.
    // -----------------------------------------------------------------

    public void SetElevationStatus(bool elevated)
    {
        IsElevated = elevated;
        StatusText = elevated
            ? "Running with Administrator privileges."
            : "Running as a standard user (normal; admin not required).";
    }

    public void OnPartialTranscript(string text)
    {
        // True barge-in: any interim user speech while Aira is speaking cancels it.
        if (SessionState == VoiceSessionState.Speaking && !string.IsNullOrWhiteSpace(text))
        {
            RequestStopSpeakingAsync?.Invoke(this, EventArgs.Empty);
        }
        PartialTranscript = text;
    }

    public void OnRecognitionError(string message)
    {
        SessionState = VoiceSessionState.Error;
        StatusText = $"Recognition error: {message}";
    }

    public void OnAssistantChunk(string chunk)
    {
        if (Messages.Count == 0 || Messages[^1].Author != MessageAuthor.Assistant)
        {
            var msg = new ChatMessageViewModel { Author = MessageAuthor.Assistant };
            msg.MarkStreaming();
            Messages.Add(msg);
        }
        Messages[^1].Append(chunk);
        AutoScrollRequested?.Invoke(this, EventArgs.Empty);
    }

    public void OnAssistantComplete(string fullText)
    {
        if (Messages.Count > 0 && Messages[^1].Author == MessageAuthor.Assistant)
        {
            Messages[^1].MarkStreamingComplete();
        }
        IsThinking = false;
        SessionState = IsListening ? VoiceSessionState.Listening : VoiceSessionState.Idle;
        StatusText = IsListening ? "Listening…" : "Ready.";

        // NOTE: the AI service already appended the assistant turn to the
        // shared conversation (it is the single writer for assistant/tool
        // turns). The VM only mirrors it into the UI and SQLite.

        // Persist the finished assistant turn.
        if (!string.IsNullOrWhiteSpace(fullText))
        {
            SaveToHistory(new ChatMessage { Role = ChatRole.Assistant, Content = fullText });
        }

        // Auto-speak the assistant's reply in a voice session.
        if (IsListening && !string.IsNullOrWhiteSpace(fullText))
        {
            SessionState = VoiceSessionState.Speaking;
            _ = RaiseSpeakAsync(fullText);
        }
    }

    public void OnSpeechPlaybackStarted()
    {
        SessionState = VoiceSessionState.Speaking;
    }

    public void OnSpeechPlaybackFinished()
    {
        SessionState = IsListening ? VoiceSessionState.Listening : VoiceSessionState.Idle;
    }

    public void OnToolExecuted(string callId, string toolName, string result)
    {
        var summary = result.Length > 120 ? result[..120] + "…" : result;
        ToolLog.Insert(0, $"{DateTime.Now:HH:mm:ss}  {toolName}  →  {summary}");
        if (ToolLog.Count > 25) ToolLog.RemoveAt(ToolLog.Count - 1);

        // Persist the tool result (the AI service owns the conversation copy).
        SaveToHistory(new ChatMessage
        {
            Role = ChatRole.Tool,
            Content = result,
            ToolCallId = callId,
            ToolName = toolName
        });
    }

    public void OnAiError(string message)
    {
        IsThinking = false;
        SessionState = VoiceSessionState.Error;
        StatusText = $"AI error: {message}";
        AppendSystem($"⚠️  {message}");
    }

    public async Task SubmitUserMessageAsync(string text)
    {
        if (string.IsNullOrWhiteSpace(text)) return;
        await SendUserTextAsync(text).ConfigureAwait(true);
    }

    // -----------------------------------------------------------------
    // Command implementations.
    // -----------------------------------------------------------------

    private async Task SendAsync()
    {
        var text = UserInput;
        UserInput = string.Empty;
        await SendUserTextAsync(text).ConfigureAwait(true);
    }

    private async Task SendUserTextAsync(string text)
    {
        text = text?.Trim() ?? string.Empty;
        if (text.Length == 0) return;

        // Typed input also cancels any in-flight speech (barge-in parity).
        if (SessionState == VoiceSessionState.Speaking)
        {
            RequestStopSpeakingAsync?.Invoke(this, EventArgs.Empty);
        }

        try
        {
            AppendUser(text);
            var msg = new ChatMessage { Role = ChatRole.User, Content = text };
            _conversation.Add(msg);
            SaveToHistory(msg);
            _history?.AutoTitle(_sessionId, text);
            IsThinking = true;
            SessionState = VoiceSessionState.Thinking;
            StatusText = "Thinking…";
            await _ai.SendUserMessageAsync(_conversation).ConfigureAwait(true);
        }
        catch (Exception ex)
        {
            _logger.Error("Send failed", ex);
            IsThinking = false;
            SessionState = VoiceSessionState.Error;
            StatusText = $"Send failed: {ex.Message}";
            AppendSystem($"⚠️  Failed to send: {ex.Message}");
        }
    }

    private void ClearChat()
    {
        try
        {
            if (_history is not null && !string.IsNullOrEmpty(_sessionId))
            {
                _history.ClearMessages(_sessionId);
            }
            StartEmptySession();
        }
        catch (Exception ex)
        {
            _logger.Warning($"Failed to clear history: {ex.Message}");
        }
    }

    private async Task ToggleListeningAsync()
    {
        if (IsListening)
        {
            RequestStopListeningAsync?.Invoke(this, EventArgs.Empty);
            IsListening = false;
            SessionState = VoiceSessionState.Idle;
            StatusText = "Voice session stopped.";
        }
        else
        {
            RequestStartListeningAsync?.Invoke(this, EventArgs.Empty);
            IsListening = true;
            SessionState = VoiceSessionState.Listening;
            StatusText = "Listening…";
        }
        await Task.CompletedTask.ConfigureAwait(false);
    }

    // -----------------------------------------------------------------
    // Helpers.
    // -----------------------------------------------------------------

    private void AppendUser(string text)
    {
        Messages.Add(new ChatMessageViewModel
        {
            Author = MessageAuthor.User,
            Text = text
        });
        AutoScrollRequested?.Invoke(this, EventArgs.Empty);
    }

    private void AppendSystem(string text)
    {
        Messages.Add(new ChatMessageViewModel
        {
            Author = MessageAuthor.System,
            Text = text
        });
        AutoScrollRequested?.Invoke(this, EventArgs.Empty);
    }

    private Task RaiseSpeakAsync(string text)
    {
        // Subscribers are async (TTS), but we just fire and forget here.
        RequestSpeakAsync?.Invoke(this, text);
        return Task.CompletedTask;
    }
}
