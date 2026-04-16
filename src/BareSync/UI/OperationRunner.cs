using Bare.Primitive.UI;

namespace BareSync.UI;

internal static class OperationRunner
{
    private const int ProgressLineCount = 2;

    public static async Task<OperationResult> RunAsync(
        OperationRunnerOptions options,
        Func<IProgress<ProgressInfo>, Task<OperationResult>> action)
    {
        return await RunAsync(
            options,
            CancellationToken.None,
            (progress, _) => action(progress)).ConfigureAwait(false);
    }

    public static async Task<OperationResult> RunAsync(
        OperationRunnerOptions options,
        CancellationToken cancellationToken,
        Func<IProgress<ProgressInfo>, CancellationToken, Task<OperationResult>> action)
    {
        if (options is null)
        {
            throw new ArgumentNullException(nameof(options));
        }

        if (action is null)
        {
            throw new ArgumentNullException(nameof(action));
        }

        var throttle = options.Throttle ?? new ConsoleRefreshThrottle();
        var currentOperationTitle = options.OperationTitle ?? string.Empty;
        var currentLastLine = (string?)null;
        var currentItem = (string?)null;
        var renderMode = options.RenderMode;
        var hasRendered = false;
        var progressCleared = false;
        var progressTop = 0;
        var progressAnchorInitialized = false;
        var cursorHidden = false;
        var startedAt = DateTime.UtcNow;
        var prevCurrentLen = 0;
        var prevCurrentWidth = 0;
        using var escapeCts = new CancellationTokenSource();
        using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, escapeCts.Token);
        var escapeSignal = options.EscapeSignal ?? ConsoleEscapeSignal.Instance;
        using var stopCts = new CancellationTokenSource();
        var cancelWatcher = Task.Run(async () =>
        {
            try
            {
                await escapeSignal.WaitForEscapeAsync(stopCts.Token).ConfigureAwait(false);
                if (!stopCts.IsCancellationRequested)
                {
                    escapeCts.Cancel();
                }
            }
            catch (OperationCanceledException)
            {
            }
            catch
            {
            }
        });
        var lastRendered = new RenderSnapshot(
            Processed: int.MinValue,
            Total: int.MinValue,
            LastLine: null,
            OperationTitle: string.Empty,
            CurrentItem: null);

        void Render(ProgressInfo info, bool force)
        {
            if (!UiInteraction.IsRenderingEnabled)
            {
                return;
            }

            if (renderMode == RenderMode.Progress)
            {
                RenderProgressOnly(info, force);
                return;
            }

            if (renderMode != RenderMode.Full)
            {
                return;
            }

            var operationTitle = currentOperationTitle;
            if (string.IsNullOrWhiteSpace(operationTitle))
            {
                operationTitle = string.Empty;
            }

            var lastLine = options.ShowLastLine ? currentLastLine : null;

            var heartbeat = throttle.IsHeartbeatDue();
            if (!force && !heartbeat
                && IsDuplicate(info.Processed, info.Total, lastLine, operationTitle, currentItem, lastRendered))
            {
                return;
            }

            if (!throttle.ShouldRefresh(info.Processed, info.Total, force))
            {
                return;
            }

            if (options.ClearAtStart || hasRendered)
            {
                ClearRenderTarget(options);
            }

            var elapsed = GetElapsed(info, startedAt);
            ScreenRenderer.Render(new OperationScreen(
                options.Header,
                operationTitle,
                info.Processed,
                info.Total,
                lastLine,
                elapsed,
                currentItem),
                options.UiOutput);
            hasRendered = true;
            lastRendered = new RenderSnapshot(
                info.Processed,
                info.Total,
                lastLine,
                operationTitle,
                currentItem);
        }

