using System.Collections;
using System.Collections.Generic;
using System.Linq;

namespace TestFramework.Mock.Matching;

/// <summary>
/// How an argument or an expected value reads in a refusal. One renderer for both sides, so a call
/// and the setup it failed to match describe the same value the same way - and a collection shows
/// its elements instead of <c>System.Byte[]</c>, which would name the type and hide the difference.
/// </summary>
internal static class MockValueText
{
    private const int ElementBudget = 8;

    public static string Describe(object? value)
    {
        return value switch
        {
            null => "null",
            string text => $"\"{text}\"",
            ICollection collection => DescribeCollection(collection),
            _ => value.ToString() ?? value.GetType().Name,
        };
    }

    /// <summary>
    /// A type as a reader writes it - <c>ValueTask&lt;Boolean&gt;</c> rather than <c>ValueTask`1</c>.
    /// </summary>
    public static string DescribeType(System.Type type)
    {
        if (!type.IsGenericType)
        {
            return type.Name;
        }

        return $"{type.Name[..type.Name.IndexOf('`')]}<{string.Join(", ", type.GetGenericArguments().Select(DescribeType))}>";
    }

    private static string DescribeCollection(ICollection collection)
    {
        IEnumerable<string> shown = collection.Cast<object?>().Take(ElementBudget).Select(Describe);
        string more = collection.Count > ElementBudget ? $", … {collection.Count} items" : string.Empty;
        return $"[{string.Join(", ", shown)}{more}]";
    }
}
