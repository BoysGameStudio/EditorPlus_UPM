#if UNITY_EDITOR && ODIN_INSPECTOR && !QUANTUM_ODIN_DISABLED
using System.Collections;
using System.Collections.Generic;

using Sirenix.OdinInspector;

public static class OdinInspectorUtils
{
    public static string GetDescriptionByValue<T>(IEnumerable a, T value, Dictionary<string, string> descriptions, string notmatch)
    {
        foreach (var obj in a)
        {
            if (obj is ValueDropdownItem<T> item && item.Value.Equals(value))
            {
                if (item.Value.Equals(value))
                {
                    return GetDescription(item.Text, descriptions);
                }
            }
        }
        return GetDescription(notmatch, descriptions); ;
    }

    public static string GetLabel<T>(IEnumerable a, T duration, string notmatch)
    {
        foreach (var obj in a)
        {
            if (obj is ValueDropdownItem<T> item && item.Value.Equals(duration))
            {
                if (item.Value.Equals(duration))
                {
                    return item.Text;
                }
            }
        }
        return notmatch;
    }

    public static string GetDescription(string key, Dictionary<string, string> descriptions)
    {
        if (descriptions.TryGetValue(key, out var description))
        {
            return key + "\t" + description;
        }
        return "not found";
    }

    public static string GetFormatedDescriptions(Dictionary<string, string> descriptions)
    {
        var result = "";
        foreach (var kvp in descriptions)
        {
            result += kvp.Key + "\t" + kvp.Value + "\n";
        }
        return result;
    }
}
#endif