using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEditorInternal;
using UnityEngine;

namespace ShapeKeyDecimator.Editors
{
    public enum ShapeKeyDecimatorLanguage
    {
        English,
        Japanese
    }

    /// <summary>
    /// UI language for every instance of the component.
    ///
    /// The two string collections below are index aligned: <see cref="EnglishStrings"/>[i] is
    /// translated by <see cref="JapaneseStrings"/>[i]. The English text doubles as the lookup key, so
    /// call sites read naturally and any string without a translation simply falls back to English
    /// rather than showing a missing-key placeholder.
    ///
    /// Only prose is translated. Mesh names, shape key names, numbers, file paths and the tool's own
    /// name stay as they are.
    /// </summary>
    public static class ShapeKeyDecimatorLocalization
    {
        /// <summary>Stored in EditorPrefs, so the choice is global to the editor, not per scene or per component.</summary>
        private const string PreferenceKey = "ShapeKeyDecimator.UiLanguage";

        // ------------------------------------------------------------------ English (source)

        public static readonly string[] EnglishStrings =
        {
            // Target lists
            "Skinned Meshes",
            "Skinned meshes to decimate. These can use both shape key regions and the whole mesh slider.",
            "Meshes",
            "Plain meshes to decimate. Only the whole mesh slider applies to these, since they have no shape keys.",
            "Drag at least one Skinned Mesh Renderer or Mesh Renderer into the lists above. This component only ever touches the meshes you assign.",
            "Only the whole mesh slider applies to plain meshes. Shape key rows below affect skinned meshes only.",

            // Advanced settings
            "Advanced",
            "Delta Threshold",
            "Protect Region Boundary",
            "Preserve Open Borders",
            "Preserve Submesh Boundaries",
            "Preserve UV Seams",
            "UV Error Weight",
            "Max Normal Deviation",
            "With UV seams unlocked, collapses can merge two texture-space corners that share a position. That is the main cause of smearing along seams.",
            "Max Normal Deviation is very low, which rejects almost every collapse — at 0 no triangle may rotate at all, so nothing gets decimated on a curved mesh. Higher values decimate more; lower values are more conservative.",

            // Table
            "Shape Key",
            "Tris",
            "Triangles that lie inside the region this shape key moves.",
            "After",
            "Triangles remaining in that region once decimated.",
            "Decimation",
            "0 = untouched, 1 = maximum reduction.",
            "No shape keys selected yet. Use the list below to add one.",
            "No triangles fully inside this shape key's region — nothing to decimate.",
            "Whole mesh",
            "Applied to the entire mesh after every shape key region above.",

            // Totals
            "Total △ (measured)",
            "Total △ (estimate)",
            "Measured from a real decimation pass with the current settings, so these are the numbers you will actually get.",
            "Estimate: region triangles × slider. It assumes every collapse succeeds, so it is an upper bound on the reduction, not a prediction. Collapses get rejected by Max Normal Deviation, UV seam and border protection and by the topology itself, and overlapping regions compound. Press Measure below, or turn on Preview, for the real figures.",
            "Measure Exact Result",

            // Add section
            "Add shape key to decimate ({0} available)",
            "Search",
            "{0} tris",

            // Apply / preview
            "Decimation happens during the build (Play mode or avatar upload). Your original mesh asset is never modified.",
            "Create Decimated Copy In Hierarchy",
            "Bakes the result now: saves the reduced meshes as assets and adds a decimated duplicate of each renderer next to the original.",
            "Preview Decimation",
            "PREVIEW ON  •  click to turn off",
            "PREVIEW ON  •  updating…",
            "Showing a temporary decimated duplicate; the original renderer is switched off. Turning preview off restores it and deletes the duplicate. Preview also ends automatically when you enter Play mode.",
            "Temporarily swaps in a decimated duplicate so you can judge the result in the scene. Nothing is saved and the duplicate never enters a scene file or a build.",

            // Dialogs and progress
            "Nothing to preview. Check that the selected shape keys exist on the target meshes and that a strength is above zero.",
            "Nothing was decimated. Check that the selected shape keys exist on the target meshes and that their strength is above zero.",
            "Decimating meshes…",
            "Measuring result…",
            "Building preview…",
            "{0}: {1} → {2} tris",
            "Meshes saved to {0}.",
            "OK"
        };

        // ------------------------------------------------------------------ 日本語 (index aligned)

