using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Reflection;
using MateEngine.AIVoiceMod;
using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;

public static class MateEngineAIVoiceMenuBuilder
{
    private sealed class PageParts
    {
        public GameObject root;
        public RectTransform content;
        public ScrollRect scroll;
    }
    private const string AssetRoot = "Assets/AIVoiceMod";
    private const string TemplateRoot = "Assets/AIVoiceModTemplate";
    private const string PrefabPath = AssetRoot + "/MateEngineAIVoice.prefab";
    private static readonly Color32 Panel = new Color32(20, 22, 32, 245);
    private static readonly Color32 Card = new Color32(42, 45, 62, 235);
    private static readonly Color32 Accent = new Color32(150, 105, 255, 255);
    private static readonly Color32 Text = new Color32(239, 238, 248, 255);
    private static readonly Color32 Muted = new Color32(177, 177, 196, 255);

    [MenuItem("MateEngine/Build AI Voice Mod Menu")]
    public static void Build()
    {
        Directory.CreateDirectory(AssetRoot);
        var root = BuildPrefabObject();
        PrefabUtility.SaveAsPrefabAsset(root, PrefabPath);
        UnityEngine.Object.DestroyImmediate(root);
        AssetDatabase.SaveAssets();
        ValidatePrefab();
        ExportMe();
    }

