using System;
using System.Collections.Generic;
using TMPro;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.UI;

namespace RiftRumble
{
    public sealed class ArenaHud : MonoBehaviour
    {
        private static readonly Color Ink = new Color(.045f, .047f, .105f);
        private static readonly Color Panel = new Color(.09f, .10f, .19f);
        private static readonly Color Muted = new Color(.6f, .66f, .8f);
        private static readonly Color Lime = new Color(.81f, 1f, .28f);
        private Canvas canvas;
        private RectTransform root, menu, hud, modal, result, touch, menuBackdrop, login;
        private TMP_FontAsset font;
        private TMP_Text status, playLabel, hudTitle, score, timer, instruction, playerInfo, ammo, superLabel, resultTitle, resultDetail;
        private TMP_Text ping, incomingLoss, outgoingLoss;
        private UnityEngine.UI.Image hpFill, superFill;
        private UnityEngine.UI.Image superFace, superGlow;
        private UnityEngine.UI.Button superButton;
        private RectTransform menuBurst;
        private Sprite circleSprite, panelSprite, glowSprite, burstSprite;
        private readonly List<Sprite> uiSprites = new List<Sprite>();
        private readonly UnityEngine.UI.Image[] ammoCells = new UnityEngine.UI.Image[GameRules.MaxAmmo];
        private readonly ArenaUiMotion[] cardMotion = new ArenaUiMotion[3];
        private readonly List<BrawlerView> previewModels = new List<BrawlerView>();
        private UnityEngine.UI.Button playButton, practiceButton, directButton;
        private readonly UnityEngine.UI.Image[] cards = new UnityEngine.UI.Image[3];
        private readonly TMP_Text[] cardLabels = new TMP_Text[3];
        private readonly UnityEngine.UI.Image[] modes = new UnityEngine.UI.Image[2];
        private readonly List<RenderTexture> previews = new List<RenderTexture>();
        private readonly List<GameObject> previewObjects = new List<GameObject>();
        private readonly List<Camera> previewCameras = new List<Camera>();
        private readonly Dictionary<NetworkBrawler, HealthLabel> healthLabels = new Dictionary<NetworkBrawler, HealthLabel>();
        private readonly List<NetworkBrawler> removedLabels = new List<NetworkBrawler>();
        private RectTransform healthRoot;
        private TMP_InputField address;
        private TMP_InputField username, serverIp, serverPort;
        private TMP_Text loginError, playerGreeting;
        private bool menuVisible = true;
        private bool resultVisible;
        private int lastGems, lastPower;
        private float nextRefresh, superRelease;
        private float nextNetworkRefresh;

        private sealed class HealthLabel
        {
            public RectTransform Root;
            public TMP_Text Name;
            public TMP_Text Health;
            public UnityEngine.UI.Image Fill;
            public UnityEngine.UI.Image[] Ammo;
        }

        private void Start()
        {
            if (Application.isBatchMode || (ArenaConnection.Instance != null && ArenaConnection.Instance.IsDedicatedServer)) { enabled = false; return; }
            font = Resources.Load<TMP_FontAsset>("RiftRumble/UI/ArenaFont");
            circleSprite = CreateUiSprite(0); panelSprite = CreateUiSprite(1);
            glowSprite = CreateUiSprite(2); burstSprite = CreateUiSprite(3);
            canvas = new GameObject("Arena UI", typeof(RectTransform), typeof(Canvas), typeof(UnityEngine.UI.CanvasScaler), typeof(UnityEngine.UI.GraphicRaycaster)).GetComponent<Canvas>();
            canvas.transform.SetParent(transform, false);
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            var scaler = canvas.GetComponent<UnityEngine.UI.CanvasScaler>();
            scaler.uiScaleMode = UnityEngine.UI.CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1600, 900); scaler.matchWidthOrHeight = .5f;
            scaler.screenMatchMode = UnityEngine.UI.CanvasScaler.ScreenMatchMode.Expand;
            menuBackdrop = Full(canvas.transform, "Menu backdrop");
            menuBackdrop.gameObject.AddComponent<UnityEngine.UI.Image>().color = Ink;
            root = Rect(canvas.transform, "Safe interface", 0, 0, 1600, 900);
            root.anchorMin = root.anchorMax = root.pivot = new Vector2(.5f, .5f);
            root.anchoredPosition = Vector2.zero;
            if (FindFirstObjectByType<EventSystem>() == null)
            {
                var events = new GameObject("Event System", typeof(EventSystem), typeof(InputSystemUIInputModule));
                events.transform.SetParent(transform, false);
            }
            BuildMenu(); BuildHud(); BuildDirectModal(); BuildResults(); BuildLogin();
            RefreshSelection();
        }

        private RectTransform Rect(Transform parent, string name, float x, float y, float width, float height)
        {
            var rect = new GameObject(name, typeof(RectTransform)).GetComponent<RectTransform>();
            rect.SetParent(parent, false); rect.anchorMin = rect.anchorMax = new Vector2(0, 1); rect.pivot = new Vector2(0, 1);
            rect.anchoredPosition = new Vector2(x, -y); rect.sizeDelta = new Vector2(width, height); return rect;
        }

        private RectTransform Full(Transform parent, string name)
        {
            var rect = Rect(parent, name, 0, 0, 0, 0);
            rect.anchorMin = Vector2.zero; rect.anchorMax = Vector2.one; rect.offsetMin = rect.offsetMax = Vector2.zero; return rect;
        }

        private UnityEngine.UI.Image Box(Transform parent, string name, float x, float y, float width, float height, Color color, bool hit = false)
        {
            var rect = Rect(parent, name, x, y, width, height); var image = rect.gameObject.AddComponent<UnityEngine.UI.Image>();
            image.color = color; image.raycastTarget = hit; return image;
        }

