using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;

namespace MateEngine.AIVoiceMod
{
    [DisallowMultipleComponent]
    public sealed class MateEngineSettingsPanel : MonoBehaviour
    {
        [Header("Navigation")]
        public Canvas canvas;
        public Button closeButton;
        public Button saveButton;
        public TMP_Text statusText;
        public Button llmTabButton;
        public Button characterTabButton;
        public Button fishTabButton;
        public Button lipSyncTabButton;
        public GameObject llmPage;
        public GameObject characterPage;
        public GameObject fishPage;
        public GameObject lipSyncPage;
        public ScrollRect llmScroll;
        public ScrollRect characterScroll;
        public ScrollRect fishScroll;
        public ScrollRect lipSyncScroll;

        [Header("LLM and BYOK")]
        public TMP_Dropdown providerDropdown;
        public TMP_InputField openRouterKeyInput;
        public TMP_InputField vercelKeyInput;
        public TMP_InputField fishKeyInput;
        public TMP_Dropdown modelDropdown;
        public Button refreshModelsButton;
        public TMP_Dropdown routingDropdown;
        public TMP_InputField pinnedProvidersInput;
        public Toggle allowFallbacksToggle;
        public Button refreshEndpointsButton;
        public TMP_Dropdown replyLengthDropdown;
        public Slider temperatureSlider;
        public TMP_Text temperatureValueText;
        public Slider maxTokensSlider;
        public TMP_Text maxTokensValueText;
        public Toggle autoSpeakToggle;
        public TMP_InputField runtimeSituationInput;

        [Header("Character")]
        public TMP_Dropdown personaDropdown;
        public TMP_InputField personaNameInput;
        public TMP_InputField personaDescriptionInput;
        public TMP_InputField personaPromptInput;
        public TMP_InputField userNicknameInput;
        public Button newPersonaButton;
        public Button deletePersonaButton;
        public Button assignVoiceButton;

        [Header("Fish Speech")]
        public TMP_Dropdown voiceDropdown;
        public TMP_InputField voiceIdInput;
        public Button fetchMyVoicesButton;
        public Button fetchPublicVoicesButton;
        public TMP_Dropdown fishModelDropdown;
        public TMP_Dropdown fishLatencyDropdown;
        public TMP_Dropdown remoteTtsModeDropdown;
        public TMP_Dropdown fishVoiceScopeDropdown;
        public TMP_Dropdown fishTransportDropdown;
        public TMP_Dropdown fishFormatDropdown;
        public TMP_Dropdown fishSampleRateDropdown;
        public Toggle conditionPreviousToggle;
        public TMP_Dropdown chunkStrategyDropdown;
        public Slider chunkLengthSlider;
        public TMP_Text chunkLengthValueText;
        public Slider speechSpeedSlider;
        public TMP_Text speechSpeedValueText;
        public Slider ttsVolumeSlider;
        public TMP_Text ttsVolumeValueText;

        [Header("Lip Sync")]
        public TMP_Dropdown lipSyncModeDropdown;
        public Slider lipSyncSmoothingSlider;
        public TMP_Text lipSyncSmoothingValueText;
        public Slider lipSyncGainSlider;
        public TMP_Text lipSyncGainValueText;
        public Slider lipSyncVolumeInfluenceSlider;
        public TMP_Text lipSyncVolumeInfluenceValueText;

        private MateEngineModRuntime runtime;
        private readonly ModelCatalogClient catalogs = new ModelCatalogClient();
        private readonly FishVoiceCatalogClient voices = new FishVoiceCatalogClient();
        private IList<FishVoiceInfo> voiceItems = new List<FishVoiceInfo>();
        private CancellationTokenSource requests;
        private MenuActions gameMenuActions;
        private MenuEntry menuEntry;
        private bool menuRegistered;

