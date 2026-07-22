using System.Text.Json;
using UMP.Core.Models;
using UMP.Core.Services;

namespace UMP.Core.Tests;

/// <summary>
/// Round-trip sauvegarde/chargement : chemins relatifs portables.
/// Regles : sous le dossier projet -> relatif ; exterieur -> absolu ;
/// chargement -> resolution contre le dossier du .ump.json ;
/// anciens projets absolus -> inchanges.
/// </summary>
public class ProjectServiceTests : IDisposable
{
    private readonly string _projDir;
    private readonly string _externalDir;
    private readonly string _inside;
    private readonly string _insideSrt;
    private readonly string _outside;

    public ProjectServiceTests()
    {
        UMP.Core.Log.AppName = "tests";
        _projDir = Path.Combine(Path.GetTempPath(), "ump_svc_" + Guid.NewGuid().ToString("N")[..8]);
        _externalDir = Path.Combine(Path.GetTempPath(), "ump_ext_" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(Path.Combine(_projDir, "media"));
        Directory.CreateDirectory(_externalDir);
        _inside = Path.Combine(_projDir, "media", "video.mp4");
        _insideSrt = Path.Combine(_projDir, "media", "subs.srt");
        _outside = Path.Combine(_externalDir, "extern.mp4");
        File.WriteAllText(_inside, "x");
        File.WriteAllText(_insideSrt, "x");
        File.WriteAllText(_outside, "x");
    }

    public void Dispose()
    {
        try { Directory.Delete(_projDir, true); } catch { }
        try { Directory.Delete(_projDir + "_moved", true); } catch { }
        try { Directory.Delete(_externalDir, true); } catch { }
    }

    private ProjectService BuildService() => new()
    {
        CurrentProject = new Project
        {
            Zones =
            {
                new Zone
                {
                    MediaFilePath = _inside,
                    Sequence = new Sequence
                    {
                        Items =
                        {
                            new SequenceItem
                            {
                                MediaPath = _inside,
                                Subtitles = { new SubtitleConfig { FilePath = _insideSrt } },
                                Buttons =
                                {
                                    new ButtonConfig
                                    {
                                        Actions = { new ButtonAction { Type = ButtonActionType.PlayMedia, MediaPath = _outside } }
                                    }
                                }
                            }
                        }
                    }
                }
            }
        }
    };

    [Fact]
    public void Save_EcritLesCheminsInternesEnRelatif()
    {
        var svc = BuildService();
        var file = Path.Combine(_projDir, "test.ump.json");
        svc.Save(file);

        var raw = File.ReadAllText(file);
        Assert.Contains(@"media\\video.mp4", raw);
        Assert.DoesNotContain(_projDir.Replace(@"\", @"\\"), raw);
    }

    [Fact]
    public void Save_LaisseLesCheminsExternesAbsolus()
    {
        var svc = BuildService();
        var file = Path.Combine(_projDir, "test.ump.json");
        svc.Save(file);

        var raw = File.ReadAllText(file);
        Assert.Contains(_externalDir.Replace(@"\", @"\\"), raw);
    }

    [Fact]
    public void Save_NEcritPasLesChampsCalcules()
    {
        var svc = BuildService();
        var file = Path.Combine(_projDir, "test.ump.json");
        svc.Save(file);

        var raw = File.ReadAllText(file);
        Assert.DoesNotContain("HasValidMedia", raw);
        Assert.DoesNotContain("EffectivePath", raw);
        Assert.DoesNotContain("IsImageSlide", raw);
        Assert.DoesNotContain("IsVideo", raw);
    }

    [Fact]
    public void Save_NeModifiePasLeProjetEnMemoire()
    {
        var svc = BuildService();
        svc.Save(Path.Combine(_projDir, "test.ump.json"));

        // L'editeur continue de travailler avec des chemins absolus
        Assert.Equal(_inside, svc.CurrentProject.Zones[0].MediaFilePath);
        Assert.Equal(_insideSrt, svc.CurrentProject.Zones[0].Sequence!.Items[0].Subtitles[0].FilePath);
    }

    [Fact]
    public void Load_ResoutLesRelatifsEnAbsolus()
    {
        var svc = BuildService();
        var file = Path.Combine(_projDir, "test.ump.json");
        svc.Save(file);

        var svc2 = new ProjectService();
        svc2.Load(file);
        var zone = svc2.CurrentProject.Zones[0];

        Assert.Equal(_inside, zone.MediaFilePath);
        Assert.Equal(_inside, zone.Sequence!.Items[0].MediaPath);
        Assert.Equal(_insideSrt, zone.Sequence!.Items[0].Subtitles[0].FilePath);
        Assert.Equal(_outside, zone.Sequence!.Items[0].Buttons[0].Actions[0].MediaPath);
    }

    [Fact]
    public void Load_DossierProjetDeplace_LesCheminsSuivent()
    {
        var svc = BuildService();
        svc.Save(Path.Combine(_projDir, "test.ump.json"));

        var movedDir = _projDir + "_moved";
        Directory.Move(_projDir, movedDir);

        var svc2 = new ProjectService();
        svc2.Load(Path.Combine(movedDir, "test.ump.json"));
        var expected = Path.Combine(movedDir, "media", "video.mp4");

        Assert.Equal(expected, svc2.CurrentProject.Zones[0].MediaFilePath);
        Assert.True(File.Exists(svc2.CurrentProject.Zones[0].MediaFilePath));
    }

    [Fact]
    public void Load_AncienProjetToutAbsolu_Inchange()
    {
        var legacy = Path.Combine(_projDir, "legacy.ump.json");
        File.WriteAllText(legacy, JsonSerializer.Serialize(new Project
        {
            Zones = { new Zone { MediaFilePath = _outside } }
        }));

        var svc = new ProjectService();
        svc.Load(legacy);

        Assert.Equal(_outside, svc.CurrentProject.Zones[0].MediaFilePath);
    }

    [Fact]
    public void Load_FichierInexistant_ProjetNeuf()
    {
        var svc = new ProjectService();
        svc.Load(Path.Combine(_projDir, "inexistant.ump.json"));

        Assert.NotNull(svc.CurrentProject);
        Assert.Empty(svc.CurrentProject.Zones);
    }

    [Fact]
    public void SaveLoad_RoundTripComplet_StructureIntacte()
    {
        var svc = BuildService();
        var file = Path.Combine(_projDir, "test.ump.json");
        svc.Save(file);

        var svc2 = new ProjectService();
        svc2.Load(file);

        Assert.Single(svc2.CurrentProject.Zones);
        var item = svc2.CurrentProject.Zones[0].Sequence!.Items[0];
        Assert.Single(item.Subtitles);
        Assert.Single(item.Buttons);
        Assert.Equal(ButtonActionType.PlayMedia, item.Buttons[0].Actions[0].Type);
    }
}