        private Sprite CreateUiSprite(int shape)
        {
            const int size = 256;
            var texture = new Texture2D(size, size, TextureFormat.RGBA32, false) { name = "Arena UI shape " + shape, filterMode = FilterMode.Bilinear, wrapMode = TextureWrapMode.Clamp };
            var pixels = new Color[size * size];
            for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
            {
                Vector2 p = new Vector2((x + .5f) / size * 2 - 1, (y + .5f) / size * 2 - 1);
                float radius = p.magnitude, alpha;
                if (shape == 0) alpha = Mathf.Clamp01((1 - radius) * size * .5f);
                else if (shape == 1)
                {
                    Vector2 corner = new Vector2(Mathf.Max(Mathf.Abs(p.x) - .8f, 0), Mathf.Max(Mathf.Abs(p.y) - .8f, 0));
                    alpha = Mathf.Clamp01((.2f - corner.magnitude) * size * .5f);
                }
                else if (shape == 2) alpha = Mathf.Pow(Mathf.Clamp01(1 - radius), 2) * .9f;
                else alpha = (Mathf.Sin(Mathf.Atan2(p.y, p.x) * 12) > 0 ? .5f : .12f) * Mathf.Clamp01(1 - radius);
                pixels[y * size + x] = new Color(1, 1, 1, alpha);
            }
            texture.SetPixels(pixels); texture.Apply(false, true);
            var sprite = Sprite.Create(texture, new Rect(0, 0, size, size), new Vector2(.5f, .5f), 100, 0, SpriteMeshType.FullRect,
                shape == 1 ? new Vector4(28, 28, 28, 28) : Vector4.zero);
            uiSprites.Add(sprite); return sprite;
        }

        private UnityEngine.UI.Image Shape(Transform parent, string name, float x, float y, float width, float height, Color color, Sprite sprite, bool hit = false)
        {
            var image = Box(parent, name, x, y, width, height, color, hit);
            if (height < 20) return image;
            image.sprite = sprite;
            if (sprite == panelSprite) image.type = UnityEngine.UI.Image.Type.Sliced;
            return image;
        }

        private void CenterPivot(RectTransform rect)
        {
            rect.anchoredPosition += new Vector2(rect.sizeDelta.x * .5f, -rect.sizeDelta.y * .5f);
            rect.pivot = new Vector2(.5f, .5f);
        }

        private void DecorateButton(UnityEngine.UI.Button button)
        {
            var image = button.GetComponent<UnityEngine.UI.Image>();
            image.sprite = panelSprite; image.type = UnityEngine.UI.Image.Type.Sliced;
            var shadow = button.gameObject.AddComponent<UnityEngine.UI.Shadow>();
            shadow.effectColor = new Color(0, 0, 0, .45f); shadow.effectDistance = new Vector2(0, -6);
            CenterPivot(image.rectTransform);
            button.gameObject.AddComponent<ArenaUiMotion>().Configure(null, .18f);
        }

        private TMP_Text Label(Transform parent, string value, float x, float y, float width, float height, float size, Color color, TextAlignmentOptions alignment = TextAlignmentOptions.MidlineLeft)
        {
            var rect = Rect(parent, value.Length > 30 ? value.Substring(0, 30) : value, x, y, width, height);
            var text = rect.gameObject.AddComponent<TextMeshProUGUI>(); if (font != null) text.font = font;
            text.text = value; text.fontSize = size; text.color = color; text.alignment = alignment;
            text.raycastTarget = false; text.textWrappingMode = TextWrappingModes.NoWrap;
            text.enableAutoSizing = true; text.fontSizeMin = size * .75f; text.fontSizeMax = size;
            text.overflowMode = TextOverflowModes.Overflow; return text;
        }

        private UnityEngine.UI.Button Button(Transform parent, string value, float x, float y, float width, float height, Color color, Action action, float size = 27)
        {
            var image = Box(parent, value, x, y, width, height, color, true);
            var button = image.gameObject.AddComponent<UnityEngine.UI.Button>(); button.targetGraphic = image;
            var colors = button.colors; colors.highlightedColor = new Color(1.15f, 1.15f, 1.15f); colors.pressedColor = new Color(.7f, .7f, .7f); button.colors = colors;
            Label(image.transform, value, 10, 0, width - 20, height, size, color == Lime ? Ink : Color.white, TextAlignmentOptions.Center);
            button.onClick.AddListener(() => action()); return button;
        }