        private void Start()
        {
            // Unity's runtime-initialize method table is baked when the player is built, so a
            // DLL injected into the Steam player must also be bootstrapped by the loaded .me prefab.
            runtime = MateEngineModRuntime.EnsureStarted();
            requests = new CancellationTokenSource();
            WireEvents();
            Populate(runtime.Settings);
            Canvas.ForceUpdateCanvases();
            ShowTab(0);
            gameMenuActions = FindFirstObjectByType<MenuActions>(FindObjectsInactive.Include);
            menuEntry = new MenuEntry { menu = canvas.gameObject };
            RegisterMenu();
        }

        private void WireEvents()
        {
            saveButton.onClick.AddListener(SaveAndApply);
            closeButton.onClick.AddListener(() => { canvas.gameObject.SetActive(false); UnregisterMenu(); });
            refreshModelsButton.onClick.AddListener(RefreshModels);
            refreshEndpointsButton.onClick.AddListener(RefreshEndpoints);
            fetchMyVoicesButton.onClick.AddListener(() => RefreshVoices(true));
            fetchPublicVoicesButton.onClick.AddListener(() => RefreshVoices(false));
            newPersonaButton.onClick.AddListener(NewPersona);
            deletePersonaButton.onClick.AddListener(DeletePersona);
            assignVoiceButton.onClick.AddListener(AssignVoice);
            llmTabButton.onClick.AddListener(() => ShowTab(0));
            characterTabButton.onClick.AddListener(() => ShowTab(1));
            fishTabButton.onClick.AddListener(() => ShowTab(2));
            lipSyncTabButton.onClick.AddListener(() => ShowTab(3));
            personaDropdown.onValueChanged.AddListener(_ => LoadPersona(runtime.Settings.ActivePersona));
            providerDropdown.onValueChanged.AddListener(_ => PopulateRouting(runtime.Settings));
            voiceDropdown.onValueChanged.AddListener(index => { if (index > 0 && index - 1 < voiceItems.Count) voiceIdInput.text = voiceItems[index - 1].id; });
            temperatureSlider.onValueChanged.AddListener(_ => RefreshValueLabels());
            maxTokensSlider.onValueChanged.AddListener(_ => RefreshValueLabels());
            chunkLengthSlider.onValueChanged.AddListener(_ => RefreshValueLabels());
            speechSpeedSlider.onValueChanged.AddListener(_ => RefreshValueLabels());
            ttsVolumeSlider.onValueChanged.AddListener(_ => RefreshValueLabels());
            lipSyncSmoothingSlider.onValueChanged.AddListener(_ => RefreshValueLabels());
            lipSyncGainSlider.onValueChanged.AddListener(_ => RefreshValueLabels());
            lipSyncVolumeInfluenceSlider.onValueChanged.AddListener(_ => RefreshValueLabels());
        }

