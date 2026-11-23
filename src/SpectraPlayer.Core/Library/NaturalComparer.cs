namespace SpectraPlayer.Core.Library;

/// <summary>
/// Orders strings the way people expect for lecture files: digit runs compare by value
/// ("Lecture 2" before "Lecture 10"), everything else ignores case.
/// </summary>
public sealed class NaturalComparer : IComparer<string?>
{
    public static readonly NaturalComparer Instance = new();

    public int Compare(string? x, string? y)
    {
        if (ReferenceEquals(x, y)) return 0;
        if (x is null) return -1;
        if (y is null) return 1;

        int i = 0, j = 0;
        while (i < x.Length && j < y.Length)
        {
            if (char.IsAsciiDigit(x[i]) && char.IsAsciiDigit(y[j]))
            {
                var startX = i;
                while (i < x.Length && char.IsAsciiDigit(x[i])) i++;
                var startY = j;
                while (j < y.Length && char.IsAsciiDigit(y[j])) j++;

                var a = x.AsSpan(startX, i - startX).TrimStart('0');
                var b = y.AsSpan(startY, j - startY).TrimStart('0');
                if (a.Length != b.Length) return a.Length < b.Length ? -1 : 1; // more digits = bigger number
                var c = a.CompareTo(b, StringComparison.Ordinal);
                if (c != 0) return c;

                // Same value ("1" vs "01"): fewer leading zeros first, so the order is stable.
                var zerosX = (i - startX) - a.Length;
                var zerosY = (j - startY) - b.Length;
                if (zerosX != zerosY) return zerosX < zerosY ? -1 : 1;
            }
            else
            {
                var c = char.ToUpperInvariant(x[i]).CompareTo(char.ToUpperInvariant(y[j]));
                if (c != 0) return c;
                i++;
                j++;
            }
        }
        return (x.Length - i).CompareTo(y.Length - j);
    }
}
