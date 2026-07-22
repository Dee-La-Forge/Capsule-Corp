using UMP.Core.Services;

namespace UMP.Core.Tests;

public class SrtParserTests : IDisposable
{
    private readonly string _dir;

    public SrtParserTests()
    {
        UMP.Core.Log.AppName = "tests";
        _dir = Path.Combine(Path.GetTempPath(), "ump_srt_" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(_dir);
    }

    public void Dispose()
    {
        try { Directory.Delete(_dir, true); } catch { }
    }

    private string WriteSrt(string content)
    {
        var p = Path.Combine(_dir, Guid.NewGuid().ToString("N")[..8] + ".srt");
        File.WriteAllText(p, content, System.Text.Encoding.UTF8);
        return p;
    }

    [Fact]
    public void Parse_EntreeSimple()
    {
        var p = WriteSrt("1\n00:00:01,000 --> 00:00:03,500\nBonjour\n");
        var entries = SrtParser.Parse(p);

        Assert.Single(entries);
        Assert.Equal("Bonjour", entries[0].Text);
        Assert.Equal(1000, entries[0].InMs);
        Assert.Equal(3500, entries[0].OutMs);
    }

    [Fact]
    public void Parse_TexteMultiligne()
    {
        var p = WriteSrt("1\n00:00:01,000 --> 00:00:02,000\nLigne 1\nLigne 2\n");
        var entries = SrtParser.Parse(p);

        Assert.Single(entries);
        Assert.Equal("Ligne 1\nLigne 2", entries[0].Text);
    }

    [Fact]
    public void Parse_SupprimeLesBalisesHtml()
    {
        var p = WriteSrt("1\n00:00:01,000 --> 00:00:02,000\n<i>Italique</i> et <b>gras</b>\n");
        var entries = SrtParser.Parse(p);

        Assert.Equal("Italique et gras", entries[0].Text);
    }

    [Fact]
    public void Parse_IgnoreLesEntreesVides()
    {
        // Cas reel (fichiers Seppia) : premiere entree sans texte
        var p = WriteSrt("1\n00:00:00,000 --> 00:00:00,200\n\n2\n00:00:01,000 --> 00:00:02,000\nTexte\n");
        var entries = SrtParser.Parse(p);

        Assert.Single(entries);
        Assert.Equal("Texte", entries[0].Text);
    }

    [Fact]
    public void Parse_PlusieursEntrees()
    {
        var p = WriteSrt(
            "1\n00:00:01,000 --> 00:00:02,000\nUn\n\n" +
            "2\n00:00:03,000 --> 00:00:04,000\nDeux\n\n" +
            "3\n00:01:00,000 --> 00:01:05,000\nTrois\n");
        var entries = SrtParser.Parse(p);

        Assert.Equal(3, entries.Count);
        Assert.Equal(60000, entries[2].InMs);
    }

    [Fact]
    public void Parse_TimestampsAvecPoint()
    {
        // Certains exports utilisent le point au lieu de la virgule
        var p = WriteSrt("1\n00:00:01.500 --> 00:00:02.750\nTexte\n");
        var entries = SrtParser.Parse(p);

        Assert.Equal(1500, entries[0].InMs);
        Assert.Equal(2750, entries[0].OutMs);
    }

    [Fact]
    public void Parse_AvecBomUtf8()
    {
        var p = Path.Combine(_dir, "bom.srt");
        File.WriteAllText(p, "1\n00:00:01,000 --> 00:00:02,000\nAccentué èàü\n",
            new System.Text.UTF8Encoding(true));
        var entries = SrtParser.Parse(p);

        Assert.Single(entries);
        Assert.Equal("Accentué èàü", entries[0].Text);
    }

    [Fact]
    public void Parse_FichierInexistant_RetourneListeVide()
    {
        var entries = SrtParser.Parse(Path.Combine(_dir, "inexistant.srt"));
        Assert.Empty(entries);
    }

    [Fact]
    public void Parse_FichierVide_RetourneListeVide()
    {
        var entries = SrtParser.Parse(WriteSrt(""));
        Assert.Empty(entries);
    }

    [Fact]
    public void Parse_TimestampIllisible_DonneZero()
    {
        var p = WriteSrt("1\nn'importe --> quoi\nTexte\n");
        var entries = SrtParser.Parse(p);

        // La ligne contient "-->" donc est traitee ; timestamps illisibles -> 0
        Assert.Single(entries);
        Assert.Equal(0, entries[0].InMs);
        Assert.Equal(0, entries[0].OutMs);
    }
}