        private void Populate(ModSettings value)
        {
            providerDropdown.options = Options("Vercel AI Gateway", "OpenRouter"); providerDropdown.value = value.llmProvider == LlmProvider.VercelGateway ? 0 : 1;
            openRouterKeyInput.text = value.keys.openRouterApiKey; vercelKeyInput.text = value.keys.vercelApiKey; fishKeyInput.text = value.keys.fishAudioApiKey;
            modelDropdown.options = Options(value.model); modelDropdown.value = 0;
            replyLengthDropdown.options = Options("Short", "Balanced", "Yap"); replyLengthDropdown.value = (int)value.replyLength;
            temperatureSlider.value = (float)value.temperature; maxTokensSlider.value = value.maxTokens; autoSpeakToggle.isOn = value.autoSpeak; runtimeSituationInput.text = value.runtimeSituation;
            fishModelDropdown.options = Options("s2.1-pro-free", "s2-pro", "s1"); fishModelDropdown.value = value.fishModel == "s1" ? 2 : (value.fishModel == "s2" || value.fishModel == "s2-pro" ? 1 : 0);
            fishLatencyDropdown.options = Options("Balanced / fastest", "Normal quality"); fishLatencyDropdown.value = value.fishLatency == "normal" ? 1 : 0;
            remoteTtsModeDropdown.options = Options("Fish Speech Live Bridge", "Stable Stream", "Early Chunks", "Sentence Chunks"); remoteTtsModeDropdown.value = RemoteModeIndex(value.remoteTtsMode);
            fishVoiceScopeDropdown.options = Options("My Models + Public", "My Fish Models", "Public Models"); fishVoiceScopeDropdown.value = value.fishVoiceScope == "mine" ? 1 : (value.fishVoiceScope == "public" ? 2 : 0);
            fishTransportDropdown.options = Options("WebSocket realtime", "Timestamp SSE (HTTP)"); fishTransportDropdown.value = value.fishTransport == FishTransport.TimestampSse ? 1 : 0;
            fishFormatDropdown.options = Options("PCM (native lip sync)"); fishFormatDropdown.value = 0;
            fishSampleRateDropdown.options = Options("16000 Hz", "22050 Hz", "24000 Hz", "32000 Hz", "44100 Hz", "48000 Hz"); SelectPrefix(fishSampleRateDropdown, value.fishSampleRate.ToString());
            conditionPreviousToggle.isOn = value.fishConditionOnPreviousChunks;
            chunkStrategyDropdown.options = Options("Fast phrase", "Safe phrase", "Eager raw"); chunkStrategyDropdown.value = value.fishChunkingStrategy == FishChunkingStrategy.SafePhrase ? 1 : (value.fishChunkingStrategy == FishChunkingStrategy.Eager ? 2 : 0);
            chunkLengthSlider.value = value.fishChunkLength; speechSpeedSlider.value = value.speechSpeed; ttsVolumeSlider.value = value.ttsVolume;
            lipSyncModeDropdown.options = Options("Hybrid (analyser blend)", "wLipSync Direct (raw)"); lipSyncModeDropdown.value = value.lipSyncMode == "direct" ? 1 : 0;
            lipSyncSmoothingSlider.value = value.lipSyncSmoothing; lipSyncGainSlider.value = value.lipSyncGain; lipSyncVolumeInfluenceSlider.value = value.lipSyncVolumeInfluence;
            PopulatePersonas(value); PopulateRouting(value); LoadPersona(value.ActivePersona); RefreshValueLabels();
        }

        private void PopulateRouting(ModSettings value)
        {
            bool openRouter = providerDropdown.value == 1;
            routingDropdown.options = openRouter ? Options("auto", "latency", "throughput", "pinned") : Options("auto", "latency", "throughput", "cost", "pinned");
            routingDropdown.value = openRouter ? (int)value.openRouterRoutingMode : (int)value.vercelRoutingMode;
            pinnedProvidersInput.text = string.Join(", ", openRouter ? value.openRouterProviderSlugs : value.vercelProviderSlugs);
            allowFallbacksToggle.isOn = openRouter ? value.openRouterAllowFallbacks : value.vercelAllowFallbacks;
            refreshEndpointsButton.gameObject.SetActive(!openRouter);
        }

        private void PopulatePersonas(ModSettings value)
        {
            personaDropdown.options = value.personas.Select(x => new TMP_Dropdown.OptionData(x.name)).ToList();
            int index = value.personas.FindIndex(x => x.id == value.activePersonaId); personaDropdown.value = Mathf.Max(0, index); personaDropdown.RefreshShownValue();
        }

        private void LoadPersona(Persona value)
        {
            if (runtime == null) return;
            int index = Mathf.Clamp(personaDropdown.value, 0, runtime.Settings.personas.Count - 1);
            value = runtime.Settings.personas[index]; runtime.Settings.activePersonaId = value.id;
            personaNameInput.text = value.name; personaDescriptionInput.text = value.description; personaPromptInput.text = value.systemPrompt; userNicknameInput.text = value.userNickname;
            voiceIdInput.text = value.voice != null ? value.voice.voiceId : runtime.Settings.fishVoiceId;
        }

