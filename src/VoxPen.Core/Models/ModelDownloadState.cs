namespace VoxPen.Core.Models;

public enum ModelDownloadState
{
    Idle,
    Downloading,
    Verifying,
    Installing,
    Completed,
    Canceled,
    Failed,
}
