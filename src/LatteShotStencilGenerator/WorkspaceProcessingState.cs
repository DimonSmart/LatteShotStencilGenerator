namespace LatteShotStencilGenerator;

/// <summary>Coordinates mutually exclusive, user-initiated workspace processing.</summary>
public sealed class WorkspaceProcessingState
{
    public bool IsBusy { get; private set; }
    public string? Message { get; private set; }

    public bool TryBegin(string message)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(message);
        if (IsBusy) return false;

        IsBusy = true;
        Message = message;
        return true;
    }

    public void Complete()
    {
        IsBusy = false;
        Message = null;
    }
}
