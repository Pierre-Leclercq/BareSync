using BareSync.Domain;
using BareSync.UI;

namespace BareSync.App.BatchMode.Screens;

/// <summary>
/// S2.7 - Step Type Picker (Add step)
/// Allows selecting the operation type for a new step.
/// </summary>
internal sealed class StepTypePickerScreen
{
    private static readonly (string Type, string Label)[] OperationTypes =
    [
        (BatchOperationCatalog.OperationTypeRefreshIndexesFull, "Refresh indexes (full)"),
        (BatchOperationCatalog.OperationTypeRefreshIndexesSmart, "Refresh indexes (smart)"),
        (BatchOperationCatalog.OperationTypeOneWaySyncDryRun, "One-way sync (dry run)"),
        (BatchOperationCatalog.OperationTypeOneWaySyncApply, "One-way sync (apply)"),
        (BatchOperationCatalog.OperationTypeCreateEncryptedFolder, "Create encrypted folder"),
        (BatchOperationCatalog.OperationTypeRefreshEncryptedFolder, "Refresh encrypted folder"),
        (BatchOperationCatalog.OperationTypeRestoreEncryptedFiles, "Restore encrypted files")
    ];

    /// <summary>
    /// Shows the step type picker and returns the selected operation type.
    /// Returns null if user cancels (option 0).
    /// </summary>
    /// <param name="inputProvider">Optional input provider for testing. If null, uses the default menu digit reader.</param>
    public string? Show(Func<int, int, int>? inputProvider = null)
    {
        while (true)
        {
            UiInteraction.Clear();
            Bare.Primitive.UI.UiConsole.WriteLine("** BareSync **");
            Bare.Primitive.UI.UiConsole.WriteLine();
            Bare.Primitive.UI.UiConsole.WriteLine("** Step / Select operation **");
            Bare.Primitive.UI.UiConsole.WriteLine();
            Bare.Primitive.UI.UiConsole.WriteLine("** Menu **");
            Bare.Primitive.UI.UiConsole.WriteLine();

            for (var i = 0; i < OperationTypes.Length; i++)
            {
                Bare.Primitive.UI.UiConsole.WriteLine($"{i + 1}. {OperationTypes[i].Label}");
            }

            Bare.Primitive.UI.UiConsole.WriteLine("0. Back");
            Bare.Primitive.UI.UiConsole.WriteLine();
            Bare.Primitive.UI.UiConsole.Write("Select an option: ");

            var selection = inputProvider?.Invoke(0, OperationTypes.Length)
                ?? UiInteraction.ReadMenuDigit(0, OperationTypes.Length);
            
            if (selection == 0)
            {
                return null; // Cancelled
            }

            if (selection >= 1 && selection <= OperationTypes.Length)
            {
                return OperationTypes[selection - 1].Type;
            }
        }
    }

    /// <summary>
    /// Shows the step type picker with predefined selection for testing.
    /// </summary>
    public string? Show(int selection)
    {
        if (selection == 0)
        {
            return null; // Cancelled
        }

        if (selection >= 1 && selection <= OperationTypes.Length)
        {
            return OperationTypes[selection - 1].Type;
        }

        return null;
    }

    /// <summary>
    /// Internal accessor for testing: returns the operation types list.
    /// </summary>
    internal static IReadOnlyList<(string Type, string Label)> GetOperationTypesForTests() => OperationTypes;
}
