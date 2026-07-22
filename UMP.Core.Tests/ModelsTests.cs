using UMP.Core.Models;

namespace UMP.Core.Tests;

public class ButtonActionTypesTests
{
    [Theory]
    [InlineData(ButtonActionType.StopAllScreens)]
    [InlineData(ButtonActionType.JumpToItemAllScreens)]
    public void IsPerZone_FauxPourLesActionsGlobales(ButtonActionType t)
        => Assert.False(ButtonActionTypes.IsPerZone(t));

    [Theory]
    [InlineData(ButtonActionType.Play)]
    [InlineData(ButtonActionType.PlaySequence)]
    [InlineData(ButtonActionType.Pause)]
    [InlineData(ButtonActionType.Stop)]
    [InlineData(ButtonActionType.ToggleSequence)]
    [InlineData(ButtonActionType.JumpToItem)]
    [InlineData(ButtonActionType.PlayMedia)]
    [InlineData(ButtonActionType.SwitchMedia)]
    [InlineData(ButtonActionType.TogglePip)]
    [InlineData(ButtonActionType.ShowPip)]
    [InlineData(ButtonActionType.HidePip)]
    public void IsPerZone_VraiPourLesActionsCiblees(ButtonActionType t)
        => Assert.True(ButtonActionTypes.IsPerZone(t));

    [Theory]
    [InlineData(ButtonActionType.TogglePip, true)]
    [InlineData(ButtonActionType.ShowPip, true)]
    [InlineData(ButtonActionType.HidePip, true)]
    [InlineData(ButtonActionType.Play, false)]
    [InlineData(ButtonActionType.StopAllScreens, false)]
    public void IsPip(ButtonActionType t, bool expected)
        => Assert.Equal(expected, ButtonActionTypes.IsPip(t));
}

public class SequenceItemTests : IDisposable
{
    private readonly string _tmpFile;

    public SequenceItemTests()
    {
        _tmpFile = Path.Combine(Path.GetTempPath(), "ump_item_" + Guid.NewGuid().ToString("N")[..8] + ".png");
        File.WriteAllText(_tmpFile, "x");
    }

    public void Dispose()
    {
        try { File.Delete(_tmpFile); } catch { }
    }

    [Fact]
    public void ItemImage_IsImageSlide_EtEffectivePath()
    {
        var item = new SequenceItem { ImageSlidePath = _tmpFile };

        Assert.True(item.IsImageSlide);
        Assert.False(item.IsVideo);
        Assert.Equal(_tmpFile, item.EffectivePath);
        Assert.True(item.HasValidMedia);
    }

    [Fact]
    public void ItemVideo_IsVideo_EtEffectivePath()
    {
        var item = new SequenceItem { MediaPath = _tmpFile };

        Assert.False(item.IsImageSlide);
        Assert.True(item.IsVideo);
        Assert.Equal(_tmpFile, item.EffectivePath);
        Assert.True(item.HasValidMedia);
    }

    [Fact]
    public void ImageSlidePrioritaireSurMediaPath()
    {
        // Si les deux sont definis, l'image gagne (regle IsImageSlide/EffectivePath)
        var item = new SequenceItem { MediaPath = "video.mp4", ImageSlidePath = _tmpFile };

        Assert.True(item.IsImageSlide);
        Assert.False(item.IsVideo);
        Assert.Equal(_tmpFile, item.EffectivePath);
    }

    [Fact]
    public void ItemVide_RienDeValide()
    {
        var item = new SequenceItem();

        Assert.False(item.IsImageSlide);
        Assert.False(item.IsVideo);
        Assert.Null(item.EffectivePath);
        Assert.False(item.HasValidMedia);
    }

    [Fact]
    public void FichierManquant_HasValidMediaFaux()
    {
        var item = new SequenceItem { MediaPath = Path.Combine(Path.GetTempPath(), "inexistant_xyz.mp4") };
        Assert.False(item.HasValidMedia);
    }
}