        private void BuildMenu()
        {
            menu = Full(root, "Character selection");
            var background = menu.gameObject.AddComponent<UnityEngine.UI.Image>(); background.color = new Color(.055f, .14f, .38f);
            background.raycastTarget = false;
            Shape(menu, "Blue atmosphere", 250, -380, 1700, 1400, new Color(.12f, .48f, 1, .7f), glowSprite);
            menuBurst = Shape(menu, "Slow arena rays", 180, -460, 1650, 1650, new Color(.3f, .65f, 1, .32f), burstSprite).rectTransform;
            CenterPivot(menuBurst);
            for (int i = 0; i < 6; i++)
            {
                var stripe = Box(menu, "Arena streak", 100 + i * 350, -150, 2, 1200, new Color(.5f, .8f, 1, .065f));
                stripe.rectTransform.localEulerAngles = new Vector3(0, 0, -24);
            }
            Label(menu, "BRAWLERS", 48, 30, 750, 75, 62, Color.white).fontStyle = FontStyles.Bold;
            playerGreeting = Label(menu, "", 1010, 45, 535, 40, 24, Color.white, TextAlignmentOptions.MidlineRight);
            playerGreeting.richText = false;

            for (int i = 0; i < 3; i++)
            {
                int index = i; BrawlerStats stats = GameRules.Stats((BrawlerId)i);
                float x = 54 + i * 508;
                Shape(menu, "Card shadow", x + 3, 146, 476, 478, new Color(0, 0, 0, .35f), panelSprite);
                cards[i] = Shape(menu, "Brawler " + stats.Name, x, 136, 476, 478, stats.Color, panelSprite, true);
                var glow = Shape(cards[i].transform, "Selection aura", -100, -110, 676, 676, stats.Color, glowSprite);
                var inner = Shape(cards[i].transform, "Card interior", 5, 5, 466, 468, new Color(.035f, .065f, .15f), panelSprite);
                var button = cards[i].gameObject.AddComponent<UnityEngine.UI.Button>(); cards[i].raycastTarget = true;
                button.targetGraphic = inner; button.onClick.AddListener(() => SelectBrawler(index));
                Shape(inner.transform, "Portrait light", -20, -50, 506, 460, Color.Lerp(stats.Color, Color.white, .16f), glowSprite);
                Shape(inner.transform, "Portrait rays", 30, 15, 406, 350, new Color(stats.Color.r, stats.Color.g, stats.Color.b, .4f), burstSprite);
                Shape(inner.transform, "Podium shadow", 100, 280, 265, 38, new Color(0, 0, 0, .3f), circleSprite);
                Label(inner.transform, stats.Role, 18, 14, 430, 30, 20, stats.Color, TextAlignmentOptions.Center);
                var raw = Rect(inner.transform, "Character portrait", 15, 26, 436, 304).gameObject.AddComponent<UnityEngine.UI.RawImage>();
                raw.raycastTarget = false; raw.texture = CreatePreview((BrawlerId)i);
                Label(inner.transform, stats.Name, 22, 320, 420, 62, 52, Color.white, TextAlignmentOptions.Center).fontStyle = FontStyles.Bold;
                BuildStat(inner.transform, "HP", stats.MaxHealth, stats.MaxHealth / 7500f, 22, new Color(.4f, 1, .55f));
                BuildStat(inner.transform, "DMG", stats.Damage, stats.Damage / 1600f, 248, new Color(1, .65f, .35f));
                cardLabels[i] = Label(inner.transform, "", 24, 425, 418, 30, 23, stats.Color, TextAlignmentOptions.Center);
                CenterPivot(cards[i].rectTransform);
                cardMotion[i] = cards[i].gameObject.AddComponent<ArenaUiMotion>();
                cardMotion[i].Configure(glow, i * .09f);
            }
            for (int i = 0; i < 2; i++)
            {
                int index = i; float x = 48 + i * 765;
                modes[i] = Shape(menu, "Game mode " + i, x, 650, 743, 90, Panel, panelSprite, true);
                var button = modes[i].gameObject.AddComponent<UnityEngine.UI.Button>(); button.targetGraphic = modes[i];
                button.onClick.AddListener(() => SelectMode(index));
                var badgeColor = i == 0 ? new Color(.73f, .38f, 1) : new Color(1, .55f, .21f);
                Shape(modes[i].transform, "Mode badge", 18, 12, 66, 66, badgeColor, circleSprite);
                Label(modes[i].transform, i == 0 ? "3v3" : "10", 18, 12, 66, 66, 26, Ink, TextAlignmentOptions.Center);
                Label(modes[i].transform, GameRules.ModeName((GameMode)i), 105, 14, 585, 60, 32, Color.white);
            }
            status = Label(menu, "", 50, 750, 1485, 34, 21, Color.white);
            directButton = Button(menu, "DIRECT CONNECT", 48, 799, 294, 65, Panel, () =>
            {
                if (!string.IsNullOrEmpty(ArenaConnection.Instance.PreferredServerAddress)) address.text = ArenaConnection.Instance.PreferredServerAddress;
                modal.gameObject.SetActive(true);
            });
            practiceButton = Button(menu, "PRACTICE", 362, 799, 270, 65, Panel, () => ArenaConnection.Instance.StartPractice());
            playButton = Button(menu, "PLAY", 1147, 789, 401, 85, new Color(1, .79f, .18f), FindMatch, 35);
            DecorateButton(directButton); DecorateButton(practiceButton); DecorateButton(playButton);
            playLabel = playButton.GetComponentInChildren<TMP_Text>();
            playLabel.color = Ink; playLabel.fontStyle = FontStyles.Bold;
        }

        private void BuildStat(Transform parent, string name, int value, float fraction, float x, Color color)
        {
            Label(parent, name, x, 389, 55, 25, 18, Muted);
            Label(parent, value.ToString("N0"), x + 50, 389, 140, 25, 21, Color.white, TextAlignmentOptions.MidlineRight);
            Shape(parent, name + " track", x, 417, 190, 5, new Color(.18f, .22f, .34f), panelSprite);
            Shape(parent, name + " strength", x, 417, 190 * Mathf.Clamp01(fraction), 5, color, panelSprite);
        }

