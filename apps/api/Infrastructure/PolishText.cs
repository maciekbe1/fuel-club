using System.Globalization;
using System.Text;

namespace FuelClub.Api.Infrastructure;

public static class PolishText
{
    /// <summary>Lower case without Polish diacritics, so "Saldo", "salda" and "SALDO" match alike.</summary>
    public static string Normalize(string text)
    {
        var decomposed = text.ToLowerInvariant()
            .Replace('ł', 'l')
            .Normalize(NormalizationForm.FormD);
        var builder = new StringBuilder(decomposed.Length);
        foreach (var c in decomposed)
            if (CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.NonSpacingMark)
                builder.Append(c);
        return builder.ToString();
    }
}
