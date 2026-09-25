namespace LatteShotStencilGenerator;

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

    public void UpdateMessage(string message)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(message);
        if (!IsBusy)
            throw new InvalidOperationException("Cannot update the processing stage when no operation is active.");

        Message = message;
    }

    public void Complete()
    {
        IsBusy = false;
        Message = null;
    }
}
