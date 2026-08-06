using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEditorInternal;
using UnityEngine;

namespace BoneMerger.Editors {
    public enum BoneMergerLanguage {
        English,
        Japanese
    }

    /// <summary>
    /// UI language for the tool's dialogs. There is no persistent Inspector to host a language
    /// button bar in (Bone Merger is a pure menu command, not a component) - see the
    /// "Tools/Bone Merger/Language" menu items instead. Mirrors dev.shapekeydecimator's
    /// ShapeKeyDecimatorLocalization pattern.
    ///
    /// The two string collections below are index aligned: <see cref="EnglishStrings"/>[i] is
    /// translated by <see cref="JapaneseStrings"/>[i]. The English text doubles as the lookup key, so
    /// call sites read naturally and any string without a translation simply falls back to English
    /// rather than showing a missing-key placeholder. Only prose is translated - bone/renderer names,
    /// numbers and file paths stay as they are.
    /// </summary>
    public static class BoneMergerLocalization {
        /// <summary>Stored in EditorPrefs, so the choice is global to the editor.</summary>
        private const string PreferenceKey = "BoneMerger.UiLanguage";

        // ------------------------------------------------------------------ English (source)

        public static readonly string[] EnglishStrings = {
            "Select one or more bones with a parent in the Hierarchy first.",
            "Merge {0} bone(s) into their parent(s)?",
            "{0} renderer(s)/mesh(es) will be modified and saved to {1}.",
            "The merged bone GameObject(s) will be deleted; any of their children that weren't also selected are reparented onto the merge target first.",
            "\n{0} of them carry other components (PhysBone, constraints, etc.) - those will be deleted too.",
            "\nThis is destructive but undoable (Ctrl+Z).",
            "Merge",
            "Cancel",
            "Merged {0} bone(s) into their parent(s).",
            "Updated {0} renderer(s):",
            "\n{0} of them had no mesh weight - merged structurally with nothing to remap:",
            "\n{0} selected object(s) had no parent and were skipped.",
            "OK"
        };

        // ------------------------------------------------------------------ 日本語 (index aligned)

        public static readonly string[] JapaneseStrings = {
            "Hierarchyで親を持つボーンを1つ以上選択してください。",
            "{0} 個のボーンを親にマージしますか?",
            "{0} 個のレンダラー/メッシュが変更され、{1} に保存されます。",
            "マージされたボーンのGameObjectは削除されます。選択されていない子は、先にマージ先へ親を付け替えられます。",
            "\nそのうち {0} 個は他のコンポーネント(PhysBone、コンストレイントなど)を持っています - それらも削除されます。",
            "\nこれは破壊的な操作ですが、元に戻せます(Ctrl+Z)。",
            "マージ",
            "キャンセル",
            "{0} 個のボーンを親にマージしました。",
            "{0} 個のレンダラーを更新しました:",
            "\nそのうち {0} 個はメッシュのウェイトがなかったため、置き換えるものはありませんでしたが構造的にマージされました:",
            "\n{0} 個の選択オブジェクトは親を持たないためスキップされました。",
            "OK"
        };

        // ------------------------------------------------------------------ current language

        private static BoneMergerLanguage? _current;
        private static Dictionary<string, string> _lookup;

        public static BoneMergerLanguage Current {
            get {
                if (_current == null) {
                    var stored = EditorPrefs.GetString(PreferenceKey, string.Empty);
                    if (stored == "ja") _current = BoneMergerLanguage.Japanese;
                    else if (stored == "en") _current = BoneMergerLanguage.English;
                    else _current = DetectDefaultLanguage();
                }
                return _current.Value;
            }
            set {
                if (_current == value) return;
                _current = value;
                EditorPrefs.SetString(PreferenceKey, value == BoneMergerLanguage.Japanese ? "ja" : "en");
                InternalEditorUtility.RepaintAllViews();
            }
        }

        /// <summary>
        /// Japanese when the machine is on Japan time, English otherwise. Note that the UTC+9 check
        /// also matches Korea, which is the intended trade for not needing a full locale table.
        /// </summary>
        private static BoneMergerLanguage DetectDefaultLanguage() {
            try {
                var zone = TimeZoneInfo.Local;
                var id = zone.Id ?? string.Empty;

                if (id.IndexOf("Tokyo", StringComparison.OrdinalIgnoreCase) >= 0 ||
                    id.IndexOf("Japan", StringComparison.OrdinalIgnoreCase) >= 0) {
                    return BoneMergerLanguage.Japanese;
                }

                // JST is UTC+9 year round, no daylight saving.
                if (zone.BaseUtcOffset == TimeSpan.FromHours(9)) {
                    return BoneMergerLanguage.Japanese;
                }
            } catch (Exception) {
                // Some platforms refuse to report a time zone; English is the safe fallback.
            }

            return BoneMergerLanguage.English;
        }

        // ------------------------------------------------------------------ lookup

        private static Dictionary<string, string> Lookup {
            get {
                if (_lookup != null) return _lookup;

                _lookup = new Dictionary<string, string>(EnglishStrings.Length);

                if (EnglishStrings.Length != JapaneseStrings.Length) {
                    Debug.LogError(
                        $"[Bone Merger] Translation tables are out of step: {EnglishStrings.Length} English " +
                        $"entries vs {JapaneseStrings.Length} Japanese. Untranslated text will show in English.");
                }

                var count = Mathf.Min(EnglishStrings.Length, JapaneseStrings.Length);
                for (var i = 0; i < count; i++) {
                    if (!_lookup.ContainsKey(EnglishStrings[i])) _lookup.Add(EnglishStrings[i], JapaneseStrings[i]);
                }

                return _lookup;
            }
        }

        /// <summary>Translates a string, falling back to the English source when unmapped.</summary>
        public static string T(string english) {
            if (Current == BoneMergerLanguage.English || string.IsNullOrEmpty(english)) return english;
            return Lookup.TryGetValue(english, out var translated) ? translated : english;
        }

        /// <summary>Translates a format string, then fills in the arguments.</summary>
        public static string T(string english, params object[] args) {
            return string.Format(T(english), args);
        }

        // ------------------------------------------------------------------ language menu

        [MenuItem("Tools/Bone Merger/Language/English", false, 1)]
        private static void SetLanguageEnglish() => Current = BoneMergerLanguage.English;

        [MenuItem("Tools/Bone Merger/Language/English", true)]
        private static bool ValidateSetLanguageEnglish() {
            Menu.SetChecked("Tools/Bone Merger/Language/English", Current == BoneMergerLanguage.English);
            return true;
        }

        [MenuItem("Tools/Bone Merger/Language/日本語", false, 2)]
        private static void SetLanguageJapanese() => Current = BoneMergerLanguage.Japanese;

        [MenuItem("Tools/Bone Merger/Language/日本語", true)]
        private static bool ValidateSetLanguageJapanese() {
            Menu.SetChecked("Tools/Bone Merger/Language/日本語", Current == BoneMergerLanguage.Japanese);
            return true;
        }
    }
}
