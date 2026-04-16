using System.Text.Json.Nodes;
using BareSync.Domain;
using BareSync.Infra;
using BareSync.UI;

namespace BareSync.App.BatchMode.Screens;

/// <summary>
/// Purges index files and related artifacts for a selected batch.
/// </summary>
internal sealed class PurgeBatchIndexesScreen
{
    public MenuStatus ShowAndPurge(BatchStorageLoader loader, string appDataRoot)
    {
        var selectionScreen = new BatchExecuteSelectionScreen(loader, appDataRoot);
        var descriptor = selectionScreen.Show();
        if (descriptor is null)
        {
            return new MenuStatus { StatusLine = "Purge canceled" };
        }

        var batch = BatchUiHelpers.LoadBatchV0(descriptor.Path);
        if (batch is null)
        {
            return new MenuStatus { StatusLine = "Purge failed: invalid batch" };
        }

        var indexPaths = CollectIndexPaths(batch);
        if (indexPaths.Count == 0)
        {
            return new MenuStatus { StatusLine = "Purge skipped: no index path found" };
        }

        var touchedPathCount = 0;
        foreach (var indexPath in indexPaths)
        {
            var hadArtifactsBefore = HasAnyArtifact(indexPath);
            IndexRefreshService.DeleteIndexArtifacts(indexPath);
            var hasArtifactsAfter = HasAnyArtifact(indexPath);
            if (hadArtifactsBefore && !hasArtifactsAfter)
            {
                touchedPathCount++;
            }
        }

        UiInteraction.Clear();
        Bare.Primitive.UI.UiConsole.WriteLine("** Batch / Purge indexes **");
        Bare.Primitive.UI.UiConsole.WriteLine();
        Bare.Primitive.UI.UiConsole.WriteLine($"Batch: {batch.Name} [{BatchUiHelpers.GetShortId(batch.Id)}]");
        Bare.Primitive.UI.UiConsole.WriteLine($"Index paths: {indexPaths.Count}");
        Bare.Primitive.UI.UiConsole.WriteLine($"Purged paths: {touchedPathCount}");
        Bare.Primitive.UI.UiConsole.WriteLine();

        return new MenuStatus
        {
            StatusLine = $"Purge indexes done ({touchedPathCount}/{indexPaths.Count})"
        };
    }

    private static List<string> CollectIndexPaths(BatchV0 batch)
    {
        var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        AddIndexPathFromJson(set, batch.ContextSnapshot, BatchContextFields.SourceIndexCsvPath);
        AddIndexPathFromJson(set, batch.ContextSnapshot, BatchContextFields.DestIndexCsvPath);

        foreach (var step in batch.Steps)
        {
            AddIndexPathFromJson(set, step.ContextOverrides, BatchContextFields.SourceIndexCsvPath);
            AddIndexPathFromJson(set, step.ContextOverrides, BatchContextFields.DestIndexCsvPath);
        }

        return set.ToList();
    }

    private static void AddIndexPathFromJson(HashSet<string> paths, JsonObject? source, string field)
    {
        if (source is null)
        {
            return;
        }

        if (!source.TryGetPropertyValue(field, out var value) || value is null)
        {
            return;
        }

        if (value is not JsonValue jsonValue || !jsonValue.TryGetValue<string>(out var text))
        {
            return;
        }

        if (string.IsNullOrWhiteSpace(text))
        {
            return;
        }

        paths.Add(text.Trim());
    }

    private static bool HasAnyArtifact(string indexPath)
    {
        return File.Exists(indexPath)
            || File.Exists($"{indexPath}.work")
            || File.Exists($"{indexPath}.checkpoint");
    }
}