        private void SaveAndApply()
        {
            var value = runtime.Settings;
            value.llmProvider = providerDropdown.value == 1 ? LlmProvider.OpenRouter : LlmProvider.VercelGateway;
            value.keys.openRouterApiKey = openRouterKeyInput.text.Trim(); value.keys.vercelApiKey = vercelKeyInput.text.Trim(); value.keys.fishAudioApiKey = fishKeyInput.text.Trim();
            if (modelDropdown.options.Count > 0) value.model = modelDropdown.options[modelDropdown.value].text.Split(' ')[0];
            var pins = pinnedProvidersInput.text.Split(',').Select(x => x.Trim()).Where(x => x.Length > 0).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
            if (value.llmProvider == LlmProvider.OpenRouter) { value.openRouterRoutingMode = (OpenRouterRoutingMode)routingDropdown.value; value.openRouterProviderSlugs = pins; value.openRouterAllowFallbacks = allowFallbacksToggle.isOn; }
            else { value.vercelRoutingMode = (VercelRoutingMode)routingDropdown.value; value.vercelProviderSlugs = pins; value.vercelAllowFallbacks = allowFallbacksToggle.isOn; }
            value.replyLength = (ReplyLength)replyLengthDropdown.value; value.temperature = temperatureSlider.value; value.maxTokens = Mathf.RoundToInt(maxTokensSlider.value); value.autoSpeak = autoSpeakToggle.isOn; value.runtimeSituation = runtimeSituationInput.text;
            value.fishModel = fishModelDropdown.value == 2 ? "s1" : (fishModelDropdown.value == 1 ? "s2" : "s2.1-pro-free"); value.fishLatency = fishLatencyDropdown.value == 1 ? "normal" : "balanced";
            value.remoteTtsMode = RemoteModeValue(remoteTtsModeDropdown.value); value.fishVoiceScope = fishVoiceScopeDropdown.value == 1 ? "mine" : (fishVoiceScopeDropdown.value == 2 ? "public" : "all");
            value.fishTransport = fishTransportDropdown.value == 1 ? FishTransport.TimestampSse : FishTransport.WebSocket; value.fishFormat = "pcm";
            value.fishSampleRate = ParseLeadingInt(fishSampleRateDropdown.options[fishSampleRateDropdown.value].text, 24000);
            value.fishConditionOnPreviousChunks = conditionPreviousToggle.isOn; value.fishChunkingStrategy = chunkStrategyDropdown.value == 1 ? FishChunkingStrategy.SafePhrase : (chunkStrategyDropdown.value == 2 ? FishChunkingStrategy.Eager : FishChunkingStrategy.FastPhrase); value.fishChunkLength = Mathf.RoundToInt(chunkLengthSlider.value); value.speechSpeed = speechSpeedSlider.value; value.ttsVolume = ttsVolumeSlider.value; value.fishVoiceId = voiceIdInput.text.Trim();
            value.lipSyncMode = lipSyncModeDropdown.value == 1 ? "direct" : "hybrid"; value.lipSyncSmoothing = lipSyncSmoothingSlider.value; value.lipSyncGain = lipSyncGainSlider.value; value.lipSyncVolumeInfluence = lipSyncVolumeInfluenceSlider.value;
            SavePersonaFields(value.ActivePersona); value.Normalize(); runtime.SaveAndApply(); PopulatePersonas(value); SetStatus("Settings saved and applied.");
        }

        private void SavePersonaFields(Persona persona)
        {
            persona.name = string.IsNullOrWhiteSpace(personaNameInput.text) ? "Mate" : personaNameInput.text.Trim(); persona.description = personaDescriptionInput.text.Trim(); persona.systemPrompt = personaPromptInput.text.Trim(); persona.userNickname = userNicknameInput.text.Trim();
        }

        private async void RefreshModels()
        {
            try
            {
                SetStatus("Refreshing language models...");
                var value = runtime.Settings; var provider = providerDropdown.value == 1 ? LlmProvider.OpenRouter : LlmProvider.VercelGateway;
                var key = provider == LlmProvider.OpenRouter ? openRouterKeyInput.text : vercelKeyInput.text;
                var items = await catalogs.FetchModelsAsync(provider, key, requests.Token);
                modelDropdown.options = items.Select(x => new TMP_Dropdown.OptionData(x.id + CapabilityLabel(x))).ToList(); SelectPrefix(modelDropdown, value.model); modelDropdown.RefreshShownValue();
                SetStatus("Loaded " + items.Count + " language models.");
            }
            catch (Exception ex) { SetStatus(ex.Message); }
        }

