using System.Text;

namespace WebAppForge.Core.Generation;

/// <summary>Helpers that turn database names into C# identifiers, URL segments and quoted SQL names.</summary>
public static class Identifier
{
    /// <summary>PascalCase from a database name, e.g. "order_items" -> "OrderItems".</summary>
    public static string Pascal(string name)
    {
        var sb = new StringBuilder();
        var upperNext = true;
        foreach (var c in name)
        {
            if (!char.IsLetterOrDigit(c))
            {
                upperNext = true;
                continue;
            }

            sb.Append(upperNext ? char.ToUpperInvariant(c) : c);
            upperNext = false;
        }

        if (sb.Length == 0 || char.IsDigit(sb[0]))
        {
            sb.Insert(0, '_');
        }

        return sb.ToString();
    }

    /// <summary>Lower-case URL segment, e.g. "Order Items" -> "order-items".</summary>
    public static string Route(string name)
    {
        var sb = new StringBuilder();
        foreach (var c in name)
        {
            if (char.IsLetterOrDigit(c))
            {
                sb.Append(char.ToLowerInvariant(c));
            }
            else if (sb.Length > 0 && sb[^1] != '-')
            {
                sb.Append('-');
            }
        }

        return sb.ToString().TrimEnd('-');
    }
}