        private RenderTexture CreatePreview(BrawlerId id)
        {
            var model = new GameObject("Preview " + id); model.transform.position = new Vector3(-80 - (int)id * 20, 0, 0);
            model.transform.rotation = Quaternion.Euler(0, -18, 0);
            var view = model.AddComponent<BrawlerView>(); view.Configure(id, -1);
            previewModels.Add(view);
            foreach (Transform part in model.GetComponentsInChildren<Transform>(true)) part.gameObject.layer = 2;
            previewObjects.Add(model);
            var cameraObject = new GameObject("Portrait camera " + id); var camera = cameraObject.AddComponent<Camera>();
            camera.orthographic = true; camera.orthographicSize = 1.42f; camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = new Color(0, 0, 0, 0); camera.cullingMask = 1 << 2;
            camera.transform.position = model.transform.position + new Vector3(3.1f, 2.8f, 6.3f);
            camera.transform.LookAt(model.transform.position + Vector3.up * 1.15f);
            camera.nearClipPlane = .1f; camera.farClipPlane = 20;
            var texture = new RenderTexture(640, 448, 24, RenderTextureFormat.ARGB32); texture.Create();
            camera.targetTexture = texture; previews.Add(texture); previewCameras.Add(camera); previewObjects.Add(cameraObject);
            return texture;
        }

        private void SelectBrawler(int index)
        {
            if (ArenaConnection.Instance.IsBusy) return;
            ArenaConnection.Instance.SelectedBrawler = (BrawlerId)index; RefreshSelection();
            previewModels[index].PlayAttack(false);
        }

        private void SelectMode(int index)
        {
            if (ArenaConnection.Instance.IsBusy) return;
            ArenaConnection.Instance.SelectedMode = (GameMode)index; RefreshSelection();
        }

        private void RefreshSelection()
        {
            var connection = ArenaConnection.Instance; if (connection == null) return;
            for (int i = 0; i < cards.Length; i++)
            {
                bool selected = i == (int)connection.SelectedBrawler;
                cards[i].color = selected ? GameRules.Stats((BrawlerId)i).Color : new Color(.18f, .2f, .3f);
                cardLabels[i].text = selected ? "SELECTED" : "";
                cardMotion[i].Select(selected);
            }
            for (int i = 0; i < modes.Length; i++) modes[i].color = i == (int)connection.SelectedMode ? new Color(.2f, .32f, .57f) : Panel;
        }

        private async void FindMatch()
        {
            if (ArenaConnection.Instance.IsBusy) ArenaConnection.Instance.CancelMatchmaking();
            else if (!string.IsNullOrEmpty(ArenaConnection.Instance.PreferredServerAddress))
                ArenaConnection.Instance.ConnectDirect(ArenaConnection.Instance.PreferredServerAddress);
            else await ArenaConnection.Instance.FindMatchAsync();
        }

        private TMP_InputField InputField(Transform parent, string name, string placeholder, float x, float y, float width, int limit)
        {
            var box = Box(parent, name, x, y, width, 66, Ink, true);
            var field = box.gameObject.AddComponent<TMP_InputField>();
            var viewport = Rect(box.transform, "Text viewport", 18, 5, width - 36, 56);
            viewport.gameObject.AddComponent<UnityEngine.UI.RectMask2D>();
            var text = Label(viewport, "", 0, 0, width - 36, 56, 29, Color.white);
            text.richText = false;
            text.enableAutoSizing = false;
            field.targetGraphic = box; field.textViewport = viewport;
            field.textComponent = (TextMeshProUGUI)text;
            if (font != null) field.fontAsset = font;
            field.placeholder = Label(viewport, placeholder, 0, 0, width - 36, 56, 25, Muted);
            field.characterLimit = limit; field.lineType = TMP_InputField.LineType.SingleLine;
            field.richText = false;
            return field;
        }

        private void BuildLogin()
        {
            login = Full(root, "Player login");
            login.gameObject.AddComponent<UnityEngine.UI.Image>().color = Ink;
            Box(login, "Brand mark", 48, 40, 8, 61, Lime);
            Label(login, "RIFT <color=#D0FF47>RUMBLE</color>", 72, 30, 900, 75, 60, Color.white);
            var panel = Box(login, "Login card", 385, 145, 830, 660, Panel, true);
            Box(panel.transform, "Accent", 0, 0, 830, 5, Lime);
            Label(panel.transform, "READY TO RUMBLE?", 40, 27, 750, 68, 50, Color.white);
            Label(panel.transform, "Choose the name other players will see.", 42, 105, 746, 38, 25, Muted);
            Label(panel.transform, "USERNAME", 40, 164, 750, 32, 23, Lime);
            username = InputField(panel.transform, "Username", "Enter your name", 40, 205, 750, 20);
            Label(panel.transform, "SERVER IP  /  OPTIONAL", 40, 306, 540, 32, 23, Muted);
            Label(panel.transform, "PORT", 612, 306, 178, 32, 23, Muted);
            serverIp = InputField(panel.transform, "Server IP", "e.g. 127.0.0.1", 40, 347, 548, 45);
            serverPort = InputField(panel.transform, "Server port", "7777", 612, 347, 178, 5);
            serverPort.contentType = TMP_InputField.ContentType.IntegerNumber;
            Label(panel.transform, "Leave the server blank for matchmaking or practice.\nChoose your brawler and mode on the next screen.", 42, 426, 746, 62, 23, Muted);
            loginError = Label(panel.transform, "", 40, 491, 750, 50, 22, new Color(1, .48f, .48f));
            loginError.textWrappingMode = TextWrappingModes.Normal;
            Button(panel.transform, "CONTINUE   >", 40, 556, 750, 70, Lime, ContinueLogin, 32);
            bool required = !ArenaConnection.Instance.HasLogin;
            login.gameObject.SetActive(required); menu.gameObject.SetActive(!required); menuVisible = !required;
            foreach (var camera in previewCameras) camera.enabled = !required;
        }

