using BareSync.App.BatchMode.Screens;
using BareSync.Domain;

namespace BareSync.Tests;

/// <summary>
/// Helper to dynamically resolve menu digits for operation types.
/// Makes tests resilient to menu ordering changes.
/// </summary>
internal static class TestMenuDigits
{
    /// <summary>
    /// Gets the menu digit (1-based) for selecting an operation type in StepTypePickerScreen.
    /// Throws if the operation type is not found.
    /// </summary>
    public static int DigitForOperation(string opType)
    {
        var operationTypes = StepTypePickerScreen.GetOperationTypesForTests();
        
        for (int i = 0; i < operationTypes.Count; i++)
        {
            if (operationTypes[i].Type == opType)
            {
                return i + 1; // Menu digits are 1-based
            }
        }
        
        throw new ArgumentException(
            $"Operation type '{opType}' not found in StepTypePickerScreen. " +
            $"Available: {string.Join(", ", operationTypes.Select(t => t.Type))}");
    }
    
    /// <summary>
    /// Gets the menu digit as a string for use in stdin sequences.
    /// </summary>
    public static string DigitForOperationString(string opType)
    {
        return DigitForOperation(opType).ToString();
    }
}