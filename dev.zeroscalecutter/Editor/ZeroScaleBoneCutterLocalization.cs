using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEditorInternal;
using UnityEngine;

namespace ZeroScaleBoneCutter.Editors
{
    public enum ZeroScaleBoneCutterLanguage
    {
        English,
        Japanese
    }

    /// <summary>
    /// UI language for every instance of the component. Mirrors dev.materialatlaser's
    /// MaterialAtlaserLocalization / dev.shapekeydecimator's ShapeKeyDecimatorLocalization pattern.
    ///
    /// The two string collections below are index aligned: <see cref="EnglishStrings"/>[i] is
    /// translated by <see cref="JapaneseStrings"/>[i]. The English text doubles as the lookup key, so
    /// call sites read naturally and any string without a translation simply falls back to English
    /// rather than showing a missing-key placeholder. Only prose is translated - mesh/renderer names,
    /// numbers and file paths stay as they are.
    /// </summary>
    public static class ZeroScaleBoneCutterLocalization
    {
        /// <summary>Stored in EditorPrefs, so the choice is global to the editor, not per scene or per component.</summary>
        private const string PreferenceKey = "ZeroScaleBoneCutter.UiLanguage";

        // ------------------------------------------------------------------ English (source)

        public static readonly string[] EnglishStrings =
        {
            "Aggressive Removal",
            "When off (default), a triangle is only cut when every one of its vertices is entirely weighted to zero-scale bones - a vertex that still carries any weight on a surviving bone is left alone, so a boundary that blends into the rest of the mesh stays closed instead of tearing open a hole. Turn this on to remove anything touching a zero-scale bone at all, even partially weighted - more thorough, but can expose a hole at a hard (non-blended) boundary.",
            "No skinned meshes under this GameObject reference a zero-scale bone.",
            "{0} skinned mesh(es) have zero-scale bones and will be cut.",
            "Cutting normally happens during the build (Play mode or avatar upload) and never touches your original assets. This button does the same work right now instead, on a duplicate, and saves the result as real project assets you can inspect.",
            "Create Optimized Copy In Hierarchy",
            "No zero-scale bones found - nothing to cut.",
            "Cutting…",
            "Created '{0}'.\n\n{1} mesh(es) modified, {2} triangle(s) removed total.\n\nMeshes saved to {3}.",
            "OK"
        };

        // ------------------------------------------------------------------ 日本語 (index aligned)

        public static readonly string[] JapaneseStrings =
        {
            "積極的に削除",
            "オフの場合(デフォルト)、三角形はすべての頂点がスケール0のボーンに完全にウェイトされている場合のみ削除されます - 生き残るボーンに少しでもウェイトが残っている頂点はそのままにされるため、メッシュの他の部分に馴染むように滑らかにつながっている境界は、穴が開かずに閉じたままになります。オンにすると、スケール0のボーンに少しでも触れている部分をすべて削除します - より徹底的ですが、滑らかにつながっていない(ブレンドされていない)境界では穴が開くことがあります。",
            "このGameObject以下にスケール0のボーンを参照しているスキンドメッシュはありません。",
            "{0} 個のスキンドメッシュにスケール0のボーンがあり、削除されます。",
            "削除処理は通常ビルド時(Playモードまたはアバターのアップロード時)に行われ、元のアセットには触れません。このボタンは複製に対して同じ処理を今すぐ実行し、結果を確認可能な実プロジェクトアセットとして保存します。",
            "最適化済みコピーをヒエラルキーに作成",
            "スケール0のボーンが見つかりません - 削除するものがありません。",
            "削除中…",
            "'{0}' を作成しました。\n\n{1} 個のメッシュを変更し、合計 {2} 個の三角形を削除しました。\n\nメッシュは {3} に保存されました。",
            "OK"
        };

        // ------------------------------------------------------------------ current language

        private static ZeroScaleBoneCutterLanguage? _current;
        private static Dictionary<string, string> _lookup;