        private void ContinueLogin()
        {
            if (!ArenaConnection.Instance.TryLogin(username.text, serverIp.text, serverPort.text, out string error))
            {
                loginError.text = error;
                return;
            }
            loginError.text = "";
            username.DeactivateInputField(); serverIp.DeactivateInputField(); serverPort.DeactivateInputField();
            EventSystem.current?.SetSelectedGameObject(null);
            RefreshSelection();
        }

        private void BuildDirectModal()
        {
            modal = Full(root, "Direct server connection");
            modal.gameObject.AddComponent<UnityEngine.UI.Image>().color = new Color(.01f, .02f, .06f, .94f);
            var panel = Box(modal, "Connection card", 400, 240, 800, 420, Panel, true);
            Label(panel.transform, "JOIN A DEDICATED ARENA", 35, 24, 740, 55, 39, Color.white);
            Label(panel.transform, "Use the same mode as the server. The match starts when all slots are filled.", 35, 94, 730, 66, 25, Muted);
            var fieldBox = Box(panel.transform, "Server address", 35, 190, 730, 68, Ink, true);
            address = fieldBox.gameObject.AddComponent<TMP_InputField>();
            var viewport = Rect(fieldBox.transform, "Text viewport", 18, 5, 694, 58);
            viewport.gameObject.AddComponent<UnityEngine.UI.RectMask2D>();
            var fieldText = Label(viewport, "", 0, 0, 694, 58, 28, Color.white);
            address.textViewport = viewport; address.textComponent = (TextMeshProUGUI)fieldText;
            if (font != null) address.fontAsset = font;
            address.text = "127.0.0.1:7777"; address.characterLimit = 64;
            Button(panel.transform, "BACK", 35, 303, 220, 70, Ink, () => modal.gameObject.SetActive(false));
            Button(panel.transform, "CONNECT   >", 275, 303, 490, 70, Lime, () =>
            {
                modal.gameObject.SetActive(false); ArenaConnection.Instance.ConnectDirect(address.text);
            });
            modal.gameObject.SetActive(false);
        }

        private void BuildHud()
        {
            hud = Full(canvas.transform, "Match HUD");
            hud.SetSiblingIndex(root.GetSiblingIndex());
            healthRoot = Full(hud, "Player nameplates");
            var top = Shape(hud, "Match header", 575, 20, 450, 116, new Color(.025f, .04f, .1f, .8f), panelSprite);
            top.rectTransform.anchorMin = top.rectTransform.anchorMax = new Vector2(.5f, 1);
            top.rectTransform.anchoredPosition = new Vector2(-225, -20);
            hudTitle = Label(top.transform, "GEM RUSH", 15, 4, 420, 26, 19, Color.white, TextAlignmentOptions.Center);
            score = Label(top.transform, "0  :  0", 10, 30, 430, 49, 38, Color.white, TextAlignmentOptions.Center);
            timer = Label(top.transform, "3:00", 135, 81, 180, 27, 24, new Color(1, .83f, .3f), TextAlignmentOptions.Center);
            var leave = Button(hud, "LEAVE", 1425, 24, 130, 50, new Color(.07f, .1f, .18f, .8f), Leave, 20);
            var leaveRect = leave.GetComponent<RectTransform>();
            leaveRect.anchorMin = leaveRect.anchorMax = Vector2.one;
            leaveRect.anchoredPosition = new Vector2(-175, -24);
            DecorateButton(leave);
            var network = Shape(hud, "Connection statistics", 0, 0, 252, 82, new Color(.025f, .03f, .08f, .72f), panelSprite);
            network.rectTransform.anchorMin = network.rectTransform.anchorMax = network.rectTransform.pivot = Vector2.one;
            network.rectTransform.anchoredPosition = new Vector2(-44, -88);
            ping = Label(network.transform, "PING  -- ms", 12, 5, 228, 26, 20, Lime, TextAlignmentOptions.MidlineRight);
            Label(network.transform, "PACKET LOSS", 12, 33, 228, 18, 13, Muted, TextAlignmentOptions.MidlineRight);
            incomingLoss = Label(network.transform, "IN  --%", 12, 54, 114, 22, 17, Muted);
            outgoingLoss = Label(network.transform, "OUT  --%", 126, 54, 114, 22, 17, Muted, TextAlignmentOptions.MidlineRight);
            instruction = Label(hud, "", 300, 150, 1000, 65, 33, Color.white, TextAlignmentOptions.Center);
            instruction.rectTransform.anchorMin = instruction.rectTransform.anchorMax = new Vector2(.5f, 1);
            instruction.rectTransform.anchoredPosition = new Vector2(-500, -150);
            var bottom = Shape(hud, "Local vitals", 34, 24, 312, 116, new Color(.025f, .03f, .08f, .7f), panelSprite);
            playerInfo = Label(bottom.transform, "", 14, 5, 284, 29, 22, Color.white);
            playerInfo.richText = false;
            Shape(bottom.transform, "Health track", 14, 41, 284, 12, new Color(.08f, .1f, .16f), panelSprite);
            hpFill = Shape(bottom.transform, "Health", 14, 41, 284, 12, new Color(.36f, 1, .53f), panelSprite);
            for (int i = 0; i < ammoCells.Length; i++)
            {
                Shape(bottom.transform, "Ammo track " + i, 14 + i * 97, 61, 90, 9, new Color(.14f, .17f, .25f), panelSprite);
                ammoCells[i] = Shape(bottom.transform, "Ammo " + i, 14 + i * 97, 61, 90, 9, new Color(1, .72f, .2f), panelSprite);
            }
            ammo = Label(bottom.transform, "", 14, 80, 284, 25, 19, Lime);
            touch = Full(hud, "Touch controls");
            var superRim = Shape(touch, "Super button", 0, 0, 120, 120, Ink, circleSprite, true);
            superRim.rectTransform.anchorMin = superRim.rectTransform.anchorMax = superRim.rectTransform.pivot = new Vector2(.5f, .5f);
            superGlow = Shape(superRim.transform, "Super ready glow", -60, -60, 240, 240, new Color(1, .8f, .15f, 0), glowSprite);
            Shape(superRim.transform, "Charge track", 4, 4, 112, 112, new Color(.32f, .28f, .13f), circleSprite);
            superFill = Shape(superRim.transform, "Super charge", 4, 4, 112, 112, new Color(1, .78f, .13f), circleSprite);
            superFill.type = UnityEngine.UI.Image.Type.Filled; superFill.fillMethod = UnityEngine.UI.Image.FillMethod.Radial360;
            superFill.fillOrigin = 2; superFill.fillAmount = 0;
            superFace = Shape(superRim.transform, "Super face", 12, 12, 96, 96, new Color(.22f, .2f, .16f), circleSprite);
            Shape(superRim.transform, "Super skull", 38, 30, 44, 40, Color.white, circleSprite);
            Shape(superRim.transform, "Skull jaw", 46, 53, 28, 24, Color.white, panelSprite);
            Shape(superRim.transform, "Skull eye L", 44, 43, 10, 12, Ink, circleSprite);
            Shape(superRim.transform, "Skull eye R", 66, 43, 10, 12, Ink, circleSprite);
            Box(superRim.transform, "Skull tooth", 58, 64, 4, 13, Ink);
            superLabel = Label(superRim.transform, "0%", 16, 81, 88, 24, 18, Color.white, TextAlignmentOptions.Center);
            superButton = superRim.gameObject.AddComponent<UnityEngine.UI.Button>(); superButton.targetGraphic = superFace;
            superButton.onClick.AddListener(() =>
            {
                var local = NetworkBrawler.Local;
                if (!NetworkBrawler.InputEnabled || local == null || !local.Alive.Value || local.SuperCharge.Value < 1) return;
                NetworkBrawler.TouchSuper = true; superRelease = Time.unscaledTime + .1f;
            });
            MakeJoystick(touch, false, null); MakeJoystick(touch, true, superRim.rectTransform);
            superRim.transform.SetAsLastSibling();
            hud.gameObject.SetActive(false);
        }

