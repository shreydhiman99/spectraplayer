using System.Text;
using SpectraPlayer.Core.Bookmarks;
using Xunit;

namespace SpectraPlayer.Core.Tests;

public class PbfFileTests
{
    // Shape taken from the community description of PotPlayer's format: header, index=ms*name*opaque, "N=" sentinel.
    private const string Sample =
        "[Bookmark]\r\n" +
        "0=39039*Intro*AAAA\r\n" +
        "1=2331000*Big topic*\r\n" +
        "2=\r\n\r\n";

    [Fact]
    public void Parse_reads_positions_names_and_opaque_field()
    {
        var list = PbfFile.Parse(Sample);

        Assert.Equal(2, list.Count);
        Assert.Equal(TimeSpan.FromMilliseconds(39039), list[0].Position);
        Assert.Equal("Intro", list[0].Name);
        Assert.Equal("AAAA", list[0].ThumbnailRaw);
        Assert.Equal(TimeSpan.FromMilliseconds(2331000), list[1].Position); // 36m51s
        Assert.Equal("Big topic", list[1].Name);
    }

    [Fact]
    public void Parse_ignores_sentinel_and_blank_lines_and_accepts_lf_endings()
    {
        var list = PbfFile.Parse("[Bookmark]\n0=1000*A*\n\n1=\n");
        Assert.Single(list);
    }

    [Fact]
    public void Parse_sorts_by_position()
    {
        var list = PbfFile.Parse("[Bookmark]\r\n0=9000*Late*\r\n1=1000*Early*\r\n2=\r\n");
        Assert.Equal(["Early", "Late"], list.Select(b => b.Name));
    }

    [Fact]
    public void Parse_rejects_text_without_header()
    {
        Assert.Throws<FormatException>(() => PbfFile.Parse("0=1000*A*\r\n"));
        Assert.Throws<FormatException>(() => PbfFile.Parse(""));
    }

    [Fact]
    public void Parse_skips_lines_it_cannot_understand()
    {
        var list = PbfFile.Parse("[Bookmark]\r\n0=1000*Good*\r\nnonsense\r\n1=abc*Bad*\r\n2=\r\n");
        Assert.Single(list);
    }

    [Fact]
    public void Empty_name_falls_back_to_timestamp()
    {
        var b = PbfFile.Parse("[Bookmark]\r\n0=65000**\r\n1=\r\n").Single();
        Assert.Equal("01:05", b.Name);
    }

    [Fact]
    public void Serialize_matches_expected_layout()
    {
        var text = PbfFile.Serialize([new Bookmark(TimeSpan.FromSeconds(12.5), "Hello", "XYZ")]);
        Assert.Equal("[Bookmark]\r\n0=12500*Hello*XYZ\r\n1=\r\n\r\n", text);
    }

    [Fact]
    public void Serialize_empty_list_still_has_header_and_sentinel()
    {
        Assert.Equal("[Bookmark]\r\n0=\r\n\r\n", PbfFile.Serialize([]));
    }

    [Fact]
    public void Serialize_protects_field_separator_and_newlines_in_names()
    {
        var text = PbfFile.Serialize([new Bookmark(TimeSpan.FromSeconds(1), "a*b\r\nc")]);
        var back = PbfFile.Parse(text).Single();
        Assert.Equal("a＊b  c", back.Name);
    }

    [Fact]
    public void Round_trip_preserves_everything_including_opaque_field()
    {
        var original = PbfFile.Parse(Sample);
        var again = PbfFile.Parse(PbfFile.Serialize(original));

        Assert.Equal(original.Select(b => (b.Position, b.Name, b.ThumbnailRaw)),
                     again.Select(b => (b.Position, b.Name, b.ThumbnailRaw)));
    }

    [Fact]
    public void Write_produces_utf16_le_with_bom_and_Read_loads_it()
    {
        var path = Path.Combine(Path.GetTempPath(), $"spectra-{Guid.NewGuid():N}.pbf");
        try
        {
            PbfFile.Write(path, [new Bookmark(TimeSpan.FromMinutes(3), "Café ☕")]);

            var bytes = File.ReadAllBytes(path);
            Assert.Equal(new byte[] { 0xFF, 0xFE }, bytes[..2]);
            Assert.False(File.Exists(path + ".tmp"));

            var b = PbfFile.Read(path).Single();
            Assert.Equal(TimeSpan.FromMinutes(3), b.Position);
            Assert.Equal("Café ☕", b.Name);
        }
        finally { File.Delete(path); }
    }

    [Theory]
    [InlineData("utf16le-bom")]
    [InlineData("utf16le-nobom")]
    [InlineData("utf16be-bom")]
    [InlineData("utf8-bom")]
    [InlineData("utf8")]
    public void Read_accepts_common_encodings(string kind)
    {
        byte[] bytes = kind switch
        {
            "utf16le-bom" => new UnicodeEncoding(false, true).GetPreamble().Concat(Encoding.Unicode.GetBytes(Sample)).ToArray(),
            "utf16le-nobom" => Encoding.Unicode.GetBytes(Sample),
            "utf16be-bom" => new UnicodeEncoding(true, true).GetPreamble().Concat(Encoding.BigEndianUnicode.GetBytes(Sample)).ToArray(),
            "utf8-bom" => new UTF8Encoding(true).GetPreamble().Concat(Encoding.UTF8.GetBytes(Sample)).ToArray(),
            _ => Encoding.UTF8.GetBytes(Sample),
        };

        var list = PbfFile.Parse(PbfFile.Decode(bytes));
        Assert.Equal(2, list.Count);
        Assert.Equal("Intro", list[0].Name);
    }
}
