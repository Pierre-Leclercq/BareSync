namespace BareSync.UI;

internal interface IScreen
{
    ScreenModel Build();

    bool ShouldClearBeforeRender => false;
}
