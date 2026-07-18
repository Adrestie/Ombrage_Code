using System;
using System.Collections.Generic;
using UnityEngine;
using System.Collections;

public static class ListExtension
{
    public static bool IsNullOrEmpty(this IList List)
    {
        return (List == null || List.Count < 1);
    }

    public static bool IsNullOrEmpty(this IDictionary Dictionary)
    {
        return (Dictionary == null || Dictionary.Count < 1);
    }

    public static bool MoveToFront<T>(this IList<T> List, T element)
    {
        if (!List.Contains(element))
            return false;

        List.Remove(element);
        List.Insert(0, element);

        return true;
    }

    public static bool MoveToFront<T>(this T[] mos, Predicate<T> match)
    {
        if (mos.Length == 0)
        {
            return false;
        }
        var idx = Array.FindIndex(mos, match);
        if (idx == -1)
        {
            return false;
        }
        var tmp = mos[idx];
        Array.Copy(mos, 0, mos, 1, idx);
        mos[0] = tmp;
        return true;
    }

    public static bool MoveToBack<T>(this IList<T> List, T element)
    {
        if (!List.Contains(element))
            return false;

        List.Remove(element);
        List.Add(element);

        return true;
    }

    public static bool MoveToBack<T>(this T[] mos, Predicate<T> match)
    {
        if (mos.Length == 0)
            return false;

        var idx = Array.FindIndex(mos, match);
        if (idx == -1)
            return false;

        var tmp = mos[idx];
        Array.Copy(mos, idx + 1, mos, idx, mos.Length - idx - 1);
        mos[mos.Length - 1] = tmp;
        return true;
    }
}


