using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using BareSync.Domain;

namespace BareSync.Infra;

internal sealed class BatchStorageWriter
{
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    public bool SaveAtomic(string appDataRoot, BatchV0 batch, out string? error)
    {
        error = null;
        if (string.IsNullOrWhiteSpace(appDataRoot))
        {
            error = "App data root is empty.";
            return false;
        }

        if (batch is null || string.IsNullOrWhiteSpace(batch.Id))
        {
            error = "Batch id is empty.";
            return false;
        }

        var batchRoot = BatchStorageLoader.ResolveBatchStoreRoot(appDataRoot);
        Directory.CreateDirectory(batchRoot);

        try
        {
            var lockPath = Path.Combine(batchRoot, $"{batch.Id}.json.lock");
            if (File.Exists(lockPath))
            {
                try
                {
                    File.Delete(lockPath);
                }
                catch
                {
                    // Best-effort cleanup; lock files should not block saves.
                }
            }

            var json = JsonSerializer.Serialize(ToPersisted(batch), SerializerOptions);
            var tempPath = Path.Combine(batchRoot, $"{batch.Id}.json.tmp.{Guid.NewGuid():N}");
            var finalPath = Path.Combine(batchRoot, $"{batch.Id}.json");

            using (var stream = new FileStream(
                       tempPath,
                       FileMode.CreateNew,
                       FileAccess.Write,
                       FileShare.None,
                       bufferSize: 4096,
                       FileOptions.SequentialScan))
            {
                var bytes = Encoding.UTF8.GetBytes(json);
                stream.Write(bytes, 0, bytes.Length);
                stream.Flush(flushToDisk: true);
            }

            ReplaceFile(tempPath, finalPath);
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            error = ex.Message;
            return false;
        }
    }

    private static object ToPersisted(BatchV0 batch)
    {
        return new
        {
            schemaVersion = batch.SchemaVersion,
            id = batch.Id,
            name = batch.Name,
            description = batch.Description,
            tags = batch.Tags,
            createdUtc = batch.CreatedUtc,
            updatedUtc = batch.UpdatedUtc,
            contextSnapshot = batch.ContextSnapshot,
            extensions = batch.Extensions,
            steps = batch.Steps.Select(step => new
            {
                stepId = step.StepId,
                operationType = step.OperationType,
                operationParams = new
                {
                    values = step.OperationParams?.Values ?? new System.Text.Json.Nodes.JsonObject(),
                    extensions = step.OperationParams?.Extensions
                },
                contextOverrides = step.ContextOverrides,
                extensions = step.Extensions
            })
        };
    }

    private static void ReplaceFile(string sourcePath, string destinationPath)
    {
        AtomicFileOperations.ReplaceFile(sourcePath, destinationPath);
    }
}
