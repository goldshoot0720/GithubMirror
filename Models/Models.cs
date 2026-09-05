using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace GithubMirror.Models;

public sealed class RepositoryInfo : ObservableObject
{
    private bool _isSelected;

    public bool IsSelected
    {
        get => _isSelected;
        set => SetField(ref _isSelected, value);
    }

    public string Name { get; set; } = string.Empty;
    public string Owner { get; set; } = string.Empty;
    public string FullName => string.IsNullOrWhiteSpace(Owner) ? Name : $"{Owner}/{Name}";
    public string CloneUrl { get; set; } = string.Empty;
    public string WebUrl { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public long SizeInBytes { get; set; }
    public string FormattedSize => SizeFormatter.Format(SizeInBytes);
    public int Stars { get; set; }
    public int Forks { get; set; }
    public bool IsPrivate { get; set; }
    public string Visibility => IsPrivate ? "私人" : "公開";
    public DateTimeOffset LastUpdated { get; set; }
    public string PrimaryLanguage { get; set; } = "—";
    public string SourcePlatform { get; set; } = "GitHub";
    public string SourceAccountName { get; set; } = string.Empty;
    public AccountCredential? SourceAccount { get; set; }
}

public sealed class AccountCredential
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Platform { get; set; } = "GitHub";
    public string Alias { get; set; } = string.Empty;
    public string Username { get; set; } = string.Empty;
    public string Token { get; set; } = string.Empty;
    public string ApiUrl { get; set; } = string.Empty;
    public string Organization { get; set; } = string.Empty;
    public string Project { get; set; } = string.Empty;
    public string Region { get; set; } = string.Empty;
    public string Profile { get; set; } = string.Empty;

    public string DisplayName
    {
        get
        {
            var name = string.IsNullOrWhiteSpace(Alias) ? Username : Alias;
            if (Platform == "AWS CodeCommit" && string.IsNullOrWhiteSpace(name))
                name = string.IsNullOrWhiteSpace(Profile) ? "default" : Profile;
            return $"{Platform} · {name}";
        }
    }

    public override string ToString() => DisplayName;
}

public sealed class MirrorTask : ObservableObject
{
    private MirrorStatus _status = MirrorStatus.Pending;
    private int _progressValue;
    private string _message = "等待中";
    private string? _errorMessage;
    private DateTimeOffset? _completedAt;

    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string SourceRepository { get; set; } = string.Empty;
    public string SourceAccount { get; set; } = string.Empty;
    public string TargetPlatform { get; set; } = string.Empty;
    public string TargetRepository { get; set; } = string.Empty;
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.Now;

    public MirrorStatus Status
    {
        get => _status;
        set
        {
            if (SetField(ref _status, value))
            {
                OnPropertyChanged(nameof(StatusText));
                OnPropertyChanged(nameof(IsRunning));
            }
        }
    }

    public string StatusText => Status switch
    {
        MirrorStatus.Pending => "等待中",
        MirrorStatus.InProgress => "鏡像中",
        MirrorStatus.Completed => "已完成",
        MirrorStatus.Failed => "失敗",
        MirrorStatus.Cancelled => "已取消",
        _ => Status.ToString()
    };

    public bool IsRunning => Status == MirrorStatus.InProgress;

    public int ProgressValue
    {
        get => _progressValue;
        set => SetField(ref _progressValue, Math.Clamp(value, 0, 100));
    }

    public string Message
    {
        get => _message;
        set => SetField(ref _message, value);
    }

    public string? ErrorMessage
    {
        get => _errorMessage;
        set => SetField(ref _errorMessage, value);
    }

    public DateTimeOffset? CompletedAt
    {
        get => _completedAt;
        set => SetField(ref _completedAt, value);
    }
}

public enum MirrorStatus { Pending, InProgress, Completed, Failed, Cancelled }

public sealed record GitRemote(string Url, string Username = "", string Password = "");
public sealed record MirrorProgress(int Percentage, string Message);

public static class SizeFormatter
{
    private static readonly string[] Units = ["B", "KB", "MB", "GB", "TB"];

    public static string Format(long bytes)
    {
        if (bytes <= 0) return "0 B";
        var value = (double)bytes;
        var unit = 0;
        while (value >= 1024 && unit < Units.Length - 1)
        {
            value /= 1024;
            unit++;
        }

        return unit == 0 ? $"{value:0} {Units[unit]}" : $"{value:0.##} {Units[unit]}";
    }
}

public abstract class ObservableObject : INotifyPropertyChanged
{
    public event PropertyChangedEventHandler? PropertyChanged;

    protected bool SetField<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value)) return false;
        field = value;
        OnPropertyChanged(propertyName);
        return true;
    }

    protected void OnPropertyChanged([CallerMemberName] string? propertyName = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
}
