using System;
using System.Text;

namespace GithubMirror.Models;

public enum MirrorStatus
{
    Pending,
    Running,
    Completed,
    Failed,
    Cancelled
}

public class MirrorJob : ObservableObject
{
    private MirrorStatus _status = MirrorStatus.Pending;
    private string _step = "等待中";
    private double _percent;
    private string? _errorMessage;
    private DateTime? _completedAt;

    private readonly StringBuilder _log = new();

    public string Id { get; } = Guid.NewGuid().ToString("N");

    public string SourceRepository { get; init; } = string.Empty;
    public string SourcePlatform { get; init; } = string.Empty;
    public string SourceAccount { get; init; } = string.Empty;
    public string TargetPlatform { get; init; } = string.Empty;
    public string TargetAccount { get; init; } = string.Empty;
    public string TargetRepository { get; init; } = string.Empty;
    public DateTime CreatedAt { get; } = DateTime.Now;

    public MirrorStatus Status
    {
        get => _status;
        set
        {
            if (SetProperty(ref _status, value))
            {
                OnPropertyChanged(nameof(StatusDisplay));
                OnPropertyChanged(nameof(IsFinished));
            }
        }
    }

    public string StatusDisplay => Status switch
    {
        MirrorStatus.Pending => "等待中",
        MirrorStatus.Running => "進行中",
        MirrorStatus.Completed => "完成",
        MirrorStatus.Failed => "失敗",
        MirrorStatus.Cancelled => "已取消",
        _ => Status.ToString()
    };

    public bool IsFinished => Status is MirrorStatus.Completed or MirrorStatus.Failed or MirrorStatus.Cancelled;

    public string Step
    {
        get => _step;
        set => SetProperty(ref _step, value);
    }

    public double Percent
    {
        get => _percent;
        set
        {
            if (SetProperty(ref _percent, value))
                OnPropertyChanged(nameof(PercentDisplay));
        }
    }

    public string PercentDisplay => $"{Percent:0}%";

    public string? ErrorMessage
    {
        get => _errorMessage;
        set => SetProperty(ref _errorMessage, value);
    }

    public DateTime? CompletedAt
    {
        get => _completedAt;
        set
        {
            if (SetProperty(ref _completedAt, value))
                OnPropertyChanged(nameof(CompletedAtDisplay));
        }
    }

    public string CompletedAtDisplay => CompletedAt?.ToString("yyyy-MM-dd HH:mm:ss") ?? "—";

    public string Route => $"{SourcePlatform} → {TargetPlatform}";

    public string Log => _log.ToString();

    public void AppendLog(string line)
    {
        if (string.IsNullOrWhiteSpace(line)) return;
        _log.AppendLine($"[{DateTime.Now:HH:mm:ss}] {line.TrimEnd()}");
        OnPropertyChanged(nameof(Log));
    }
}
