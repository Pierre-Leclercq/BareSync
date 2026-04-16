using System.Text.RegularExpressions;
using Bare.Primitive.UI;
using BareSync.UI;
using Xunit;

namespace BareSync.Tests;

public sealed class OperationRunnerTests
{
    [Fact]
    public void EnsureProgressAnchor_WhenNotInitialized_UsesCurrentCursorTop()
    {
        var anchor = OperationRunner.EnsureProgressAnchor(
            progressTop: 0,
            progressAnchorInitialized: false,
            currentCursorTop: 12);

        Assert.Equal(12, anchor.ProgressTop);
        Assert.True(anchor.AnchorInitialized);
    }

    [Fact]
    public void EnsureProgressAnchor_WhenAlreadyInitialized_KeepsExistingTop()
    {
        var anchor = OperationRunner.EnsureProgressAnchor(
            progressTop: 7,
            progressAnchorInitialized: true,
            currentCursorTop: 25);

        Assert.Equal(7, anchor.ProgressTop);
        Assert.True(anchor.AnchorInitialized);
    }

    [Fact]
    public async Task RunAsync_RespectsThrottleAndDedupes()
    {
        var throttle = new TestThrottle(skipProcessed: 2);
        var options = new OperationRunnerOptions
        {
            OperationTitle = "Test operation",
            RenderMode = RenderMode.Full,
            Throttle = throttle
        };

        var console = new MockConsoleAdapter();
        using var _ = new TestConsoleScope(console);

        var result = await OperationRunner.RunAsync(options, async progress =>
        {
            progress.Report(new ProgressInfo { Processed = 1, Total = 3, LastLine = "A" });
            progress.Report(new ProgressInfo { Processed = 1, Total = 3, LastLine = "A" });
            progress.Report(new ProgressInfo { Processed = 2, Total = 3, LastLine = "B" });
            progress.Report(new ProgressInfo { Processed = 3, Total = 3, LastLine = "C" });
            return new OperationResult { StatusLine = "Done", SuccessOrWarningFlag = true };
        });

        var output = console.OutputText;
        var renderCount = Regex.Matches(output, "^Operation:", RegexOptions.Multiline).Count;

        Assert.Equal(3, renderCount);
        Assert.Equal("Done", result.StatusLine);
    }

    [Fact]
    public async Task RunAsync_DoesNotRenderWhenRenderModeNone()
    {
        var options = new OperationRunnerOptions
        {
            OperationTitle = "Quiet mode",
            RenderMode = RenderMode.None
        };

        var console = new MockConsoleAdapter();
        using var _ = new TestConsoleScope(console);

        await OperationRunner.RunAsync(options, async progress =>
        {
            progress.Report(new ProgressInfo { Processed = 1, Total = 2, LastLine = "A" });
            progress.Report(new ProgressInfo { Processed = 2, Total = 2, LastLine = "B" });
            return new OperationResult { StatusLine = "Done", SuccessOrWarningFlag = true };
        });

        Assert.Equal(string.Empty, console.OutputText);
    }

    [Fact]
    public async Task RunAsync_CancelsOnEscape()
    {
        var escapeSignal = new FakeEscapeSignal();
        var options = new OperationRunnerOptions
        {
            OperationTitle = "Cancelable",
            RenderMode = RenderMode.None,
            EscapeSignal = escapeSignal
        };

        var started = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var cancellationObserved = false;
        var timeout = TimeSpan.FromSeconds(3);

        var runTask = OperationRunner.RunAsync(
            options,
            CancellationToken.None,
            async (_, token) =>
            {
                started.TrySetResult(true);
                try
                {
                    await Task.Delay(Timeout.InfiniteTimeSpan, token);
                }
                catch (OperationCanceledException) when (token.IsCancellationRequested)
                {
                    cancellationObserved = true;
                    throw;
                }

                return new OperationResult { StatusLine = "Should not complete" };
            });

        try
        {
            await started.Task.WaitAsync(timeout);
        }
        catch (TimeoutException)
        {
            Assert.Fail($"Timed out waiting for the operation to start within {timeout.TotalSeconds} seconds.");
            return;
        }
        escapeSignal.Trigger();

        OperationResult result;
        try
        {
            result = await runTask.WaitAsync(timeout);
        }
        catch (TimeoutException)
        {
            Assert.Fail($"Timed out waiting for the operation to cancel after ESC within {timeout.TotalSeconds} seconds.");
            return;
        }
        Assert.Equal("Canceled", result.StatusLine);
        Assert.False(result.SuccessOrWarningFlag);

        Assert.True(cancellationObserved);
    }

    [Fact]
    public async Task RunAsync_WithUiOutput_RendersProgressWithoutConsoleCoupling()
    {
        var output = new InMemoryUiOutput(width: 100, height: 5);
        var options = new OperationRunnerOptions
        {
            OperationTitle = "UI output test",
            RenderMode = RenderMode.Progress,
            UiOutput = output,
            Throttle = new TestThrottle(int.MinValue)
        };

        var result = await OperationRunner.RunAsync(options, async progress =>
        {
            progress.Report(new ProgressInfo
            {
                Processed = 3,
                Total = 10,
                LastLine = "phase-1",
                CurrentItem = "item-A"
            });

            return new OperationResult
            {
                SuccessOrWarningFlag = true,
                StatusLine = "Done"
            };
        });

        var lines = output.GetLines();
        Assert.Contains("UI output test", lines[0], StringComparison.Ordinal);
        Assert.Contains("3/10", lines[0], StringComparison.Ordinal);
        Assert.Contains("Current: item-A", lines[1], StringComparison.Ordinal);
        Assert.Equal("Done", result.StatusLine);
    }

    private sealed class FakeEscapeSignal : IEscapeSignal
    {
        private readonly TaskCompletionSource<bool> _triggered =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public Task WaitForEscapeAsync(CancellationToken cancellationToken)
        {
            return _triggered.Task.WaitAsync(cancellationToken);
        }

        public void Trigger()
        {
            _triggered.TrySetResult(true);
        }
    }

    private sealed class TestThrottle : IRefreshThrottle
    {
        private readonly int _skipProcessed;

        public TestThrottle(int skipProcessed)
        {
            _skipProcessed = skipProcessed;
        }

        public bool ShouldRefresh(int processed, int total, bool force = false)
        {
            if (force)
            {
                return true;
            }

            return processed != _skipProcessed;
        }

        public bool IsHeartbeatDue()
        {
            return false;
        }
    }
}