    private static GameObject BuildPrefabObject()
    {
        var root = new GameObject("MateEngine AI + Voice");
        var controller = root.AddComponent<MateEngineSettingsPanel>();

        var canvasObject = new GameObject("AI Voice Settings", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
        canvasObject.transform.SetParent(root.transform, false);
        // TMP_Dropdown creates its popup canvas at sorting order 30000. Keep the
        // panel below that so the popup and its click-blocker sit above the glass UI.
        var canvas = canvasObject.GetComponent<Canvas>(); canvas.renderMode = RenderMode.ScreenSpaceOverlay; canvas.sortingOrder = 25000;
        // Keep the UI the same readable relative size across 1080p, 1440p, and 4K.
        // Windows desktop scaling does not affect a ScreenSpaceOverlay canvas.
        var scaler = canvasObject.GetComponent<CanvasScaler>(); scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize; scaler.referenceResolution = new Vector2(1920, 1080); scaler.matchWidthOrHeight = 0.5f;
        Stretch(canvasObject.GetComponent<RectTransform>());
        controller.canvas = canvas;

        var shade = NewImage("Shade", canvasObject.transform, new Color32(4, 5, 10, 170)); Stretch(shade.rectTransform);
        var panel = NewImage("Glass Panel", shade.transform, Panel); SetRect(panel.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(420, 460), Vector2.zero); controller.panelRoot = panel.rectTransform;

        var title = NewText("Title", panel.transform, "MATE ENGINE  /  AI + VOICE", 16, FontStyles.Bold, Text);
        SetRect(title.rectTransform, new Vector2(0.5f, 1f), new Vector2(350, 28), new Vector2(0, -14));

        var close = Primitive<Button>("Button.prefab", panel.transform, "Close"); SetButtonText(close, "×"); CompactText(close.GetComponentInChildren<TMP_Text>(true), 13, false); SetRect(close.GetComponent<RectTransform>(), new Vector2(1f, 1f), new Vector2(28, 26), new Vector2(-13, -13)); controller.closeButton = close;

        var tabs = Row(panel.transform, 26); SetRect(tabs, new Vector2(0.5f, 1f), new Vector2(390, 26), new Vector2(0, -42));
        controller.llmTabButton = Button(tabs, "LLM", 1); controller.characterTabButton = Button(tabs, "Character", 1); controller.fishTabButton = Button(tabs, "Fish Speech", 1); controller.lipSyncTabButton = Button(tabs, "Lip Sync", 1);

        var llm = CreatePage(panel.transform, "LLM Page"); var character = CreatePage(panel.transform, "Character Page"); var fish = CreatePage(panel.transform, "Fish Speech Page"); var lipSync = CreatePage(panel.transform, "Lip Sync Page");
        controller.llmPage = llm.root; controller.llmScroll = llm.scroll; controller.characterPage = character.root; controller.characterScroll = character.scroll;
        controller.fishPage = fish.root; controller.fishScroll = fish.scroll; controller.lipSyncPage = lipSync.root; controller.lipSyncScroll = lipSync.scroll;
        var llmContent = llm.content; var characterContent = character.content; var fishContent = fish.content; var lipSyncContent = lipSync.content;

        Header(llmContent, "API KEYS (BYOK - SAVED LOCALLY)");
        controller.openRouterKeyInput = LabeledInput(llmContent, "OpenRouter", "OpenRouter API key", true);
        controller.vercelKeyInput = LabeledInput(llmContent, "Vercel AI Gateway", "Vercel AI Gateway key", true);
        controller.fishKeyInput = LabeledInput(llmContent, "Fish Audio", "Fish Audio API key", true);

        Header(llmContent, "LANGUAGE MODEL");
        controller.providerDropdown = Dropdown(llmContent, "Provider");
        var modelRow = Row(llmContent, 34); controller.modelDropdown = Dropdown(modelRow, "Language model", 3); controller.refreshModelsButton = Button(modelRow, "Refresh", 1);
        controller.routingDropdown = Dropdown(llmContent, "Provider routing");
        controller.pinnedProvidersInput = Input(llmContent, "Pinned provider slugs, comma separated", false, 30);
        controller.allowFallbacksToggle = ToggleField(llmContent, "Allow provider fallbacks");
        controller.refreshEndpointsButton = Button(llmContent, "Inspect Vercel Endpoints");
        controller.replyLengthDropdown = Dropdown(llmContent, "Reply length");
        controller.temperatureSlider = SliderField(llmContent, "Temperature", 0, 2, 0.95f, out controller.temperatureValueText);
        controller.maxTokensSlider = SliderField(llmContent, "Max Output Tokens", 80, 4000, 920, out controller.maxTokensValueText, true);
        controller.autoSpeakToggle = ToggleField(llmContent, "Auto Speak");
        controller.runtimeSituationInput = Input(llmContent, "Runtime situation / extra system context", false, 34);

        Header(characterContent, "CHARACTER + PERSONALITY");
        controller.personaDropdown = Dropdown(characterContent, "Active character");
        controller.personaNameInput = Input(characterContent, "Character name", false, 30);
        controller.personaDescriptionInput = Input(characterContent, "Short character description", false, 34);
        controller.personaPromptInput = Input(characterContent, "Personality / system prompt", false, 120, true);
        controller.userNicknameInput = Input(characterContent, "How she should address you", false, 30);
        var personaRow = Row(characterContent, 30); controller.newPersonaButton = Button(personaRow, "New", 1); controller.deletePersonaButton = Button(personaRow, "Delete", 1); controller.assignVoiceButton = Button(personaRow, "Assign Voice", 2);

        Header(fishContent, "FISH SPEECH LIVE");
        controller.remoteTtsModeDropdown = Dropdown(fishContent, "Remote TTS pacing");
        controller.fishVoiceScopeDropdown = Dropdown(fishContent, "Voice catalog");
        var voiceRow = Row(fishContent, 36); controller.voiceDropdown = Dropdown(voiceRow, "Fish voice", 3); controller.fetchMyVoicesButton = Button(voiceRow, "Mine", 1); controller.fetchPublicVoicesButton = Button(voiceRow, "Public", 1);
        controller.voiceIdInput = Input(fishContent, "Fish reference_id; blank uses default", false, 30);
        controller.fishTransportDropdown = Dropdown(fishContent, "Transport");
        controller.fishFormatDropdown = Dropdown(fishContent, "Audio format");
        controller.fishSampleRateDropdown = Dropdown(fishContent, "PCM sample rate");
        controller.fishModelDropdown = Dropdown(fishContent, "Synthesis model");
        controller.fishLatencyDropdown = Dropdown(fishContent, "Latency / quality");
        controller.conditionPreviousToggle = ToggleField(fishContent, "Condition Previous Chunks");
        controller.chunkStrategyDropdown = Dropdown(fishContent, "Live chunking");
        controller.chunkLengthSlider = SliderField(fishContent, "Fish Chunk", 100, 300, 160, out controller.chunkLengthValueText, true);
        controller.speechSpeedSlider = SliderField(fishContent, "Generation Speed", 0.5f, 2, 1, out controller.speechSpeedValueText);
        controller.ttsVolumeSlider = SliderField(fishContent, "Playback Volume", 0, 2, 1, out controller.ttsVolumeValueText);

        Header(lipSyncContent, "AUDIO-DRIVEN LIP SYNC");
        controller.lipSyncModeDropdown = Dropdown(lipSyncContent, "Lip Sync Mode");
        Hint(lipSyncContent, "Hybrid shapes wLipSync with volume, frequency bands, and smoothing. Direct feeds raw A/I/U/E/O weights for A/B comparison.");
        controller.lipSyncSmoothingSlider = SliderField(lipSyncContent, "Smoothing", 0, 0.9f, 0.44f, out controller.lipSyncSmoothingValueText);
        controller.lipSyncGainSlider = SliderField(lipSyncContent, "Mouth Gain", 0.1f, 2, 1, out controller.lipSyncGainValueText);
        controller.lipSyncVolumeInfluenceSlider = SliderField(lipSyncContent, "Volume Influence", 0, 2, 1, out controller.lipSyncVolumeInfluenceValueText);
        Hint(lipSyncContent, "Higher smoothing is steadier. Gain controls mouth opening. Volume Influence below 1 evens loudness; above 1 exaggerates contrast. Defaults: 0.44 / 1.00 / 1.00.");

        controller.statusText = NewText("Status", panel.transform, "Ready. Settings stored locally.", 10, FontStyles.Normal, Muted);
        SetRect(controller.statusText.rectTransform, new Vector2(0.5f, 0f), new Vector2(176, 28), new Vector2(-112, 15)); controller.statusText.alignment = TextAlignmentOptions.MidlineLeft;
        var scaleRow = Row(panel.transform, 28); SetRect(scaleRow, new Vector2(0f, 0f), new Vector2(102, 28), new Vector2(208, 15));
        controller.uiScaleDownButton = Button(scaleRow, "−", 1);
        controller.uiScaleText = NewText("UI Scale", scaleRow, "100%", 10, FontStyles.Bold, Text); Layout(controller.uiScaleText.gameObject, 28, 1.4f); controller.uiScaleText.alignment = TextAlignmentOptions.Center;
        controller.uiScaleUpButton = Button(scaleRow, "+", 1);
        controller.saveButton = Primitive<Button>("Button.prefab", panel.transform, "Save"); SetButtonText(controller.saveButton, "Save"); CompactText(controller.saveButton.GetComponentInChildren<TMP_Text>(true), 11, false); SetRect(controller.saveButton.GetComponent<RectTransform>(), new Vector2(1f, 0f), new Vector2(82, 28), new Vector2(-16, 15));
        return root;
    }

    private static void ExportMe()
    {
        var importer = AssetImporter.GetAtPath(PrefabPath); importer.assetBundleName = "mateengine-aivoice.bundle";
        var buildRoot = Path.GetFullPath(Path.Combine(Application.dataPath, "../TempAIVoiceBuild"));
        if (Directory.Exists(buildRoot)) Directory.Delete(buildRoot, true); Directory.CreateDirectory(buildRoot);
        BuildPipeline.BuildAssetBundles(buildRoot, BuildAssetBundleOptions.None, BuildTarget.StandaloneWindows64);
        var packageRoot = Path.Combine(buildRoot, "Package"); Directory.CreateDirectory(packageRoot);
        foreach (var name in new[] { "mateengine-aivoice.bundle", "mateengine-aivoice.bundle.manifest" })
        {
            var source = Path.Combine(buildRoot, name);
            if (!File.Exists(source)) throw new FileNotFoundException("Asset bundle build did not produce " + name, source);
            File.Copy(source, Path.Combine(packageRoot, name), true);
        }
        File.WriteAllText(Path.Combine(packageRoot, "modinfo.json"), "{\n  \"name\": \"Mate Engine AI + Voice\",\n  \"author\": \"Mate Engine community\",\n  \"description\": \"Native OpenRouter, Vercel AI Gateway, Fish Speech, personality, and audio lip sync.\",\n  \"buildTarget\": \"StandaloneWindows64\"\n}");
        File.WriteAllText(Path.Combine(packageRoot, "mod_type.json"), "{\"type\":\"Mod\"}");
        var configuredRoot = Environment.GetEnvironmentVariable("MATEENGINE_AI_VOICE_MOD_ROOT");
        var modRoot = string.IsNullOrWhiteSpace(configuredRoot) ? Path.GetFullPath(Path.Combine(Application.dataPath, "../../../MateEngine-AI-Voice-Mod")) : Path.GetFullPath(configuredRoot);
        var dist = Path.Combine(modRoot, "dist"); Directory.CreateDirectory(dist);
        var output = Path.Combine(dist, "MateEngine AI Voice.me"); if (File.Exists(output)) File.Delete(output); ZipFile.CreateFromDirectory(packageRoot, output);
        AssetDatabase.RemoveAssetBundleName(importer.assetBundleName, true); Directory.Delete(buildRoot, true); AssetDatabase.SaveAssets(); AssetDatabase.Refresh();
        Debug.Log("[MateEngineAIVoice] Built " + output);
    }

    private static void ValidatePrefab()
    {
        var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);
        if (prefab == null) throw new InvalidOperationException("AI Voice prefab was not saved.");
        var panel = prefab.GetComponent<MateEngineSettingsPanel>();
        if (panel == null) throw new InvalidOperationException("AI Voice prefab is missing MateEngineSettingsPanel.");
        var missing = typeof(MateEngineSettingsPanel)
            .GetFields(BindingFlags.Instance | BindingFlags.Public)
            .Where(field => typeof(UnityEngine.Object).IsAssignableFrom(field.FieldType) && field.GetValue(panel) == null)
            .Select(field => field.Name)
            .ToArray();
        if (missing.Length > 0) throw new InvalidOperationException("AI Voice prefab has unassigned controls: " + string.Join(", ", missing));
        Debug.Log("[MateEngineAIVoice] Validated prefab controls: " + typeof(MateEngineSettingsPanel).GetFields(BindingFlags.Instance | BindingFlags.Public).Count(field => typeof(UnityEngine.Object).IsAssignableFrom(field.FieldType)));
    }