        private async void RefreshEndpoints()
        {
            try
            {
                var model = modelDropdown.options.Count > 0 ? modelDropdown.options[modelDropdown.value].text.Split(' ')[0] : runtime.Settings.model;
                SetStatus("Refreshing Vercel provider endpoints..."); var items = await catalogs.FetchVercelEndpointsAsync(model, requests.Token);
                SetStatus("Providers: " + string.Join(", ", items.Where(x => !x.status.HasValue || x.status == 0).Select(x => x.providerName).ToArray()));
            }
            catch (Exception ex) { SetStatus(ex.Message); }
        }

        private async void RefreshVoices(bool mine)
        {
            try
            {
                SetStatus("Fetching Fish voice models..."); voiceItems = await voices.FetchAsync(fishKeyInput.text, mine, requests.Token);
                voiceDropdown.options = new List<TMP_Dropdown.OptionData> { new TMP_Dropdown.OptionData("Manual Fish reference") };
                voiceDropdown.options.AddRange(voiceItems.Select(x => new TMP_Dropdown.OptionData(string.IsNullOrWhiteSpace(x.author) ? x.title : x.title + " - " + x.author))); voiceDropdown.value = 0; voiceDropdown.RefreshShownValue();
                SetStatus("Loaded " + voiceItems.Count + " Fish voice models.");
            }
            catch (Exception ex) { SetStatus(ex.Message); }
        }

        private void NewPersona()
        {
            SavePersonaFields(runtime.Settings.ActivePersona); var persona = new Persona { id = "persona-" + DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(), name = "New Character", systemPrompt = "" };
            runtime.Settings.personas.Add(persona); runtime.Settings.activePersonaId = persona.id; PopulatePersonas(runtime.Settings); LoadPersona(persona);
        }
        private void DeletePersona()
        {
            var value = runtime.Settings; if (value.personas.Count <= 1) { SetStatus("At least one character is required."); return; }
            value.personas.RemoveAll(x => x.id == value.activePersonaId); value.activePersonaId = value.personas[0].id; PopulatePersonas(value); LoadPersona(value.ActivePersona);
        }
        private void AssignVoice()
        {
            var persona = runtime.Settings.ActivePersona; var modelId = fishModelDropdown.value == 2 ? "s1" : (fishModelDropdown.value == 1 ? "s2" : "s2.1-pro-free"); persona.voice = new VoiceBinding { voiceId = voiceIdInput.text.Trim(), modelId = modelId, label = voiceDropdown.options.Count > 0 ? voiceDropdown.options[voiceDropdown.value].text : voiceIdInput.text.Trim() };
            SetStatus("Current Fish voice assigned to " + persona.name + ".");
        }