        public static readonly string[] JapaneseStrings =
        {
            // 対象リスト
            "スキンドメッシュ",
            "削減するスキンドメッシュ。シェイプキー領域と「メッシュ全体」の両方を適用できます。",
            "メッシュ",
            "削減する通常のメッシュ。シェイプキーを持たないため、「メッシュ全体」のみが適用されます。",
            "上のリストにスキンドメッシュレンダラーまたはメッシュレンダラーを1つ以上ドラッグしてください。このコンポーネントは指定されたメッシュのみを対象にします。",
            "通常のメッシュには「メッシュ全体」のみが適用されます。下のシェイプキーの行はスキンドメッシュにのみ影響します。",

            // 詳細設定
            "詳細設定",
            "変位のしきい値",
            "領域の境界を保護",
            "開いた境界を保持",
            "サブメッシュ境界を保持",
            "UVシームを保持",
            "UV誤差の重み",
            "法線の最大許容角度",
            "UVシームの保持を解除すると、同じ位置にある2つのUV頂点が結合されることがあります。これがシーム沿いのテクスチャのにじみの主な原因です。",
            "法線の最大許容角度が非常に低いため、ほぼすべての収縮が却下されます。0では三角形が一切回転できないため、曲面のメッシュでは何も削減されません。値を大きくするほど削減が進み、小さくするほど保守的になります。",

            // 表
            "シェイプキー",
            "三角形",
            "このシェイプキーが動かす領域内にある三角形の数。",
            "削減後",
            "削減後にその領域に残る三角形の数。",
            "削減率",
            "0 = 削減なし、1 = 最大限に削減。",
            "シェイプキーがまだ選択されていません。下のリストから追加してください。",
            "このシェイプキーの領域内に完全に含まれる三角形がありません。削減できるものがありません。",
            "メッシュ全体",
            "上記のすべてのシェイプキー領域を処理した後、メッシュ全体に適用されます。",

            // 合計
            "合計△（実測）",
            "合計△（推定）",
            "現在の設定で実際に削減処理を実行した結果です。この数値がそのまま反映されます。",
            "推定値：領域のポリゴン数 × スライダー。すべての収縮が成功する前提のため、削減量の上限であり予測ではありません。法線の最大許容角度、UVシームや境界の保護、メッシュの形状そのものによって収縮は却下され、領域が重なる場合は効果が累積します。実際の数値は下の「正確な結果を実測する」またはプレビューで確認してください。",
            "正確な結果を実測する",

            // 追加
            "削減するシェイプキーを追加（{0} 件）",
            "検索",
            "{0} 三角形",

            // 適用・プレビュー
            "削減はビルド時（Playモードまたはアバターのアップロード時）に実行されます。元のメッシュアセットは変更されません。",
            "削減済みのコピーをヒエラルキーに作成",
            "結果をすぐに確定します：削減したメッシュをアセットとして保存し、各レンダラーの削減済み複製を元のオブジェクトの隣に追加します。",
            "削減をプレビュー",
            "プレビュー中  •  クリックで解除",
            "プレビュー中  •  更新中…",
            "一時的な削減済み複製を表示しています。元のレンダラーは無効化されています。プレビューを解除すると元に戻り、複製は削除されます。Playモードに入ると自動的に解除されます。",
            "削減結果をシーン上で確認するために、一時的に削減済みの複製に差し替えます。保存は行われず、複製がシーンファイルやビルドに含まれることはありません。",

            // ダイアログ・進行状況
            "プレビューできる対象がありません。選択したシェイプキーが対象メッシュに存在し、削減率が0より大きいことを確認してください。",
            "削減は実行されませんでした。選択したシェイプキーが対象メッシュに存在し、削減率が0より大きいことを確認してください。",
            "メッシュを削減中…",
            "結果を計測中…",
            "プレビューを作成中…",
            "{0}: {1} → {2} ポリゴン",
            "メッシュを {0} に保存しました。",
            "OK"
        };

        // ------------------------------------------------------------------ current language

        private static ShapeKeyDecimatorLanguage? _current;
        private static Dictionary<string, string> _lookup;

        public static ShapeKeyDecimatorLanguage Current
        {
            get
            {
                if (_current == null)
                {
                    var stored = EditorPrefs.GetString(PreferenceKey, string.Empty);
                    if (stored == "ja") _current = ShapeKeyDecimatorLanguage.Japanese;
                    else if (stored == "en") _current = ShapeKeyDecimatorLanguage.English;
                    else _current = DetectDefaultLanguage();
                }
                return _current.Value;
            }
            set
            {
                if (_current == value) return;
                _current = value;
                EditorPrefs.SetString(PreferenceKey, value == ShapeKeyDecimatorLanguage.Japanese ? "ja" : "en");

                // Every inspector showing this component has to follow the change, not just the one
                // that was clicked.
                InternalEditorUtility.RepaintAllViews();
            }
        }

        /// <summary>
        /// Japanese when the machine is on Japan time, English otherwise. Note that the UTC+9 check
        /// also matches Korea, which is the intended trade for not needing a full locale table.
        /// </summary>
        private static ShapeKeyDecimatorLanguage DetectDefaultLanguage()
        {
            try
            {
                var zone = TimeZoneInfo.Local;
                var id = zone.Id ?? string.Empty;

                if (id.IndexOf("Tokyo", StringComparison.OrdinalIgnoreCase) >= 0 ||
                    id.IndexOf("Japan", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    return ShapeKeyDecimatorLanguage.Japanese;
                }

                // JST is UTC+9 year round, no daylight saving.
                if (zone.BaseUtcOffset == TimeSpan.FromHours(9))
                {
                    return ShapeKeyDecimatorLanguage.Japanese;
                }
            }
            catch (Exception)
            {
                // Some platforms refuse to report a time zone; English is the safe fallback.
            }

            return ShapeKeyDecimatorLanguage.English;
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
                        $"[Shape Key Decimator] Translation tables are out of step: {EnglishStrings.Length} English " +
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
            if (Current == ShapeKeyDecimatorLanguage.English || string.IsNullOrEmpty(english)) return english;
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
                DrawLanguageButton("English", ShapeKeyDecimatorLanguage.English);
                DrawLanguageButton("日本語", ShapeKeyDecimatorLanguage.Japanese);
            }
        }

        private static void DrawLanguageButton(string label, ShapeKeyDecimatorLanguage language)
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
