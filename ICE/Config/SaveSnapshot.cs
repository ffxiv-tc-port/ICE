using System.Collections;
using System.Collections.Generic;
using System.Reflection;

namespace ICE.Config;

/// <summary>
/// 存檔快照的複製工具，以及「快照與本體不共用任何可變實例」的反射稽核。
/// </summary>
/// <remarks>
/// 🔴 契約：快照拍完之後交給背景執行緒序列化，主執行緒同時繼續改本體。
/// 只要快照圖裡有任何一個集合或設定物件和本體是同一個實例，序列化途中就可能擲
/// 「Collection was modified」，那一次存檔整份丟失 ⇒ <see cref="FindShared"/> 不是空的就不能走背景序列化。
/// </remarks>
internal static class SaveSnapshot
{
    private static readonly Func<object, object> MemberwiseCloneFn = (Func<object, object>)Delegate.CreateDelegate(
        typeof(Func<object, object>),
        typeof(object).GetMethod("MemberwiseClone", BindingFlags.Instance | BindingFlags.NonPublic)!);

    /// <summary>淺層複製（純量成員全部照抄，參考型成員仍指向原實例，呼叫端要自己再換掉）。</summary>
    public static T Shallow<T>(T o) where T : class? => o == null ? o : (T)MemberwiseCloneFn(o);

    public static List<T>? CopyList<T>(List<T>? src, Func<T, T>? each = null)
    {
        if (src == null)
            return null;
        if (each == null)
            return new List<T>(src);
        var r = new List<T>(src.Count);
        foreach (var x in src)
            r.Add(each(x));
        return r;
    }

    /// <summary>逐筆依原列舉順序加入新字典（輸出的鍵順序與原字典相同），沿用原字典的比較子。</summary>
    public static Dictionary<TKey, TValue>? CopyDict<TKey, TValue>(Dictionary<TKey, TValue>? src, Func<TValue, TValue>? each = null)
        where TKey : notnull
    {
        if (src == null)
            return null;
        var r = new Dictionary<TKey, TValue>(src.Count, src.Comparer);
        foreach (var kv in src)
            r.Add(kv.Key, each == null ? kv.Value : each(kv.Value));
        return r;
    }

    /// <summary>
    /// 回傳 <paramref name="snapshot"/> 物件圖裡「同一個實例也出現在 <paramref name="original"/> 物件圖裡」的路徑；空＝完全不共用。
    /// </summary>
    /// <remarks>
    /// 字串不算（不可變）；裝箱的值型別每次讀出都是新實例，也不算。
    /// 本組件的型別往所有執行個體欄位（含自動屬性的後備欄位）走；集合只走元素，不走它的內部欄位。
    /// </remarks>
    public static List<string> FindShared(object original, object snapshot, int max = 8)
    {
        var inOriginal = new HashSet<object>(ReferenceEqualityComparer.Instance);
        Walk(original, null, inOriginal, null, null, max);
        var hits = new List<string>();
        Walk(snapshot, null, new HashSet<object>(ReferenceEqualityComparer.Instance), inOriginal, hits, max);
        if (hits.Count == 0)
            return hits;
        // 有命中才重走一次組路徑字串（正常情況不配置那一大堆路徑）。
        hits.Clear();
        Walk(snapshot, "root", new HashSet<object>(ReferenceEqualityComparer.Instance), inOriginal, hits, max);
        return hits;
    }

    private static void Walk(object? o, string? path, HashSet<object> visited, HashSet<object>? other, List<string>? hits, int max)
    {
        if (o == null || o is string || hits?.Count >= max)
            return;
        var t = o.GetType();
        if (t.IsValueType || !visited.Add(o))
            return;
        if (other != null && other.Contains(o))
        {
            hits!.Add($"{path} ({t.Name})");
            return;
        }

        if (o is IDictionary dict)
        {
            foreach (DictionaryEntry e in dict)
            {
                Walk(e.Key, path == null ? null : $"{path}.key", visited, other, hits, max);
                Walk(e.Value, path == null ? null : $"{path}[{e.Key}]", visited, other, hits, max);
            }
            return;
        }
        if (o is IEnumerable seq)
        {
            var i = 0;
            foreach (var x in seq)
                Walk(x, path == null ? null : $"{path}[{i++}]", visited, other, hits, max);
            return;
        }
        if (t.Assembly != typeof(SaveSnapshot).Assembly)
            return;

        for (var bt = t; bt != null && bt != typeof(object); bt = bt.BaseType)
        {
            foreach (var f in bt.GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly))
                Walk(f.GetValue(o), path == null ? null : $"{path}.{MemberName(f)}", visited, other, hits, max);
        }
    }

    private static string MemberName(FieldInfo f)
    {
        var n = f.Name;
        var close = n.IndexOf('>');
        return n.StartsWith('<') && close > 1 ? n[1..close] : n;
    }
}