        private void SetStatus(string value) { if (statusText != null) statusText.text = value; }
        private void ShowTab(int index)
        {
            llmPage.SetActive(index == 0); characterPage.SetActive(index == 1); fishPage.SetActive(index == 2); lipSyncPage.SetActive(index == 3);
            var scroll = index == 0 ? llmScroll : index == 1 ? characterScroll : index == 2 ? fishScroll : lipSyncScroll;
            Canvas.ForceUpdateCanvases(); scroll.verticalNormalizedPosition = 1f;
            SetTabVisual(llmTabButton, index == 0); SetTabVisual(characterTabButton, index == 1); SetTabVisual(fishTabButton, index == 2); SetTabVisual(lipSyncTabButton, index == 3);
        }
        private static void SetTabVisual(Button button, bool active)
        {
            var colors = button.colors; colors.normalColor = active ? new Color32(112, 77, 205, 255) : new Color32(47, 50, 68, 255); colors.selectedColor = colors.normalColor; button.colors = colors;
        }
        private void RefreshValueLabels()
        {
            temperatureValueText.text = temperatureSlider.value.ToString("0.00");
            maxTokensValueText.text = Mathf.RoundToInt(maxTokensSlider.value).ToString();
            chunkLengthValueText.text = Mathf.RoundToInt(chunkLengthSlider.value).ToString();
            speechSpeedValueText.text = speechSpeedSlider.value.ToString("0.00") + "x";
            ttsVolumeValueText.text = ttsVolumeSlider.value.ToString("0.00");
            lipSyncSmoothingValueText.text = lipSyncSmoothingSlider.value.ToString("0.00");
            lipSyncGainValueText.text = lipSyncGainSlider.value.ToString("0.00") + "x";
            lipSyncVolumeInfluenceValueText.text = lipSyncVolumeInfluenceSlider.value.ToString("0.00");
        }
        private void Update()
        {
            if (IsTyping() || !Input.GetKeyDown(KeyCode.J)) return;
            bool active = !canvas.gameObject.activeSelf; canvas.gameObject.SetActive(active);
            if (active) RegisterMenu(); else UnregisterMenu();
        }
        private static bool IsTyping()
        {
            var selected = EventSystem.current != null ? EventSystem.current.currentSelectedGameObject : null;
            return selected != null && (selected.GetComponent<TMP_InputField>() != null || selected.GetComponent<InputField>() != null);
        }
        private void RegisterMenu()
        {
            if (gameMenuActions == null) gameMenuActions = FindFirstObjectByType<MenuActions>(FindObjectsInactive.Include);
            if (gameMenuActions == null || menuEntry == null || menuRegistered) return;
            if (!gameMenuActions.menuEntries.Exists(x => x.menu == canvas.gameObject)) gameMenuActions.menuEntries.Add(menuEntry);
            menuRegistered = true;
        }
        private void UnregisterMenu()
        {
            if (gameMenuActions == null || !menuRegistered) return;
            gameMenuActions.menuEntries.RemoveAll(x => x.menu == canvas.gameObject); menuRegistered = false;
        }
        private static List<TMP_Dropdown.OptionData> Options(params string[] values) { return values.Select(x => new TMP_Dropdown.OptionData(x)).ToList(); }
        private static void Select(TMP_Dropdown dropdown, string value) { int index = dropdown.options.FindIndex(x => string.Equals(x.text, value, StringComparison.OrdinalIgnoreCase)); dropdown.value = Mathf.Max(0, index); }
        private static void SelectPrefix(TMP_Dropdown dropdown, string value) { int index = dropdown.options.FindIndex(x => x.text == value || x.text.StartsWith(value + " ", StringComparison.Ordinal)); dropdown.value = Mathf.Max(0, index); }
        private static int ParseLeadingInt(string value, int fallback) { int result; return int.TryParse((value ?? "").Split(' ')[0], out result) ? result : fallback; }
        private static int RemoteModeIndex(RemoteTtsMode value) { return value == RemoteTtsMode.FullResponse ? 1 : value == RemoteTtsMode.EarlyChunks ? 2 : value == RemoteTtsMode.SentenceChunks ? 3 : 0; }
        private static RemoteTtsMode RemoteModeValue(int index) { return index == 1 ? RemoteTtsMode.FullResponse : index == 2 ? RemoteTtsMode.EarlyChunks : index == 3 ? RemoteTtsMode.SentenceChunks : RemoteTtsMode.LiveBridge; }
        private static string CapabilityLabel(ModelInfo value)
        {
            var tags = new List<string>(); if (value.supportsStructuredOutputs) tags.Add("json"); if (value.inputModalities.Contains("image")) tags.Add("vision"); if (value.supportsImplicitCaching) tags.Add("cache"); if (value.contextWindow.HasValue) tags.Add((value.contextWindow.Value / 1000) + "K ctx");
            return tags.Count == 0 ? "" : " [" + string.Join(", ", tags.ToArray()) + "]";
        }
        private void OnDestroy() { UnregisterMenu(); if (requests != null) { requests.Cancel(); requests.Dispose(); } }
    }
}