        void RenderProgressOnly(ProgressInfo info, bool force)
        {
            if (options.UiOutput is not null)
            {
                var operationTitleForOutput = string.IsNullOrWhiteSpace(currentOperationTitle)
                    ? string.Empty
                    : currentOperationTitle;
                var lastLineForOutput = options.ShowLastLine ? currentLastLine : null;
                var duplicateForOutput = IsDuplicate(
                    info.Processed,
                    info.Total,
                    lastLineForOutput,
                    operationTitleForOutput,
                    currentItem,
                    lastRendered);
                var heartbeatForOutput = throttle.IsHeartbeatDue();

                if (!force && !heartbeatForOutput && duplicateForOutput)
                {
                    return;
                }

                if (!throttle.ShouldRefresh(info.Processed, info.Total, force))
                {
                    return;
                }

                if (!progressCleared && options.ClearAtStart)
                {
                    options.UiOutput.Clear();
                    progressCleared = true;
                }

                WriteProgressLinesToUiOutput(
                    options.UiOutput,
                    operationTitleForOutput,
                    info.Processed,
                    info.Total,
                    currentItem,
                    lastLineForOutput);

                hasRendered = true;
                lastRendered = new RenderSnapshot(
                    info.Processed,
                    info.Total,
                    lastLineForOutput,
                    operationTitleForOutput,
                    currentItem);
                return;
            }

            if (Bare.Primitive.UI.UiConsole.IsOutputRedirected)
            {
                var redirectedTitle = string.IsNullOrWhiteSpace(currentOperationTitle)
                    ? string.Empty
                    : currentOperationTitle;
                var redirectedLastLine = options.ShowLastLine ? currentLastLine : null;
                WriteProgressLines(
                    redirectedTitle,
                    info.Processed,
                    info.Total,
                    currentItem,
                    redirectedLastLine,
                    progressTop,
                    ref prevCurrentLen,
                    ref prevCurrentWidth);
                return;
            }

            var operationTitle = string.IsNullOrWhiteSpace(currentOperationTitle)
                ? string.Empty
                : currentOperationTitle;
            var lastLine = options.ShowLastLine ? currentLastLine : null;
            var duplicate = IsDuplicate(info.Processed, info.Total, lastLine, operationTitle, currentItem, lastRendered);
            var heartbeat = throttle.IsHeartbeatDue();

            if (!force && !heartbeat && duplicate)
            {
                return;
            }

            if (!throttle.ShouldRefresh(info.Processed, info.Total, force))
            {
                return;
            }

            if (!progressCleared && options.ClearAtStart)
            {
                UiInteraction.Clear();
                progressCleared = true;
                progressAnchorInitialized = false;
            }

            if (!progressAnchorInitialized)
            {
                var anchor = EnsureProgressAnchor(progressTop, progressAnchorInitialized, Bare.Primitive.UI.UiConsole.CursorTop);
                progressTop = anchor.ProgressTop;
                progressAnchorInitialized = anchor.AnchorInitialized;
                EnsureLines(progressTop, ProgressLineCount);
            }

            WriteProgressLines(operationTitle, info.Processed, info.Total, currentItem, lastLine, progressTop, ref prevCurrentLen, ref prevCurrentWidth);

            hasRendered = true;
            lastRendered = new RenderSnapshot(
                info.Processed,
                info.Total,
                lastLine,
                operationTitle,
                currentItem);
        }

        Render(new ProgressInfo
        {
            Processed = 0,
            Total = -1,
            StartedAt = startedAt
        }, force: true);

        var progress = new InlineProgress(info =>
        {
            if (info is null)
            {
                return;
            }

            var force = false;
            if (!string.IsNullOrWhiteSpace(info.OperationTitle)
                && !string.Equals(info.OperationTitle, currentOperationTitle, StringComparison.Ordinal))
            {
                currentOperationTitle = info.OperationTitle;
                currentLastLine = null;
                prevCurrentLen = 0;
                prevCurrentWidth = 0;
                force = true;
            }

            if (!string.IsNullOrWhiteSpace(info.LastLine))
            {
                currentLastLine = info.LastLine;
            }

            if (!string.IsNullOrWhiteSpace(info.CurrentItem))
            {
                currentItem = info.CurrentItem;
            }

            Render(info, force);
        });

