using BareSync.Domain;
using BareSync.Infra;
using BareSync.UI;
using System.Text.Json.Nodes;

namespace BareSync.App.BatchMode.Screens;

/// <summary>
/// S2.1 Batch Home screen - Entry point for batch mode with create option.
/// </summary>
internal sealed class BatchHomeScreen : IBatchScreen
{
    public BatchStorageDescriptor? Show(BatchScreenContext context)
    {
        while (true)
        {
            var selection = PromptBatchHome();
            switch (selection)
            {
                case 0:
                    return null; // Back to main menu
                case 1:
                    // List batches - delegate to list screen
                    return null; // Will be handled by controller
                case 2:
                    var createdId = PromptCreateBatch(context.AppDataRoot);
                    if (!string.IsNullOrWhiteSpace(createdId))
                    {
                        var created = context.Loader.LoadAll(context.AppDataRoot)
                            .FirstOrDefault(entry => string.Equals(entry.Id, createdId, StringComparison.OrdinalIgnoreCase));
                        if (created is not null)
                        {
                            return created;
                        }
                    }
                    break;
            }
        }
    }

    private static int PromptBatchHome()
    {
        UiInteraction.Clear();
        Bare.Primitive.UI.UiConsole.WriteLine("** Batch mode **");
        Bare.Primitive.UI.UiConsole.WriteLine();
        Bare.Primitive.UI.UiConsole.WriteLine("** Menu **");
        Bare.Primitive.UI.UiConsole.WriteLine();
        Bare.Primitive.UI.UiConsole.WriteLine("1. List batches");
        Bare.Primitive.UI.UiConsole.WriteLine("2. Create new batch");
        Bare.Primitive.UI.UiConsole.WriteLine("0. Back");
        Bare.Primitive.UI.UiConsole.WriteLine();
        Bare.Primitive.UI.UiConsole.Write("Select an option: ");
        return UiInteraction.ReadMenuDigit(0, 2);
    }

    private static string? PromptCreateBatch(string appDataRoot)
    {
        UiInteraction.Clear();
        Bare.Primitive.UI.UiConsole.Write("Enter batch name (empty or ESC to cancel):");
        Bare.Primitive.UI.UiConsole.WriteLine();
        var name = UiInteraction.ReadLineWithEscape();
        if (string.IsNullOrWhiteSpace(name))
        {
            return null;
        }

        var trimmedName = name.Trim();
        if (string.IsNullOrWhiteSpace(trimmedName))
        {
            return null;
        }

        var id = Guid.NewGuid().ToString("D");
        var timestamp = DateTimeOffset.UtcNow.ToString("yyyy-MM-ddTHH:mm:ss'Z'", System.Globalization.CultureInfo.InvariantCulture);
        var batch = new BatchV0
        {
            SchemaVersion = 0,
            Id = id,
            Name = trimmedName,
            CreatedUtc = timestamp,
            UpdatedUtc = timestamp,
            ContextSnapshot = new JsonObject(),
            Steps = new List<BatchStepV0>()
        };

        var writer = new BatchStorageWriter();
        if (!writer.SaveAtomic(appDataRoot, batch, out var error))
        {
            Bare.Primitive.UI.UiConsole.WriteLine($"Failed to save batch: {error}");
            return null;
        }
        return id;
    }
}