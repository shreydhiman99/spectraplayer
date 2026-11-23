using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace SpectraPlayer.Core.Bookmarks;

/// <summary>
/// Reads and writes PotPlayer bookmark files (.pbf).
/// <para>
/// Layout (community-documented, not an official spec): UTF-16 text; first line <c>[Bookmark]</c>; then one line per
/// bookmark, <c>{index}={milliseconds}*{name}*{opaque}</c>, index starting at 0; then a closing <c>{count}=</c> line.
/// </para>
/// </summary>
public static partial class PbfFile
{
    public const string Header = "[Bookmark]";

    // Names cannot contain '*' (it is the field separator), so the name group excludes it.
    [GeneratedRegex(@"^(?<index>\d+)=(?<ms>\d+)\*(?<name>[^*]*)\*(?<rest>.*)$")]
    private static partial Regex LineRegex();

    public static IReadOnlyList<Bookmark> Read(string path) => Parse(Decode(File.ReadAllBytes(path)));

    /// <summary>Parses .pbf text. Throws <see cref="FormatException"/> when the header is missing.</summary>
    public static IReadOnlyList<Bookmark> Parse(string text)
    {
        var lines = text.Split(["\r\n", "\n", "\r"], StringSplitOptions.None);
        var i = 0;
        while (i < lines.Length && string.IsNullOrWhiteSpace(lines[i])) i++;
        if (i >= lines.Length || lines[i].Trim().TrimStart('﻿') != Header)
            throw new FormatException("Not a PotPlayer bookmark file: missing [Bookmark] header.");

        var result = new List<Bookmark>();
        for (i++; i < lines.Length; i++)
        {
            var m = LineRegex().Match(lines[i].TrimEnd());
            if (!m.Success) continue; // the "{count}=" sentinel, blank lines, anything unrecognised
            if (!long.TryParse(m.Groups["ms"].Value, NumberStyles.None, CultureInfo.InvariantCulture, out var ms)
                || ms > TimeSpan.MaxValue.TotalMilliseconds) continue;
            result.Add(new Bookmark(TimeSpan.FromMilliseconds(ms), m.Groups["name"].Value, m.Groups["rest"].Value));
        }

        // OrderBy is stable, so bookmarks at the same position keep their file order.
        return result.OrderBy(b => b.Position).ToList();
    }

    public static string Serialize(IEnumerable<Bookmark> bookmarks)
    {
        var sb = new StringBuilder();
        sb.Append(Header).Append("\r\n");
        var index = 0;
        foreach (var b in bookmarks)
        {
            var ms = (long)Math.Round(b.Position.TotalMilliseconds);
            sb.Append(index++).Append('=').Append(ms).Append('*')
              .Append(SanitizeName(b.Name)).Append('*')
              .Append(SanitizeName(b.ThumbnailRaw)).Append("\r\n");
        }
        sb.Append(index).Append("=\r\n\r\n");
        return sb.ToString();
    }

    /// <summary>Writes UTF-16 LE with BOM (what PotPlayer writes), via a temp file so a failure never truncates the original.</summary>
    public static void Write(string path, IEnumerable<Bookmark> bookmarks)
    {
        var tmp = path + ".tmp";
        File.WriteAllText(tmp, Serialize(bookmarks), new UnicodeEncoding(bigEndian: false, byteOrderMark: true));
        File.Move(tmp, path, overwrite: true);
    }

    internal static string Decode(byte[] bytes)
    {
        if (bytes.Length >= 2 && bytes[0] == 0xFF && bytes[1] == 0xFE) return Encoding.Unicode.GetString(bytes, 2, bytes.Length - 2);
        if (bytes.Length >= 2 && bytes[0] == 0xFE && bytes[1] == 0xFF) return Encoding.BigEndianUnicode.GetString(bytes, 2, bytes.Length - 2);
        if (bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF) return Encoding.UTF8.GetString(bytes, 3, bytes.Length - 3);
        // No BOM: ASCII-range text in UTF-16 LE has a zero second byte.
        if (bytes.Length >= 2 && bytes[0] != 0 && bytes[1] == 0) return Encoding.Unicode.GetString(bytes);
        return Encoding.UTF8.GetString(bytes);
    }

    private static string SanitizeName(string value) =>
        value.Replace('*', '＊') // fullwidth asterisk keeps the field layout intact
             .Replace('\r', ' ').Replace('\n', ' ');
}
