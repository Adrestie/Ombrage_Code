using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace Ombrage.ClaudeCodeGUI
{
    [Serializable]
    public class Snippet
    {
        public string title;
        public string text;
    }

    [Serializable]
    internal class SnippetList
    {
        public List<Snippet> items = new();
    }

    /// <summary>Modèles de prompts réutilisables, persistés dans EditorPrefs.</summary>
    public static class SnippetStore
    {
        private const string KEY = "ClaudeCodeGUI_Snippets";

        public static List<Snippet> LoadAll()
        {
            string json = EditorPrefs.GetString(KEY, "");
            if (string.IsNullOrEmpty(json)) return new List<Snippet>();
            try { return JsonUtility.FromJson<SnippetList>(json)?.items ?? new List<Snippet>(); }
            catch { return new List<Snippet>(); }
        }

        public static void SaveAll(List<Snippet> items)
        {
            EditorPrefs.SetString(KEY, JsonUtility.ToJson(new SnippetList { items = items ?? new List<Snippet>() }));
        }

        public static void Add(string title, string text)
        {
            var list = LoadAll();
            list.Add(new Snippet { title = title, text = text });
            SaveAll(list);
        }
    }
}