        private void MakeJoystick(Transform parent, bool attack, RectTransform companion)
        {
            var area = Full(parent, attack ? "Aim touch zone" : "Move touch zone");
            area.anchorMin = new Vector2(attack ? .5f : 0, 0); area.anchorMax = new Vector2(attack ? 1 : .5f, .48f);
            area.pivot = new Vector2(.5f, .5f); area.offsetMin = area.offsetMax = Vector2.zero;
            area.gameObject.AddComponent<UnityEngine.UI.Image>().color = Color.clear;
            Color color = attack ? new Color(1, .27f, .3f) : new Color(.18f, .68f, 1);
            var back = Shape(area, "Stick base", 0, 0, 200, 200, new Color(color.r, color.g, color.b, .35f), circleSprite);
            back.rectTransform.anchorMin = back.rectTransform.anchorMax = back.rectTransform.pivot = new Vector2(.5f, .5f);
            back.rectTransform.anchoredPosition = new Vector2(attack ? 150 : -150, -30);
            Shape(back.transform, "Stick rim", 12, 12, 176, 176, new Color(color.r, color.g, color.b, .45f), circleSprite);
            Shape(back.transform, "Stick well", 18, 18, 164, 164, new Color(.03f, .06f, .12f, .65f), circleSprite);
            var handle = Shape(back.transform, "Stick", 0, 0, 86, 86, color, circleSprite);
            handle.rectTransform.anchorMin = handle.rectTransform.anchorMax = handle.rectTransform.pivot = new Vector2(.5f, .5f);
            handle.rectTransform.anchoredPosition = Vector2.zero;
            Shape(handle.transform, "Stick highlight", 13, 9, 60, 50, new Color(1, 1, 1, .18f), circleSprite);
            if (attack)
            {
                Shape(handle.transform, "Aim icon", 25, 25, 36, 36, Color.white, circleSprite);
                Shape(handle.transform, "Aim icon centre", 32, 32, 22, 22, color, circleSprite);
            }
            var stick = area.gameObject.AddComponent<ArenaTouchStick>(); stick.Configure(back.rectTransform, handle.rectTransform, attack, companion);
        }

        private void BuildResults()
        {
            result = Full(root, "Match result");
            result.gameObject.AddComponent<UnityEngine.UI.Image>().color = new Color(.025f, .03f, .09f, .91f);
            Label(result, "ARENA COMPLETE", 400, 180, 800, 50, 30, Lime, TextAlignmentOptions.Center);
            resultTitle = Label(result, "VICTORY", 200, 264, 1200, 130, 104, Color.white, TextAlignmentOptions.Center);
            resultDetail = Label(result, "", 400, 410, 800, 130, 30, Muted, TextAlignmentOptions.Center);
            Button(result, "BACK TO BRAWLERS   >", 535, 612, 530, 85, Lime, Leave, 34);
            result.gameObject.SetActive(false);
        }

        private async void Leave()
        {
            NetworkBrawler.InputEnabled = false;
            await ArenaConnection.Instance.LeaveAsync();
        }

