using System;
using System.Collections.Generic;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEngine;

namespace Ombrage.ClaudeCodeGUI
{
    /// <summary>
    /// Popup pour répondre à un AskUserQuestion (modale IMGUI). Reproduit le composant
    /// AskUserQuestion de Claude Code : une ou plusieurs questions, chacune avec ses options
    /// (single/multi-select) et une option « Autre » (réponse libre).
    /// </summary>
    public class AskUserQuestionDialog : EditorWindow
    {
        public readonly struct Answer
        {
            public readonly string Question;
            public readonly string Value;
            public Answer(string q, string v) { Question = q; Value = v; }
        }

        private JArray               _questions;
        private Action<List<Answer>> _onSubmit;   // null = annulé
        private List<bool[]>         _selected = new();
        private List<string>         _customAnswers = new();
        private Vector2              _scroll;
        private GUIStyle             _questionStyle, _descStyle;

        public static AskUserQuestionDialog Show(JArray questions, Action<List<Answer>> onSubmit)
        {
            var w = CreateInstance<AskUserQuestionDialog>();
            w.titleContent = new GUIContent("Claude — Question");
            w._questions = questions;
            w._onSubmit = onSubmit;

            foreach (var q in questions)
            {
                var options = q["options"] as JArray;
                int n = options?.Count ?? 0;
                w._selected.Add(new bool[n]);
                w._customAnswers.Add("");
            }

            w.minSize = new Vector2(520, 360);
            w.maxSize = new Vector2(900, 900);
            w.ShowUtility();
            w.Focus();
            return w;
        }

        private void InitStyles()
        {
            if (_questionStyle != null) return;
            _questionStyle = new GUIStyle(EditorStyles.boldLabel)
            {
                fontSize = 13,
                wordWrap = true,
                padding = new RectOffset(4, 4, 4, 2),
            };
            _descStyle = new GUIStyle(EditorStyles.miniLabel)
            {
                wordWrap = true,
                padding = new RectOffset(24, 8, 0, 4),
                fontStyle = FontStyle.Italic,
            };
        }

        private void OnGUI()
        {
            InitStyles();
            if (_questions == null || _questions.Count == 0) { Close(); return; }

            _scroll = EditorGUILayout.BeginScrollView(_scroll);

            for (int i = 0; i < _questions.Count; i++)
            {
                var q = _questions[i];
                string question = (string)q["question"] ?? "(question vide)";
                string header   = (string)q["header"];
                bool   multi    = (bool?)q["multiSelect"] ?? false;
                var    options  = q["options"] as JArray;

                EditorGUILayout.BeginVertical(EditorStyles.helpBox);

                if (!string.IsNullOrEmpty(header))
                    EditorGUILayout.LabelField(header.ToUpperInvariant(), EditorStyles.miniBoldLabel);

                EditorGUILayout.LabelField(question, _questionStyle);

                if (multi) EditorGUILayout.LabelField("(choix multiple)", EditorStyles.miniLabel);
                EditorGUILayout.Space(2);

                if (options != null)
                {
                    for (int j = 0; j < options.Count; j++)
                    {
                        string label = (string)options[j]["label"] ?? "?";
                        string desc  = (string)options[j]["description"];

                        bool prev = _selected[i][j];
                        bool now  = EditorGUILayout.ToggleLeft(label, prev);
                        if (now != prev)
                        {
                            if (!multi && now)
                                for (int k = 0; k < _selected[i].Length; k++) _selected[i][k] = false;
                            _selected[i][j] = now;
                        }
                        if (!string.IsNullOrEmpty(desc))
                            EditorGUILayout.LabelField(desc, _descStyle);
                    }
                }

                EditorGUILayout.Space(4);
                EditorGUILayout.LabelField("Autre (réponse libre, prioritaire si remplie) :", EditorStyles.miniLabel);
                _customAnswers[i] = EditorGUILayout.TextField(_customAnswers[i]);

                EditorGUILayout.EndVertical();
                EditorGUILayout.Space(6);
            }

            EditorGUILayout.EndScrollView();

            EditorGUILayout.BeginHorizontal();
            GUILayout.FlexibleSpace();
            if (GUILayout.Button("Annuler", GUILayout.Width(100), GUILayout.Height(26)))
            {
                _onSubmit?.Invoke(null);
                _onSubmit = null;
                Close();
                return;
            }
            if (GUILayout.Button("Envoyer", GUILayout.Width(120), GUILayout.Height(26)))
            {
                var answers = BuildAnswers();
                _onSubmit?.Invoke(answers);
                _onSubmit = null;
                Close();
                return;
            }
            EditorGUILayout.EndHorizontal();
            EditorGUILayout.Space(4);
        }

        private List<Answer> BuildAnswers()
        {
            var result = new List<Answer>();
            for (int i = 0; i < _questions.Count; i++)
            {
                string question = (string)_questions[i]["question"] ?? "";
                var options = _questions[i]["options"] as JArray;

                string custom = (_customAnswers[i] ?? "").Trim();
                string value;

                if (!string.IsNullOrEmpty(custom))
                {
                    value = custom;
                }
                else
                {
                    var picked = new List<string>();
                    if (options != null)
                        for (int j = 0; j < _selected[i].Length && j < options.Count; j++)
                            if (_selected[i][j])
                                picked.Add((string)options[j]["label"] ?? "?");
                    value = picked.Count == 0 ? "(aucune réponse)" : string.Join(", ", picked);
                }

                result.Add(new Answer(question, value));
            }
            return result;
        }

        private void OnDestroy()
        {
            // Fermeture sans clic explicite = annulation.
            _onSubmit?.Invoke(null);
            _onSubmit = null;
        }
    }
}