        try
        {
            if (renderMode == RenderMode.Progress)
            {
                cursorHidden = TrySetCursorVisible(false);
            }

            var result = await action(progress, linkedCts.Token).ConfigureAwait(false);
            if (options.ClearAtEnd && renderMode == RenderMode.Full && UiInteraction.IsRenderingEnabled)
            {
                ClearRenderTarget(options);
            }

            return result ?? new OperationResult();
        }
        catch (OperationCanceledException)
        {
            if (options.ClearAtEnd && renderMode == RenderMode.Full && UiInteraction.IsRenderingEnabled)
            {
                ClearRenderTarget(options);
            }

            return new OperationResult
            {
                SuccessOrWarningFlag = false,
                StatusLine = "Canceled"
            };
        }
        catch (Exception ex)
        {
            if (options.ClearAtEnd && renderMode == RenderMode.Full && UiInteraction.IsRenderingEnabled)
            {
                ClearRenderTarget(options);
            }

            return new OperationResult
            {
                SuccessOrWarningFlag = false,
                StatusLine = ex.Message
            };
        }
        finally
        {
            escapeCts.Cancel();
            stopCts.Cancel();
            try
            {
                await cancelWatcher.WaitAsync(TimeSpan.FromMilliseconds(100)).ConfigureAwait(false);
            }
            catch (TimeoutException)
            {
            }
            catch (OperationCanceledException)
            {
            }
            catch
            {
            }

            if (cursorHidden)
            {
                TrySetCursorVisible(true);
            }
        }
    }

    private static bool IsDuplicate(
        int processed,
        int total,
        string? lastLine,
        string operationTitle,
        string? currentItem,
        RenderSnapshot lastRendered)
    {
        return processed == lastRendered.Processed
            && total == lastRendered.Total
            && string.Equals(lastLine, lastRendered.LastLine, StringComparison.Ordinal)
            && string.Equals(operationTitle, lastRendered.OperationTitle, StringComparison.Ordinal)
            && string.Equals(currentItem, lastRendered.CurrentItem, StringComparison.Ordinal);
    }

    private readonly record struct RenderSnapshot(
        int Processed,
        int Total,
        string? LastLine,
        string OperationTitle,
        string? CurrentItem);

    private static TimeSpan GetElapsed(ProgressInfo info, DateTime startedAtFallback)
    {
        var started = info.StartedAt == default ? startedAtFallback : info.StartedAt;
        var elapsed = DateTime.UtcNow - started;
        return elapsed < TimeSpan.Zero ? TimeSpan.Zero : elapsed;
    }

    private static void EnsureLines(int top, int count)
    {
        if (Bare.Primitive.UI.UiConsole.IsOutputRedirected)
        {
            return;
        }

        try
        {
            var currentTop = Bare.Primitive.UI.UiConsole.CursorTop;
            if (currentTop <= top + count - 1)
            {
                for (var i = currentTop; i <= top + count - 1; i++)
                {
                    Bare.Primitive.UI.UiConsole.WriteLine();
                }
            }
        }
        catch
        {
            // If redirected, CursorTop may throw; ignore.
        }
    }

    internal static (int ProgressTop, bool AnchorInitialized) EnsureProgressAnchor(
        int progressTop,
        bool progressAnchorInitialized,
        int currentCursorTop)
    {
        if (progressAnchorInitialized)
        {
            return (progressTop, true);
        }

        return (currentCursorTop, true);
    }

    private static void WriteProgressLines(
        string operationTitle,
        int processed,
        int total,
        string? currentItem,
        string? lastLine,
        int progressTop,
        ref int prevCurrentLen,
        ref int prevCurrentWidth)
    {
        var statusLine = BuildStatusLine(operationTitle, processed, total, lastLine);
        var currentLine = string.IsNullOrWhiteSpace(currentItem)
            ? string.Empty
            : $"Current: {currentItem}";

        if (Bare.Primitive.UI.UiConsole.IsOutputRedirected)
        {
            Bare.Primitive.UI.UiConsole.WriteLine(statusLine);
            Bare.Primitive.UI.UiConsole.WriteLine(currentLine);
            return;
        }

        try
        {
            var currentLen = currentLine.Length;
            
            var width = Bare.Primitive.UI.UiConsole.BufferWidth;
            width = width > 1 ? width : 1;
            
            var prevWidth = prevCurrentWidth == 0 ? width : prevCurrentWidth;
            prevWidth = prevWidth > 1 ? prevWidth : 1;
            
            var linesOldPrev = (int)Math.Ceiling((double)prevCurrentLen / prevWidth);
            var linesOldCur = (int)Math.Ceiling((double)currentLen / width);
            var linesToClear = Math.Max(linesOldPrev, linesOldCur);
            linesToClear = Math.Max(1, linesToClear);
            
            Bare.Primitive.UI.UiConsole.SetCursorPosition(0, progressTop + 1);
            for (var i = 0; i < linesToClear; i++)
            {
                Bare.Primitive.UI.UiConsole.Write(new string(' ', width));
                if (i < linesToClear - 1)
                {
                    Bare.Primitive.UI.UiConsole.WriteLine();
                }
            }
            
            Bare.Primitive.UI.UiConsole.SetCursorPosition(0, progressTop);
            Bare.Primitive.UI.UiConsole.WriteLine(PadLine(statusLine));
            Bare.Primitive.UI.UiConsole.WriteLine(currentLine);
            
            var lastRow = progressTop + 1 + (int)Math.Ceiling((double)currentLen / width) - 1;
            Bare.Primitive.UI.UiConsole.SetCursorPosition(0, lastRow);
            
            prevCurrentLen = currentLen;
            prevCurrentWidth = width;
        }
        catch
        {
            Bare.Primitive.UI.UiConsole.WriteLine(statusLine);
            Bare.Primitive.UI.UiConsole.WriteLine(currentLine);
        }
    }

    private static string PadLine(string line)
    {
        try
        {
            if (Bare.Primitive.UI.UiConsole.IsOutputRedirected)
            {
                return line;
            }

            var width = Bare.Primitive.UI.UiConsole.BufferWidth;
            if (width > 1)
            {
                return line.PadRight(width - 1);
            }
        }
        catch
        {
        }

        return line;
    }

    private static string BuildStatusLine(string operationTitle, int processed, int total, string? lastLine)
    {
        var status = operationTitle;
        if (total > 0)
        {
            status = string.IsNullOrWhiteSpace(operationTitle)
                ? $"Progress: {processed}/{total}"
                : $"{operationTitle} ({processed}/{total})";
        }
        else
        {
            status = string.IsNullOrWhiteSpace(operationTitle)
                ? $"Processed: {processed}"
                : $"{operationTitle} (processed {processed})";
        }

        if (!string.IsNullOrWhiteSpace(lastLine))
        {
            status = $"{status} - {lastLine}";
        }

        return status;
    }

    private static void WriteProgressLinesToUiOutput(
        IUiOutput uiOutput,
        string operationTitle,
        int processed,
        int total,
        string? currentItem,
        string? lastLine)
    {
        if (uiOutput.Width <= 0 || uiOutput.Height <= 0)
        {
            return;
        }

        var statusLine = BuildStatusLine(operationTitle, processed, total, lastLine);
        var currentLine = string.IsNullOrWhiteSpace(currentItem)
            ? string.Empty
            : $"Current: {currentItem}";

        uiOutput.WriteAt(0, 0, UiText.Fit(statusLine, uiOutput.Width));

        if (uiOutput.Height > 1)
        {
            uiOutput.WriteAt(0, 1, UiText.Fit(currentLine, uiOutput.Width));
        }
    }

    private static bool TrySetCursorVisible(bool visible)
    {
        if (Bare.Primitive.UI.UiConsole.IsOutputRedirected)
        {
            return false;
        }

        if (!OperatingSystem.IsWindows())
        {
            return false;
        }

        return TrySetCursorVisibleWindows(visible);
    }

    [System.Runtime.Versioning.SupportedOSPlatform("windows")]
    private static bool TrySetCursorVisibleWindows(bool visible)
    {
        try
        {
            var original = Bare.Primitive.UI.UiConsole.CursorVisible;
            Bare.Primitive.UI.UiConsole.CursorVisible = visible;
            return original != visible;
        }
        catch
        {
            return false;
        }
    }

    private static void ClearRenderTarget(OperationRunnerOptions options)
    {
        if (options.UiOutput is not null)
        {
            options.UiOutput.Clear();
            return;
        }

        UiInteraction.Clear();
    }

    private sealed class InlineProgress : IProgress<ProgressInfo>
    {
        private readonly Action<ProgressInfo> _handler;

        public InlineProgress(Action<ProgressInfo> handler)
        {
            _handler = handler;
        }

        public void Report(ProgressInfo value)
        {
            _handler(value);
        }
    }
}