    private static void Header(Transform parent, string value)
    {
        var card = NewImage(value, parent, Card); Layout(card.gameObject, 26); var text = NewText("Text", card.transform, value, 11, FontStyles.Bold, Text); Stretch(text.rectTransform, 8, 8, 3, 3); text.alignment = TextAlignmentOptions.MidlineLeft;
    }
    private static PageParts CreatePage(Transform parent, string name)
    {
        var viewport = NewImage(name, parent, new Color32(0, 0, 0, 0)); SetRect(viewport.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(390, 340), new Vector2(0, -4)); viewport.gameObject.AddComponent<RectMask2D>();
        var contentObject = new GameObject("Content", typeof(RectTransform), typeof(VerticalLayoutGroup), typeof(ContentSizeFitter)); contentObject.transform.SetParent(viewport.transform, false);
        var content = contentObject.GetComponent<RectTransform>(); content.anchorMin = new Vector2(0, 1); content.anchorMax = new Vector2(1, 1); content.pivot = new Vector2(0.5f, 1); content.sizeDelta = Vector2.zero;
        var layout = contentObject.GetComponent<VerticalLayoutGroup>(); layout.padding = new RectOffset(4, 4, 3, 7); layout.spacing = 3; layout.childControlHeight = true; layout.childControlWidth = true; layout.childForceExpandHeight = false; layout.childForceExpandWidth = true;
        contentObject.GetComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;
        var scroll = viewport.gameObject.AddComponent<ScrollRect>(); scroll.viewport = viewport.rectTransform; scroll.content = content; scroll.horizontal = false; scroll.vertical = true; scroll.movementType = ScrollRect.MovementType.Clamped; scroll.scrollSensitivity = 32;
        return new PageParts { root = viewport.gameObject, content = content, scroll = scroll };
    }
    private static TMP_InputField Input(Transform parent, string placeholder, bool password, float height, bool multiline = false)
    {
        var field = Primitive<TMP_InputField>("Input.prefab", parent, placeholder); Layout(field.gameObject, height); field.contentType = password ? TMP_InputField.ContentType.Password : TMP_InputField.ContentType.Standard;
        field.lineType = multiline ? TMP_InputField.LineType.MultiLineNewline : TMP_InputField.LineType.SingleLine; CompactText(field.textComponent, 11, multiline); if (field.placeholder is TMP_Text text) { text.text = placeholder; CompactText(text, 10, multiline); } return field;
    }
    private static TMP_InputField LabeledInput(Transform parent, string label, string placeholder, bool password)
    {
        var row = Row(parent, 32); var text = NewText("Label", row, label, 10, FontStyles.Normal, Muted); Layout(text.gameObject, 28, 1);
        var input = Input(row, placeholder, password, 28); Layout(input.gameObject, 28, 2.45f); return input;
    }
    private static TMP_Dropdown Dropdown(Transform parent, string label, float flexible = 1)
    {
        var row = Row(parent, 32); var text = NewText("Label", row, label, 10, FontStyles.Normal, Muted); Layout(text.gameObject, 28, 1); var dropdown = Primitive<TMP_Dropdown>("Dropdown.prefab", row, label); Layout(dropdown.gameObject, 28, flexible); foreach (var item in dropdown.GetComponentsInChildren<TMP_Text>(true)) CompactText(item, 11, false); return dropdown;
    }
    private static Button Button(Transform parent, string text, float flexible = 1)
    {
        var button = Primitive<Button>("Button.prefab", parent, text); Layout(button.gameObject, 28, flexible); SetButtonText(button, text); var label = button.GetComponentInChildren<TMP_Text>(true); if (label != null) CompactText(label, 11, false); return button;
    }
    private static Toggle ToggleField(Transform parent, string text)
    {
        var toggle = Primitive<Toggle>("Toggle.prefab", parent, text); Layout(toggle.gameObject, 26); var label = toggle.GetComponentInChildren<TMP_Text>(true); if (label != null) { label.text = text; CompactText(label, 10, false); } return toggle;
    }
    private static Slider SliderField(Transform parent, string label, float min, float max, float value, out TMP_Text valueText, bool whole = false)
    {
        var row = Row(parent, 28); var text = NewText("Label", row, label, 10, FontStyles.Normal, Muted); Layout(text.gameObject, 24, 1);
        var root = new GameObject(label + " Slider", typeof(RectTransform), typeof(Slider), typeof(LayoutElement)); root.transform.SetParent(row, false); Layout(root, 24, 2);
        var background = NewImage("Background", root.transform, new Color32(70, 72, 92, 255)); SetRect(background.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0, 8), Vector2.zero, true, 12, 12);
        var fillArea = new GameObject("Fill Area", typeof(RectTransform)); fillArea.transform.SetParent(root.transform, false); Stretch(fillArea.GetComponent<RectTransform>(), 12, 12, 0, 0);
        var fill = NewImage("Fill", fillArea.transform, Accent); Stretch(fill.rectTransform);
        var handleArea = new GameObject("Handle Slide Area", typeof(RectTransform)); handleArea.transform.SetParent(root.transform, false); Stretch(handleArea.GetComponent<RectTransform>(), 12, 12, 0, 0);
        var handle = NewImage("Handle", handleArea.transform, Text); SetRect(handle.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(13, 13), Vector2.zero);
        var slider = root.GetComponent<Slider>(); slider.fillRect = fill.rectTransform; slider.handleRect = handle.rectTransform; slider.targetGraphic = handle; slider.minValue = min; slider.maxValue = max; slider.value = value; slider.wholeNumbers = whole;
        valueText = NewText("Value", row, whole ? Mathf.RoundToInt(value).ToString() : value.ToString("0.00"), 10, FontStyles.Bold, Text); Layout(valueText.gameObject, 24, 0.45f); valueText.alignment = TextAlignmentOptions.MidlineRight;
        return slider;
    }
    private static void Hint(Transform parent, string value)
    {
        var text = NewText("Hint", parent, value, 9, FontStyles.Normal, Muted); Layout(text.gameObject, 36); text.enableWordWrapping = true; text.overflowMode = TextOverflowModes.Truncate; text.margin = new Vector4(6, 2, 6, 2); text.alignment = TextAlignmentOptions.TopLeft;
    }
    private static RectTransform Row(Transform parent, float height)
    {
        var row = new GameObject("Row", typeof(RectTransform), typeof(HorizontalLayoutGroup), typeof(LayoutElement)); row.transform.SetParent(parent, false); Layout(row, height);
        var layout = row.GetComponent<HorizontalLayoutGroup>(); layout.spacing = 4; layout.childControlHeight = true; layout.childControlWidth = true; layout.childForceExpandHeight = true; layout.childForceExpandWidth = true; return row.GetComponent<RectTransform>();
    }
    private static T Primitive<T>(string prefabName, Transform parent, string name) where T : Component
    {
        var path = TemplateRoot + "/" + prefabName; var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
        if (prefab == null) throw new FileNotFoundException("Missing UI primitive", path);
        var instance = (GameObject)PrefabUtility.InstantiatePrefab(prefab); instance.name = name; instance.transform.SetParent(parent, false);
        var component = instance.GetComponent<T>() ?? instance.GetComponentInChildren<T>(true); if (component == null) throw new MissingComponentException(prefabName + " does not contain " + typeof(T).Name); return component;
    }
    private static Image NewImage(string name, Transform parent, Color color) { var go = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer), typeof(Image)); go.transform.SetParent(parent, false); var image = go.GetComponent<Image>(); image.color = color; return image; }
    private static TextMeshProUGUI NewText(string name, Transform parent, string value, float size, FontStyles style, Color color)
    {
        var go = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer), typeof(TextMeshProUGUI)); go.transform.SetParent(parent, false); var text = go.GetComponent<TextMeshProUGUI>(); text.text = value; text.fontSize = size; text.fontStyle = style; text.color = color; text.enableWordWrapping = false; text.overflowMode = TextOverflowModes.Ellipsis; text.raycastTarget = false; return text;
    }
    private static void CompactText(TMP_Text text, float maxSize, bool multiline)
    {
        if (text == null) return; text.enableAutoSizing = true; text.fontSizeMax = maxSize; text.fontSizeMin = Mathf.Max(7, maxSize - 3); text.enableWordWrapping = multiline; text.overflowMode = multiline ? TextOverflowModes.Truncate : TextOverflowModes.Ellipsis;
    }
    private static void SetButtonText(Button button, string value) { var text = button.GetComponentInChildren<TMP_Text>(true); if (text != null) text.text = value; }
    private static void Layout(GameObject value, float height, float flexible = 0) { var element = value.GetComponent<LayoutElement>() ?? value.AddComponent<LayoutElement>(); element.preferredHeight = height; element.flexibleWidth = flexible; }
    private static void Stretch(RectTransform value, float left = 0, float right = 0, float top = 0, float bottom = 0) { value.anchorMin = Vector2.zero; value.anchorMax = Vector2.one; value.offsetMin = new Vector2(left, bottom); value.offsetMax = new Vector2(-right, -top); }
    private static void SetRect(RectTransform value, Vector2 anchor, Vector2 size, Vector2 position, bool stretchX = false, float left = 0, float right = 0)
    {
        value.anchorMin = stretchX ? new Vector2(0, anchor.y) : anchor; value.anchorMax = stretchX ? new Vector2(1, anchor.y) : anchor; value.pivot = anchor; value.sizeDelta = stretchX ? new Vector2(-(left + right), size.y) : size; value.anchoredPosition = position;
    }
}