        private void Update()
        {
            if (root == null || ArenaConnection.Instance == null) return;
            var connection = ArenaConnection.Instance; var match = ArenaMatch.Instance;
            bool inMatch = match != null && match.IsSpawned && NetworkManager.Singleton.IsClient;
            bool showLogin = !connection.HasLogin && !inMatch;
            bool showMenu = !inMatch && !showLogin;
            login.gameObject.SetActive(showLogin);
            menuBackdrop.gameObject.SetActive(!inMatch);
            if (menuVisible != showMenu)
            {
                menuVisible = showMenu; menu.gameObject.SetActive(menuVisible);
                foreach (var camera in previewCameras) camera.enabled = menuVisible;
                if (menuVisible) foreach (var motion in cardMotion) motion.Replay();
            }
            if (hud.gameObject.activeSelf != inMatch)
            {
                hud.gameObject.SetActive(inMatch);
                nextNetworkRefresh = 0;
                if (inMatch && ArenaCamera.Main != null) ArenaCamera.Main.ShowArena();
            }
            bool finished = inMatch && match.Phase == MatchPhase.Finished;
            if (finished && !resultVisible) ArenaAudio.PlayResult(NetworkBrawler.Local != null && NetworkBrawler.Local.Team.Value == match.WinningTeam);
            resultVisible = finished;
            result.gameObject.SetActive(finished);
            NetworkBrawler.InputEnabled = inMatch && match.IsPlaying && !finished && !modal.gameObject.activeSelf;
            if (superRelease > 0 && Time.unscaledTime > superRelease) { NetworkBrawler.TouchSuper = false; superRelease = 0; }
            UpdateNameplates(inMatch);
            UpdateVisuals(inMatch);
            if (Time.unscaledTime < nextRefresh) return; nextRefresh = Time.unscaledTime + .1f;
            status.text = connection.Status == "Choose your brawler and jump in." ? "" : connection.Status;
            playerGreeting.text = connection.Username;
            playLabel.text = connection.IsBusy ? "CANCEL" : string.IsNullOrEmpty(connection.PreferredServerAddress) ? "PLAY" : "JOIN SERVER";
            practiceButton.interactable = directButton.interactable = !connection.IsBusy;
            if (!inMatch) return;
            if (Time.unscaledTime >= nextNetworkRefresh)
            {
                nextNetworkRefresh = Time.unscaledTime + 1f;
                var stats = connection.NetworkStats;
                ping.text = stats != null && stats.HasPing ? $"PING  {stats.PingMs} ms" : "PING  -- ms";
                incomingLoss.text = stats != null && stats.IncomingLoss >= 0 ? $"IN  {stats.IncomingLoss:0.0}%" : "IN  --%";
                outgoingLoss.text = stats != null && stats.OutgoingLoss >= 0 ? $"OUT  {stats.OutgoingLoss:0.0}%" : "OUT  --%";
            }
            hudTitle.text = GameRules.ModeName(match.Mode);
            score.text = match.Mode == GameMode.GemRush ? $"<color=#65DEFF>{match.BlueScore}</color>    :    <color=#FF787B>{match.RedScore}</color>" : $"{match.AliveCount}  BRAWLERS LEFT";
            int remaining = Mathf.CeilToInt(match.Remaining); timer.text = $"{remaining / 60}:{remaining % 60:00}";
            var local = NetworkBrawler.Local;
            if (match.Phase == MatchPhase.Waiting) instruction.text = $"WAITING FOR PLAYERS   {match.Players.Count} / {match.Capacity}";
            else if (match.Phase == MatchPhase.Countdown) instruction.text = $"GET READY   {Mathf.CeilToInt(match.Remaining)}";
            else if (local != null && !local.Alive.Value) instruction.text = match.Mode == GameMode.GemRush ? "KNOCKED OUT  /  RESPAWNING..." : "ELIMINATED  /  SPECTATING";
            else if (match.HoldingTeam >= 0) instruction.text = $"{(match.HoldingTeam == 0 ? "BLUE" : "RED")} TEAM HOLDING   {Mathf.CeilToInt(match.HoldRemaining)}";
            else instruction.text = "";
            if (local != null)
            {
                if (local.Gems.Value > lastGems || local.Power.Value > lastPower) ArenaAudio.PlayPickup(local.Power.Value > lastPower);
                lastGems = local.Gems.Value; lastPower = local.Power.Value;
                playerInfo.text = $"{local.DisplayName}    {local.Health.Value:N0}";
                ammo.text = match.Mode == GameMode.GemRush ? "GEMS  " + local.Gems.Value : "POWER  " + local.Power.Value;
                superLabel.text = local.SuperCharge.Value >= 1 ? "" : $"{local.SuperCharge.Value * 100:0}%";
            }
            if (finished)
            {
                bool victory = local != null && local.Team.Value == match.WinningTeam;
                resultTitle.text = match.WinningTeam < 0 ? "DRAW" : victory ? "VICTORY!" : "ROUND OVER";
                resultTitle.color = victory ? Lime : Color.white;
                resultDetail.text = match.Mode == GameMode.GemRush ? $"GEM RUSH\nBLUE  {match.BlueScore}   /   RED  {match.RedScore}" : "SOLO SHOWDOWN\n" + (victory ? "You are the last brawler standing." : "Pick a brawler. Take another shot.");
            }
        }

