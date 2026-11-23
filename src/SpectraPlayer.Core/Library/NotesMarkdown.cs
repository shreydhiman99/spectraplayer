using System.Text;

namespace SpectraPlayer.Core.Library;

/// <summary>One video's notes inside a larger export.</summary>
/// <param name="Subtitle">Optional line under the heading, such as the subfolder the video sits in.</param>
public sealed record NotesSection(string Title, string? Subtitle, IReadOnlyList<Note> Notes);

/// <summary>
/// Renders notes as Markdown that pastes cleanly into Obsidian, Notion or a plain text editor:
/// <c>- **[12:34]** text</c>, with continuation lines indented so multi-line notes stay in one bullet.
/// </summary>
public static class NotesMarkdown
{
    public static string ForVideo(string videoTitle, IReadOnlyList<Note> notes)
    {
        var sb = new StringBuilder();
        sb.Append("# ").Append(videoTitle.Trim()).Append("\n\n");
        if (notes.Count == 0) sb.Append("_No notes._\n");
        else AppendBullets(sb, notes);
        return sb.ToString();
    }

    public static string ForTab(string tabTitle, IEnumerable<NotesSection> sections)
    {
        var list = sections.Where(s => s.Notes.Count > 0).ToList();
        var total = list.Sum(s => s.Notes.Count);

        var sb = new StringBuilder();
        sb.Append("# ").Append(tabTitle.Trim()).Append(" — notes\n\n");
        if (list.Count == 0)
        {
            sb.Append("_No notes._\n");
            return sb.ToString();
        }

        sb.Append('_').Append(total).Append(total == 1 ? " note" : " notes")
          .Append(" in ").Append(list.Count).Append(list.Count == 1 ? " video" : " videos").Append("_\n\n");

        foreach (var section in list)
        {
            sb.Append("## ").Append(section.Title.Trim()).Append('\n');
            if (!string.IsNullOrWhiteSpace(section.Subtitle)) sb.Append('_').Append(section.Subtitle.Trim()).Append("_\n");
            sb.Append('\n');
            AppendBullets(sb, section.Notes);
            sb.Append('\n');
        }
        return sb.ToString().TrimEnd('\n') + "\n";
    }

    private static void AppendBullets(StringBuilder sb, IEnumerable<Note> notes)
    {
        foreach (var note in notes.OrderBy(n => n.Start).ThenBy(n => n.Id))
        {
            var lines = note.Text.Replace("\r\n", "\n").Replace('\r', '\n').Trim().Split('\n');
            sb.Append("- **[").Append(note.TimeLabel).Append("]** ").Append(lines[0].TrimEnd()).Append('\n');
            foreach (var line in lines.Skip(1))
                sb.Append(line.Length == 0 ? string.Empty : "  " + line.TrimEnd()).Append('\n');
        }
    }
}
