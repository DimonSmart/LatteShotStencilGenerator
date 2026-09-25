using LatteShotStencilGenerator;
using Xunit;

namespace LatteShotStencilGenerator.Tests;

public sealed class WorkspaceProcessingStateTests
{
    [Fact]
    public void TryBegin_RejectsConflictingOperation_UntilCompleted()
    {
        var state = new WorkspaceProcessingState();

        Assert.True(state.TryBegin("Importing SVG artwork…"));
        Assert.True(state.IsBusy);
        Assert.Equal("Importing SVG artwork…", state.Message);
        Assert.False(state.TryBegin("Regenerating preview…"));

        state.Complete();

        Assert.False(state.IsBusy);
        Assert.Null(state.Message);
        Assert.True(state.TryBegin("Regenerating preview…"));
    }

    [Fact]
    public void Complete_ClearsBusyState_AfterFailedOperation()
    {
        var state = new WorkspaceProcessingState();
        state.TryBegin("Importing SVG artwork…");

        try
        {
            throw new InvalidOperationException("SVG parsing failed.");
        }
        catch (InvalidOperationException)
        {
            state.Complete();
        }

        Assert.False(state.IsBusy);
        Assert.Null(state.Message);
    }
}
