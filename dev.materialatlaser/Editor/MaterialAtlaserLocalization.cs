using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEditorInternal;
using UnityEngine;

namespace MaterialAtlaser.Editors
{
    public enum MaterialAtlaserLanguage
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
    /// Only prose is translated. Mesh/material/renderer names, numbers, file paths and the tool's own
    /// name stay as they are. Mirrors dev.shapekeydecimator's ShapeKeyDecimatorLocalization pattern.
    /// </summary>
    public static class MaterialAtlaserLocalization
    {
        /// <summary>Stored in EditorPrefs, so the choice is global to the editor, not per scene or per component.</summary>
        private const string PreferenceKey = "MaterialAtlaser.UiLanguage";

        // ------------------------------------------------------------------ English (source)

        public static readonly string[] EnglishStrings =
        {
            // Sections
            "Scan",
            "Atlas",
            "Merge",

            // Fields
            "Ignore Regular Meshes",
            "When enabled, plain (non-skinned) MeshRenderers under this GameObject are left untouched and keep their original materials. Disable to fold them into the atlas as well.",
            "Atlas Size",
            "Maximum size (in pixels) of each generated atlas texture. Materials are packed as densely as they fit; if there isn't enough room they're shrunk to fit rather than overflowing.",
            "Atlas Count",
            "How many atlas textures/materials to split the scanned materials across. 1 means every material ends up on a single shared material (one material slot), even across different shaders - a transparent/cutout material grouped with an opaque one will render opaque from then on. Raising this spreads materials over more atlases - and more material slots - to keep texture detail (and shader features) higher when there are a lot of very different materials.",
            "Merge Skinned Meshes And Material Slots",
            "After atlasing, combine every affected SkinnedMeshRenderer into a single SkinnedMeshRenderer sharing one merged mesh, instead of leaving the renderers separate for d4rkAvatarOptimizer (or another tool) to merge afterwards. Turn this on if d4rkAvatarOptimizer isn't picking up the merge on its own - it looks for renderers/materials that are already trivially mergeable, and this does that merge directly rather than hoping it recognizes the result. Only SkinnedMeshRenderers are combined; plain MeshRenderers (even if atlased because 'Ignore Regular Meshes' is off) are left separate. Renderers that rely on being toggled independently (e.g. via animated active-state) should not be merged, since they'd lose that independence.",
            "Merged Mesh Name",
            "When meshes are actually merged (2+ skinned meshes with 'Merge Skinned Meshes And Material Slots' on), this does one of two things. If it matches one of the included skinned meshes by name, merging targets that renderer directly - it survives as-is instead of whichever renderer happened to be scanned first, so anything with a direct reference to it (most importantly VRCAvatarDescriptor's eyelids/viseme 'Body' mesh) keeps working. Otherwise it's just the merged mesh's name (and also renames the surviving renderer's GameObject to match, so it shows up in the Hierarchy, not just the Mesh field in its Inspector). Leave blank to default to the first included skinned mesh's name plus \" (Merged)\", with nothing renamed.",

            // Merge target preview
            "Merged mesh will be named \"{0}\".",
            "Merging into \"{0}\" - it survives as-is, so anything referencing it directly (like the Avatar Descriptor's eyelids/viseme mesh) keeps working.",
            "No included skinned mesh named \"{0}\" - will merge into \"{1}\" instead and name the result \"{0}\".",

            // Skinned mesh list
            "Skinned Meshes ({0}/{1} included)",
            "No SkinnedMeshRenderers with a mesh assigned were found under this GameObject.",
            "Include All",
            "Exclude All",

            // Bake section
            "Atlasing/merging normally happens during the build (Play mode or avatar upload) and never touches your original assets. This button does the same work right now instead, on a duplicate, and saves the result as real project assets you can inspect.",
            "Create Optimized Copy In Hierarchy",
            "No included skinned meshes to atlas - check the list above.",
            "Atlasing and merging…",
            "Created '{0}' with {1} mesh(es) and {2} material slot(s) total.\n\nMeshes and materials saved to {3}.",
            "OK"
        };

        // ------------------------------------------------------------------ 日本語 (index aligned)