        public static ZeroScaleBoneCutterLanguage Current
        {
            get
            {
                if (_current == null)
                {
                    var stored = EditorPrefs.GetString(PreferenceKey, string.Empty);
                    if (stored == "ja") _current = ZeroScaleBoneCutterLanguage.Japanese;
                    else if (stored == "en") _current = ZeroScaleBoneCutterLanguage.English;
                    else _current = DetectDefaultLanguage();
                }
                return _current.Value;
            }
            set
            {
                if (_current == value) return;
                _current = value;
                EditorPrefs.SetString(PreferenceKey, value == ZeroScaleBoneCutterLanguage.Japanese ? "ja" : "en");

                // Every inspector showing this component has to follow the change, not just the one
                // that was clicked.
                InternalEditorUtility.RepaintAllViews();
            }
        }

        /// <summary>
        /// Japanese when the machine is on Japan time, English otherwise. Note that the UTC+9 check
        /// also matches Korea, which is the intended trade for not needing a full locale table.
        /// </summary>
        private static ZeroScaleBoneCutterLanguage DetectDefaultLanguage()
        {
            try
            {
                var zone = TimeZoneInfo.Local;
                var id = zone.Id ?? string.Empty;

                if (id.IndexOf("Tokyo", StringComparison.OrdinalIgnoreCase) >= 0 ||
                    id.IndexOf("Japan", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    return ZeroScaleBoneCutterLanguage.Japanese;
                }

                // JST is UTC+9 year round, no daylight saving.
                if (zone.BaseUtcOffset == TimeSpan.FromHours(9))
                {
                    return ZeroScaleBoneCutterLanguage.Japanese;
                }
            }
            catch (Exception)
            {
                // Some platforms refuse to report a time zone; English is the safe fallback.
            }

            return ZeroScaleBoneCutterLanguage.English;
        }

        // ------------------------------------------------------------------ lookup

        private static Dictionary<string, string> Lookup
        {
            get
            {
                if (_lookup != null) return _lookup;

                _lookup = new Dictionary<string, string>(EnglishStrings.Length);

                if (EnglishStrings.Length != JapaneseStrings.Length)
                {
                    Debug.LogError(
                        $"[Zero Scale Bone Cutter] Translation tables are out of step: {EnglishStrings.Length} English " +
                        $"entries vs {JapaneseStrings.Length} Japanese. Untranslated text will show in English.");
                }

                var count = Mathf.Min(EnglishStrings.Length, JapaneseStrings.Length);
                for (var i = 0; i < count; i++)
                {
                    if (!_lookup.ContainsKey(EnglishStrings[i])) _lookup.Add(EnglishStrings[i], JapaneseStrings[i]);
                }

                return _lookup;
            }
        }

        /// <summary>Translates a string, falling back to the English source when unmapped.</summary>
        public static string T(string english)
        {
            if (Current == ZeroScaleBoneCutterLanguage.English || string.IsNullOrEmpty(english)) return english;
            return Lookup.TryGetValue(english, out var translated) ? translated : english;
        }

        /// <summary>Translates a format string, then fills in the arguments.</summary>
        public static string T(string english, params object[] args)
        {
            return string.Format(T(english), args);
        }

        // ------------------------------------------------------------------ language selector

        private static readonly Color ActiveLanguageColor = new Color(0.53f, 0.71f, 1f);

        /// <summary>Draws the English / 日本語 selector. Affects every instance of the component.</summary>
        public static void DrawLanguageBar()
        {
            using (new EditorGUILayout.HorizontalScope())
            {
                GUILayout.FlexibleSpace();
                DrawLanguageButton("English", ZeroScaleBoneCutterLanguage.English);
                DrawLanguageButton("日本語", ZeroScaleBoneCutterLanguage.Japanese);
            }
        }

        private static void DrawLanguageButton(string label, ZeroScaleBoneCutterLanguage language)
        {
            var isActive = Current == language;

            var previousColor = GUI.backgroundColor;
            if (isActive) GUI.backgroundColor = ActiveLanguageColor;

            var clicked = GUILayout.Button(label, EditorStyles.miniButton, GUILayout.Width(72f));

            GUI.backgroundColor = previousColor;

            if (clicked && !isActive) Current = language;
        }
    }
}
