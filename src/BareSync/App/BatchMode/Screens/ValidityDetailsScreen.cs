using BareSync.Domain;
using BareSync.UI;

namespace BareSync.App.BatchMode.Screens;

/// <summary>
/// S2.16 Validity Details - Show batch validation status and reason.
/// </summary>
internal sealed class ValidityDetailsScreen
{
    public void Show(BatchStorageDescriptor descriptor)
    {
        UiInteraction.Clear();
        Bare.Primitive.UI.UiConsole.WriteLine("** Batch / Validity details **");
        Bare.Primitive.UI.UiConsole.WriteLine();
        Bare.Primitive.UI.UiConsole.WriteLine($"Status: {descriptor.Status}");
        Bare.Primitive.UI.UiConsole.WriteLine($"Reason: {descriptor.Reason}");
        Bare.Primitive.UI.UiConsole.WriteLine();
        Bare.Primitive.UI.UiConsole.WriteLine("** Menu **");
        Bare.Primitive.UI.UiConsole.WriteLine();
        Bare.Primitive.UI.UiConsole.WriteLine("0. Back");
        Bare.Primitive.UI.UiConsole.WriteLine();
        Bare.Primitive.UI.UiConsole.Write("Select an option: ");
        _ = UiInteraction.ReadMenuDigit(0, 0);
    }
}