        public static readonly string[] JapaneseStrings =
        {
            // セクション
            "スキャン",
            "アトラス",
            "マージ",

            // フィールド
            "通常のメッシュを無視",
            "有効にすると、このGameObject以下の通常の(スキンなし)MeshRendererには触れず、元のマテリアルのままにします。無効にするとアトラスに含めます。",
            "アトラスサイズ",
            "生成する各アトラステクスチャの最大サイズ(ピクセル)。マテリアルはできるだけ密に詰め込まれ、収まりきらない場合ははみ出さずに縮小されます。",
            "アトラス数",
            "スキャンしたマテリアルをいくつのアトラステクスチャ/マテリアルに分割するか。1にすると、シェーダーが異なっていてもすべてのマテリアルが1つの共有マテリアル(1マテリアルスロット)にまとまります - 不透明なテンプレートと同じグループになった透明/カットアウトのマテリアルは、以降不透明として描画されます。値を増やすとより多くのアトラス(とマテリアルスロット)に分散され、性質の大きく異なるマテリアルが多い場合にテクスチャの精細さ(とシェーダー機能)を保ちやすくなります。",
            "スキンドメッシュとマテリアルスロットをマージ",
            "アトラス化の後、影響を受けたすべてのSkinnedMeshRendererを1つのSkinnedMeshRendererと1つの結合メッシュにまとめます。d4rkAvatarOptimizer(または他のツール)が後でマージするのを待つ代わりに直接マージします。d4rkAvatarOptimizerが自動でマージを検出しない場合はこれを有効にしてください - d4rkは既に自明にマージ可能なレンダラー/マテリアルを探すため、結果を認識してくれるとは限りません。マージされるのはSkinnedMeshRendererのみで、通常のMeshRenderer(「通常のメッシュを無視」がオフでアトラス化されたものも含む)は分離されたままです。個別にオン/オフを切り替えたいレンダラー(アニメーションでのアクティブ状態切り替えなど)は、その独立性が失われるためマージすべきではありません。",
            "結合メッシュの名前",
            "実際にメッシュがマージされる場合(2つ以上のスキンドメッシュがあり「スキンドメッシュとマテリアルスロットをマージ」がオン)、この項目は2通りの働きをします。含まれているスキンドメッシュのいずれかと名前が一致する場合、そのレンダラーを直接マージ先にします - 最初にスキャンされたレンダラーではなく、その名前のレンダラー自体がそのまま生き残るため、それを直接参照している何か(特にVRCAvatarDescriptorのまぶた/リップシンク用「Body」メッシュ参照)が壊れずに済みます。一致しない場合は単に結合メッシュの名前になります(あわせて生き残ったレンダラーのGameObject名もこれに変更されるため、Inspectorの「Mesh」欄だけでなくHierarchy上でも確認できます)。空欄のままにすると、含まれる最初のスキンドメッシュの名前に \" (Merged)\" を付けたものがデフォルトになり、名前の変更は行われません。",

            // マージ先プレビュー
            "結合メッシュの名前は \"{0}\" になります。",
            "\"{0}\" にマージします - このレンダラーはそのまま残るため、これを直接参照しているもの(Avatar Descriptorのまぶた/リップシンク用メッシュなど)は動作し続けます。",
            "\"{0}\" という名前のスキンドメッシュは含まれていません - 代わりに \"{1}\" にマージし、結果に \"{0}\" という名前を付けます。",

            // スキンドメッシュ一覧
            "スキンドメッシュ ({0}/{1} 件を含む)",
            "このGameObject以下にメッシュが設定されたSkinnedMeshRendererが見つかりませんでした。",
            "すべて含める",
            "すべて除外",

            // ベイクセクション
            "アトラス化・マージは通常ビルド時(Playモードまたはアバターのアップロード時)に行われ、元のアセットには触れません。このボタンは複製に対して同じ処理を今すぐ実行し、結果を確認可能な実プロジェクトアセットとして保存します。",
            "最適化済みコピーをヒエラルキーに作成",
            "アトラス化対象として含まれているスキンドメッシュがありません - 上のリストを確認してください。",
            "アトラス化・マージ中…",
            "'{0}' を作成しました(合計 {1} メッシュ、{2} マテリアルスロット)。\n\nメッシュとマテリアルは {3} に保存されました。",
            "OK"
        };

        // ------------------------------------------------------------------ current language

        private static MaterialAtlaserLanguage? _current;
        private static Dictionary<string, string> _lookup;

        public static MaterialAtlaserLanguage Current
        {
            get
            {
                if (_current == null)
                {
                    var stored = EditorPrefs.GetString(PreferenceKey, string.Empty);
                    if (stored == "ja") _current = MaterialAtlaserLanguage.Japanese;
                    else if (stored == "en") _current = MaterialAtlaserLanguage.English;
                    else _current = DetectDefaultLanguage();
                }
                return _current.Value;
            }
            set
            {
                if (_current == value) return;
                _current = value;
                EditorPrefs.SetString(PreferenceKey, value == MaterialAtlaserLanguage.Japanese ? "ja" : "en");

                // Every inspector showing this component has to follow the change, not just the one
                // that was clicked.
                InternalEditorUtility.RepaintAllViews();
            }
        }

        /// <summary>
        /// Japanese when the machine is on Japan time, English otherwise. Note that the UTC+9 check
        /// also matches Korea, which is the intended trade for not needing a full locale table.
        /// </summary>
        private static MaterialAtlaserLanguage DetectDefaultLanguage()
        {
            try
            {
                var zone = TimeZoneInfo.Local;
                var id = zone.Id ?? string.Empty;

                if (id.IndexOf("Tokyo", StringComparison.OrdinalIgnoreCase) >= 0 ||
                    id.IndexOf("Japan", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    return MaterialAtlaserLanguage.Japanese;
                }

                // JST is UTC+9 year round, no daylight saving.
                if (zone.BaseUtcOffset == TimeSpan.FromHours(9))
                {
                    return MaterialAtlaserLanguage.Japanese;
                }
            }
            catch (Exception)
            {
                // Some platforms refuse to report a time zone; English is the safe fallback.
            }

            return MaterialAtlaserLanguage.English;
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
                        $"[Material Atlaser] Translation tables are out of step: {EnglishStrings.Length} English " +
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
            if (Current == MaterialAtlaserLanguage.English || string.IsNullOrEmpty(english)) return english;
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
                DrawLanguageButton("English", MaterialAtlaserLanguage.English);
                DrawLanguageButton("日本語", MaterialAtlaserLanguage.Japanese);
            }
        }

        private static void DrawLanguageButton(string label, MaterialAtlaserLanguage language)
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
