using BareSync.App.BatchMode.Screens;
using BareSync.Domain;
using Xunit;

namespace BareSync.Tests;

public sealed class StepTypePickerScreenTests
{
    [Fact]
    public void OperationTypes_ContainsRefreshIndexesFull()
    {
        // Act
        var operationTypes = StepTypePickerScreen.GetOperationTypesForTests();

        // Assert
        var fullRefresh = operationTypes.First(t => t.Type == BatchOperationCatalog.OperationTypeRefreshIndexesFull);
        Assert.Equal("Refresh indexes (full)", fullRefresh.Label);
    }

    [Fact]
    public void OperationTypes_ContainsRefreshIndexesSmart()
    {
        // Act
        var operationTypes = StepTypePickerScreen.GetOperationTypesForTests();

        // Assert
        var smartRefresh = operationTypes.First(t => t.Type == BatchOperationCatalog.OperationTypeRefreshIndexesSmart);
        Assert.Equal("Refresh indexes (smart)", smartRefresh.Label);
    }

    [Fact]
    public void OperationTypes_FullBeforeSmart()
    {
        // Act
        var operationTypes = StepTypePickerScreen.GetOperationTypesForTests();
        var list = operationTypes.ToList();

        // Assert - FULL should be at index 0, SMART at index 1
        var fullIndex = list.FindIndex(t => t.Type == BatchOperationCatalog.OperationTypeRefreshIndexesFull);
        var smartIndex = list.FindIndex(t => t.Type == BatchOperationCatalog.OperationTypeRefreshIndexesSmart);
        
        Assert.NotEqual(-1, fullIndex);
        Assert.NotEqual(-1, smartIndex);
        Assert.True(fullIndex < smartIndex, "Full refresh should appear before smart refresh in the menu");
    }

    [Fact]
    public void OperationTypes_NoDuplicates()
    {
        // Act
        var operationTypes = StepTypePickerScreen.GetOperationTypesForTests();

        // Assert
        var duplicates = operationTypes
            .GroupBy(t => t.Type)
            .Where(g => g.Count() > 1)
            .Select(g => g.Key)
            .ToList();

        Assert.Empty(duplicates);
    }
}