        private void UpdateVisuals(bool inMatch)
        {
            if (menuVisible)
            {
                menuBurst.localEulerAngles = new Vector3(0, 0, Time.unscaledTime * 1.5f);
                for (int i = 0; i < previewModels.Count; i++)
                {
                    var view = previewModels[i];
                    float angle = -18 + Mathf.Sin(Time.unscaledTime * .65f + i) * (i == (int)ArenaConnection.Instance.SelectedBrawler ? 12 : 4);
                    view.SetMotion(Vector3.zero, Quaternion.Euler(0, angle, 0) * Vector3.forward, true);
                }
            }
            if (!inMatch) return;
            var local = NetworkBrawler.Local;
            bool ready = local != null && local.Alive.Value && NetworkBrawler.InputEnabled && local.SuperCharge.Value >= 1;
            superButton.interactable = ready;
            float blend = 1 - Mathf.Exp(-14 * Time.unscaledDeltaTime);
            float charge = local != null ? Mathf.Clamp01(local.SuperCharge.Value) : 0;
            superFill.fillAmount = Mathf.Lerp(superFill.fillAmount, charge, blend);
            superFace.color = ready ? new Color(1, .78f, .12f) : new Color(.22f, .2f, .16f);
            superGlow.color = new Color(1, .78f, .12f, ready ? .7f + Mathf.Sin(Time.unscaledTime * 7) * .2f : 0);
            superButton.transform.localScale = Vector3.one * (ready ? 1.03f + Mathf.Sin(Time.unscaledTime * 7) * .03f : 1);
            if (local == null) return;
            float health = Mathf.Clamp01((float)local.Health.Value / local.MaxHealth);
            hpFill.rectTransform.sizeDelta = new Vector2(Mathf.Lerp(hpFill.rectTransform.sizeDelta.x, 284 * health, blend), 12);
            hpFill.color = health < .3f ? new Color(1, .3f, .33f) : new Color(.36f, 1, .53f);
            for (int i = 0; i < ammoCells.Length; i++)
            {
                float amount = i < local.Ammo.Value ? 1 : i == local.Ammo.Value ? local.ReloadProgress : 0;
                ammoCells[i].rectTransform.sizeDelta = new Vector2(90 * amount, 9);
                ammoCells[i].color = i < local.Ammo.Value ? new Color(1, .72f, .2f) : new Color(1, .72f, .2f, .45f);
            }
        }

        private void UpdateNameplates(bool visible)
        {
            removedLabels.Clear();
            foreach (var pair in healthLabels)
                if (pair.Key == null || !pair.Key.IsSpawned || !visible) removedLabels.Add(pair.Key);
            foreach (var player in removedLabels) { Destroy(healthLabels[player].Root.gameObject); healthLabels.Remove(player); }
            if (!visible || Camera.main == null) return;
            foreach (var player in ArenaMatch.Instance.Players)
            {
                if (player == null) continue;
                if (!healthLabels.TryGetValue(player, out HealthLabel label))
                {
                    var bar = Rect(healthRoot, "Brawler vitals", 0, 0, 164, 80);
                    Shape(bar, "Health outline", 10, 45, 144, 17, Ink, panelSprite);
                    label = new HealthLabel
                    {
                        Root = bar, Name = Label(bar, "", 0, 0, 164, 27, 21, Color.white, TextAlignmentOptions.Center),
                        Health = Label(bar, "", 0, 26, 164, 20, 18, Color.white, TextAlignmentOptions.Center),
                        Fill = Shape(bar, "Health fill", 13, 48, 138, 11, Lime, panelSprite),
                        Ammo = new UnityEngine.UI.Image[GameRules.MaxAmmo]
                    };
                    label.Name.fontStyle = FontStyles.Bold;
                    for (int i = 0; i < label.Ammo.Length; i++)
                        label.Ammo[i] = Shape(bar, "Ammo " + i, 13 + i * 47, 66, 44, 6, new Color(1, .72f, .2f), panelSprite);
                    label.Name.richText = false;
                    label.Name.fontSizeMin = 10;
                    healthLabels[player] = label;
                }
                Vector3 screen = Camera.main.WorldToScreenPoint(player.transform.position + Vector3.up * 3f);
                label.Root.gameObject.SetActive(player.Alive.Value && screen.z > 0);
                RectTransformUtility.ScreenPointToLocalPointInRectangle(healthRoot, screen, null, out Vector2 local);
                label.Root.anchorMin = label.Root.anchorMax = new Vector2(.5f, .5f); label.Root.anchoredPosition = local - healthRoot.rect.center + new Vector2(-82, 0);
                label.Name.text = player.DisplayName + (player.Gems.Value > 0 ? "  " + player.Gems.Value : "");
                label.Health.text = player.Health.Value.ToString();
                label.Fill.rectTransform.sizeDelta = new Vector2(138 * Mathf.Clamp01((float)player.Health.Value / player.MaxHealth), 11);
                var owner = NetworkBrawler.Local;
                label.Fill.color = player == owner ? new Color(.4f, 1, .35f) : owner != null && player.Team.Value >= 0 && player.Team.Value == owner.Team.Value ? new Color(.25f, .8f, 1) : new Color(1, .35f, .4f);
                for (int i = 0; i < label.Ammo.Length; i++)
                {
                    label.Ammo[i].gameObject.SetActive(player == owner);
                    float amount = i < player.Ammo.Value ? 1 : i == player.Ammo.Value ? player.ReloadProgress : 0;
                    label.Ammo[i].rectTransform.sizeDelta = new Vector2(44 * amount, 6);
                }
            }
        }

        private void OnDestroy()
        {
            foreach (var preview in previewObjects) if (preview != null) Destroy(preview);
            foreach (var texture in previews) { texture.Release(); Destroy(texture); }
            foreach (var sprite in uiSprites) { Destroy(sprite.texture); Destroy(sprite); }
            NetworkBrawler.TouchMove = NetworkBrawler.TouchAim = Vector2.zero;
            NetworkBrawler.TouchAiming = NetworkBrawler.TouchAttack = NetworkBrawler.TouchQuickFire = NetworkBrawler.TouchSuper = false;
        }
    }
}
