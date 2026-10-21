using System;
using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace URAH_Kingdoms
{
    using Object = UnityEngine.Object;

    /// <summary>
    /// The profile editor, a main-menu window ("Kingdoms (URAH)" button, MainMenu). Made from the game's
    /// own parts: a copy of the "choose display name" window (frame, caption, its place right of the menu
    /// buttons), filled with copies of that window's label, input field, buttons and status line, and of
    /// the campaign settings' dropdown for the arms. The profiles are on the left (+ New), the selected
    /// one's fields on the right. Opening it closes the other menu windows and the other windows close it
    /// (TitleUI.CloseAnyOpenMenus), like the game's own. Saves through the ProfileStore, which validates
    /// like everywhere else; the arms come from the map's CoA table (CoAMapping), so no map is needed.
    /// </summary>
    internal static class Editor
    {
        private static readonly Color Error = new Color(1f, 0.45f, 0.35f, 1f);

        private static GameObject root;
        private static TitleUI title;

        /// <summary>The game's parts, inside the copied window (inactive), copied again for every control.</summary>
        private sealed class Parts
        {
            public RectTransform Window, Body;
            public TextMeshProUGUI Caption, Label, Status;
            public TMP_InputField Input;
            public BSGButton Button, Back;
        }

        private sealed class Form
        {
            public string Id;
            public TMP_InputField Name, Ian, Anian, Ians, Ruler, Color;
            public BSGDropdown Arms;
            public readonly List<string> ArmsKeys = new List<string>(); // per option; null = the kingdom's own
            public Image Swatch;
            public TextMeshProUGUI Status;
            public TextMeshProUGUI DeleteLabel;
            public bool DeleteArmed;
        }

        public static void Open(TitleUI on, string id, string status = null)
        {
            Close();
            title = on;
            try
            {
                title.CloseAnyOpenMenus(); // closes this one too: built after it
                Build(id, status);
                title.m_LogosAndText?.SetActive(false); // the right side is the window's, as with the game's windows
            }
            catch (Exception e)
            {
                Plugin.Log.LogError($"Profile editor: {e}");
                Close();
            }
        }

        public static void Close()
        {
            if (root != null)
            {
                Object.Destroy(root);
                root = null;
                title?.m_LogosAndText?.SetActive(true);
            }
        }

        [HarmonyPatch(typeof(TitleUI), nameof(TitleUI.CloseAnyOpenMenus))]
        private static class CloseAnyOpenMenusPatch
        {
            private static void Prefix() => Close();
        }

        private static Parts Copy(GameObject source)
        {
            root = Object.Instantiate(source, source.transform.parent, worldPositionStays: false);
            root.name = "URAH_KingdomEditor";
            foreach (UIChooseDisplayName logic in root.GetComponentsInChildren<UIChooseDisplayName>(includeInactive: true))
            {
                Object.DestroyImmediate(logic); // the copy is ours: no display name logic
            }
            var parts = new Parts
            {
                Window = Find<RectTransform>("RegisterWindow"),
                Body = Find<RectTransform>("Body"),
                Caption = Find<TextMeshProUGUI>("id_CaptionLabel"),
                Label = Find<TextMeshProUGUI>("id_TextField"),
                Input = Find<TMP_InputField>("id_DisplayNameInput"),
                Button = Find<BSGButton>("btn_ChooseDisplayName"),
                Back = Find<BSGButton>("btn_Back"),
                Status = Find<TextMeshProUGUI>("id_StatusMessage"),
            };
            var missing = new List<string>();
            if (parts.Window == null) missing.Add("window");
            if (parts.Body == null) missing.Add("body");
            if (parts.Label == null) missing.Add("label");
            if (parts.Input == null) missing.Add("input");
            if (parts.Button == null) missing.Add("button");
            if (missing.Count > 0)
            {
                throw new InvalidOperationException("the display name window has changed, missing: " + string.Join(", ", missing));
            }
            // the window's own content becomes templates; the body is laid out anew
            var templates = new GameObject("Templates", typeof(RectTransform));
            templates.transform.SetParent(root.transform, worldPositionStays: false);
            templates.SetActive(false);
            foreach (Component part in new Component[] { parts.Label, parts.Input, parts.Button, parts.Back, parts.Status })
            {
                if (part != null)
                {
                    part.transform.SetParent(templates.transform, worldPositionStays: false);
                }
            }
            foreach (Transform child in parts.Body.Cast<Transform>().ToList())
            {
                Object.DestroyImmediate(child.gameObject);
            }
            return parts;

            T Find<T>(string name) where T : Component =>
                root.GetComponentsInChildren<T>(includeInactive: true).FirstOrDefault(c => c.name == name);
        }

        private static void Build(string id, string status)
        {
            GameObject source = title.m_ChooseDisplayName;
            if (source == null)
            {
                throw new InvalidOperationException("no display name window to copy");
            }
            Parts parts = Copy(source);

            // larger than the display name window, at the same place right of the menu; caption centred on top
            const float width = 860f, height = 700f;
            parts.Window.anchorMin = new Vector2(parts.Window.anchorMin.x, 1f);
            parts.Window.anchorMax = new Vector2(parts.Window.anchorMin.x, 1f);
            parts.Window.pivot = new Vector2(0f, 1f);
            parts.Window.sizeDelta = new Vector2(width, height);
            parts.Window.anchoredPosition = new Vector2(Math.Min(parts.Window.anchoredPosition.x, 1920f - width - 40f), -170f);
            foreach (RectTransform child in parts.Window.Cast<Transform>().OfType<RectTransform>())
            {
                if (child.name == "Background")
                {
                    child.anchorMin = Vector2.zero;
                    child.anchorMax = Vector2.one;
                    child.pivot = new Vector2(0.5f, 0.5f);
                    child.offsetMin = child.offsetMax = Vector2.zero;
                }
                else if (child.name == "CaptionFrame")
                {
                    child.anchorMin = child.anchorMax = new Vector2(0.5f, 1f);
                    child.anchoredPosition = new Vector2(0f, -38.8f);
                }
            }
            if (parts.Caption != null)
            {
                UIText.SetText(parts.Caption, UiTexts.Get("menu"));
            }

            // body: below the caption, above the footer
            RectTransform body = parts.Body;
            body.anchorMin = Vector2.zero;
            body.anchorMax = Vector2.one;
            body.pivot = new Vector2(0.5f, 0.5f);
            body.offsetMin = new Vector2(40f, 80f);
            body.offsetMax = new Vector2(-40f, -80f);
            var columns = body.gameObject.AddComponent<HorizontalLayoutGroup>();
            columns.spacing = 30f;
            columns.childControlWidth = columns.childControlHeight = true;
            columns.childForceExpandWidth = false;
            columns.childForceExpandHeight = true;

            Profile profile = id != null ? Plugin.Profiles.Load(id) : null;
            var form = new Form { Id = profile != null ? id : null };

            // left: the profiles
            RectTransform list = Column(body, 210f, 0f, 6f);
            Text(parts, list, UiTexts.Get("profiles"));
            foreach (string pid in Plugin.Profiles.Ids())
            {
                Profile p = Plugin.Profiles.Load(pid);
                string target = pid;
                Button(parts, list, p != null ? p.Name : pid + " (" + UiTexts.Get("invalid") + ")", () => Open(title, target), selected: pid == form.Id);
            }
            Button(parts, list, UiTexts.Get("new"), () => Open(title, null), selected: form.Id == null);
            Spacer(list);

            // right: the fields
            RectTransform fields = Column(body, 0f, 1f, 4f);
            form.Name = Field(parts, fields, UiTexts.Get("f_name"), Profile.MaxName);
            form.Ruler = Field(parts, fields, UiTexts.Get("f_ruler"), Profile.MaxName);
            RectTransform colorRow = Row(parts, fields, UiTexts.Get("f_color"));
            form.Color = Input(parts, colorRow, 7);
            form.Swatch = Swatch(parts, colorRow);
            form.Color.onValueChanged.AddListener(_ => UpdateSwatch(form));
            RectTransform armsRow = Row(parts, fields, UiTexts.Get("f_arms"));
            form.Arms = ArmsDropdown(armsRow, form);
            TextMeshProUGUI hint = Text(parts, fields, UiTexts.Get("adjective_hint"));
            hint.fontSize = Math.Max(12f, hint.fontSize - 3f);
            hint.enableWordWrapping = true;
            form.Ian = Field(parts, fields, UiTexts.Get("f_ian"), Profile.MaxName);
            form.Anian = Field(parts, fields, UiTexts.Get("f_anian"), Profile.MaxName);
            form.Ians = Field(parts, fields, UiTexts.Get("f_ians"), Profile.MaxName);
            RectTransform codeRow = ButtonRow(fields);
            Button(parts, codeRow, UiTexts.Get("copy_code"), () => CopyCode(form));
            Button(parts, codeRow, UiTexts.Get("paste_code"), () => PasteCode(form));
            form.Status = parts.Status != null ? Object.Instantiate(parts.Status, fields, worldPositionStays: false) : Text(parts, fields, "");
            form.Status.gameObject.SetActive(true);
            form.Status.gameObject.AddComponent<LayoutElement>().preferredHeight = 40f;
            form.Status.enableWordWrapping = true;
            UIText.SetText(form.Status, "");
            Spacer(fields);

            // footer: the window's buttons, at its bottom edge like the game's windows
            RectTransform footer = Footer(parts.Window);
            if (parts.Back != null)
            {
                BSGButton back = Object.Instantiate(parts.Back, footer, worldPositionStays: false);
                Wire(back, UiTexts.Get("close"), Close);
            }
            Button(parts, footer, UiTexts.Get("save"), () => Save(form));
            if (form.Id != null)
            {
                form.DeleteLabel = Button(parts, footer, UiTexts.Get("delete"), () => Delete(form));
            }

            if (profile != null)
            {
                Fill(form, profile);
            }
            else if (id != null)
            {
                Status(form, string.Format(UiTexts.Get("invalid_file"), id), error: true); // the log has why
            }
            UpdateSwatch(form);
            if (status != null)
            {
                Status(form, status, error: false);
            }
            root.SetActive(true);
            LayoutRebuilder.ForceRebuildLayoutImmediate(parts.Window);
        }

        private static void Fill(Form form, Profile p)
        {
            form.Name.text = p.Name ?? "";
            form.Ruler.text = p.Ruler ?? "";
            form.Color.text = p.Color ?? "";
            SetArms(form, p.Arms);
            form.Ian.text = p.Adjective?.Ian ?? "";
            form.Anian.text = p.Adjective?.Anian ?? "";
            form.Ians.text = p.Adjective?.Ians ?? "";
        }

        /// <summary>The profile in the fields, normalized; null with the errors in the status line.</summary>
        private static Profile Read(Form form)
        {
            int arms = form.Arms.value;
            var p = new Profile
            {
                Name = form.Name.text,
                Ruler = form.Ruler.text,
                Color = form.Color.text,
                Arms = arms >= 0 && arms < form.ArmsKeys.Count ? form.ArmsKeys[arms] : null,
                Adjective = new Adjective { Ian = form.Ian.text, Anian = form.Anian.text, Ians = form.Ians.text },
            };
            var errors = p.Normalize();
            if (errors.Count > 0)
            {
                Status(form, string.Join("\n", errors), error: true);
                return null;
            }
            return p;
        }

        private static void Save(Form form)
        {
            Profile p = Read(form);
            if (p == null)
            {
                return;
            }
            string id = form.Id ?? Plugin.Profiles.FreeId(ProfileStore.IdFromName(p.Name));
            if (!Plugin.Profiles.Save(id, p))
            {
                Status(form, UiTexts.Get("save_failed"), error: true);
                return;
            }
            Plugin.Log.LogInfo($"Profile '{id}' saved: {p}");
            Open(title, id, UiTexts.Get("saved"));
        }

        private static void Delete(Form form)
        {
            if (!form.DeleteArmed)
            {
                form.DeleteArmed = true;
                UIText.SetText(form.DeleteLabel, UiTexts.Get("delete_confirm"));
                return;
            }
            if (Plugin.Profiles.Delete(form.Id))
            {
                Plugin.Log.LogInfo($"Profile '{form.Id}' deleted");
                Open(title, null);
            }
        }

        private static void CopyCode(Form form)
        {
            Profile p = Read(form);
            if (p != null)
            {
                GUIUtility.systemCopyBuffer = p.ToCode();
                Status(form, UiTexts.Get("copied"), error: false);
            }
        }

        private static void PasteCode(Form form)
        {
            Profile p = Profile.FromCode(GUIUtility.systemCopyBuffer, out string error);
            if (p == null)
            {
                Status(form, string.Format(UiTexts.Get("paste_failed"), error), error: true);
                return;
            }
            Fill(form, p);
            bool armsMissing = p.Arms != null && !form.ArmsKeys.Contains(p.Arms);
            Status(form, armsMissing ? string.Format(UiTexts.Get("unknown_arms"), p.Arms) : UiTexts.Get("pasted"), error: armsMissing);
        }

        private static Color? statusColor;

        private static void Status(Form form, string text, bool error)
        {
            if (statusColor == null)
            {
                statusColor = form.Status.color;
            }
            form.Status.color = error ? Error : statusColor.Value;
            UIText.SetText(form.Status, text);
        }

        private static void UpdateSwatch(Form form)
        {
            string text = form.Color.text.Trim();
            form.Swatch.color = text.Length == 7 && ColorUtility.TryParseHtmlString(text, out Color c) ? c : new Color(1f, 1f, 1f, 0.25f);
        }

        // ---- arms ----

        /// <summary>The settings' dropdown: "the kingdom's own", then the kingdoms with arms in the map's CoA table, by name.</summary>
        private static BSGDropdown ArmsDropdown(RectTransform row, Form form)
        {
            BSGDropdown template = Selection.Template();
            if (template == null)
            {
                throw new InvalidOperationException("no dropdown to copy");
            }
            BSGDropdown dropdown = Object.Instantiate(template, row, worldPositionStays: false);
            dropdown.name = "URAH_Arms";
            dropdown.onValueChanged.RemoveAllListeners();
            dropdown.OnShow = null;
            dropdown.interactable = true;
            var le = dropdown.GetComponent<LayoutElement>() ?? dropdown.gameObject.AddComponent<LayoutElement>();
            le.ignoreLayout = false;
            le.flexibleWidth = 1f;
            float h = ((RectTransform)template.transform).rect.height;
            le.preferredHeight = h > 1f ? h : 29f;

            form.ArmsKeys.Clear();
            dropdown.options.Clear();
            form.ArmsKeys.Add(null);
            dropdown.options.Add(new TMP_Dropdown.OptionData(UiTexts.Get("own_arms")));
            foreach (var kv in ArmsKeys()
                .Where(k => Defs.FindTextField("tn_" + k) != null)
                .Select(k => new KeyValuePair<string, string>(k, Defs.Localize("tn_" + k)))
                .OrderBy(kv => kv.Value, StringComparer.CurrentCultureIgnoreCase))
            {
                form.ArmsKeys.Add(kv.Key);
                dropdown.options.Add(new TMP_Dropdown.OptionData(kv.Value));
            }
            dropdown.template.sizeDelta = new Vector2(0f, 25f * 12f);
            dropdown.SetValueWithoutNotify(0);
            dropdown.RefreshShownValue();
            dropdown.gameObject.SetActive(true);
            return dropdown;
        }

        private static void SetArms(Form form, string key)
        {
            int index = key != null ? form.ArmsKeys.IndexOf(key) : 0;
            form.Arms.SetValueWithoutNotify(index < 0 ? 0 : index);
            form.Arms.RefreshShownValue();
        }

        /// <summary>Keys of the kingdoms with arms in the map's CoA table (without the era variants).</summary>
        private static List<string> ArmsKeys()
        {
            string map = GameLogic.Get(create: false)?.map_name;
            Logic.CoAMapping mapping = Logic.CoAMapping.GetMapping(string.IsNullOrEmpty(map) ? "europe" : map);
            if (mapping?.mapping == null)
            {
                return new List<string>();
            }
            return mapping.mapping.Where(kv => kv.Value > 0 && kv.Key.IndexOf('.') < 0).Select(kv => kv.Key).ToList();
        }

        // ---- building blocks: copies of the window's parts in layout groups ----

        private static RectTransform Column(RectTransform parent, float width, float flexible, float spacing)
        {
            var go = new GameObject("Column", typeof(RectTransform), typeof(VerticalLayoutGroup), typeof(LayoutElement));
            var rt = (RectTransform)go.transform;
            rt.SetParent(parent, worldPositionStays: false);
            var v = go.GetComponent<VerticalLayoutGroup>();
            v.spacing = spacing;
            v.childControlWidth = v.childControlHeight = true;
            v.childForceExpandWidth = true;
            v.childForceExpandHeight = false;
            var le = go.GetComponent<LayoutElement>();
            if (width > 0f)
            {
                le.preferredWidth = width;
            }
            le.flexibleWidth = flexible;
            return rt;
        }

        private static void Spacer(RectTransform column)
        {
            var go = new GameObject("Spacer", typeof(RectTransform), typeof(LayoutElement));
            go.transform.SetParent(column, worldPositionStays: false);
            go.GetComponent<LayoutElement>().flexibleHeight = 1f;
        }

        private static TextMeshProUGUI Text(Parts parts, RectTransform parent, string text)
        {
            TextMeshProUGUI t = Object.Instantiate(parts.Label, parent, worldPositionStays: false);
            t.name = "Label";
            t.raycastTarget = false;
            t.enableWordWrapping = false;
            UIText.SetText(t, text);
            t.gameObject.SetActive(true);
            return t;
        }

        /// <summary>A row: the window's label in front, the controls added to it share the rest.</summary>
        private static RectTransform Row(Parts parts, RectTransform parent, string label)
        {
            var go = new GameObject("Row", typeof(RectTransform), typeof(HorizontalLayoutGroup), typeof(LayoutElement));
            var rt = (RectTransform)go.transform;
            rt.SetParent(parent, worldPositionStays: false);
            var h = go.GetComponent<HorizontalLayoutGroup>();
            h.spacing = 10f;
            h.childAlignment = TextAnchor.MiddleLeft;
            h.childControlWidth = h.childControlHeight = true;
            h.childForceExpandWidth = h.childForceExpandHeight = false;
            go.GetComponent<LayoutElement>().preferredHeight = 46f;
            TextMeshProUGUI l = Text(parts, rt, label);
            l.alignment = TextAlignmentOptions.MidlineLeft;
            var le = l.gameObject.AddComponent<LayoutElement>();
            le.preferredWidth = 190f;
            le.preferredHeight = 46f;
            return rt;
        }

        private static TMP_InputField Field(Parts parts, RectTransform parent, string label, int limit)
        {
            return Input(parts, Row(parts, parent, label), limit);
        }

        private static TMP_InputField Input(Parts parts, RectTransform row, int limit)
        {
            TMP_InputField input = Object.Instantiate(parts.Input, row, worldPositionStays: false);
            input.name = "Input";
            input.onValueChanged.RemoveAllListeners();
            input.onEndEdit.RemoveAllListeners();
            input.onSubmit.RemoveAllListeners();
            input.contentType = TMP_InputField.ContentType.Standard;
            input.lineType = TMP_InputField.LineType.SingleLine;
            input.characterLimit = limit;
            input.text = "";
            if (input.placeholder is TMP_Text placeholder)
            {
                placeholder.text = "";
            }
            var le = input.gameObject.AddComponent<LayoutElement>();
            le.flexibleWidth = 1f;
            le.preferredHeight = 44f;
            input.gameObject.SetActive(true);
            return input;
        }

        /// <summary>The colour preview: the input's frame, tinted.</summary>
        private static Image Swatch(Parts parts, RectTransform row)
        {
            var go = new GameObject("Swatch", typeof(RectTransform), typeof(Image), typeof(LayoutElement));
            go.transform.SetParent(row, worldPositionStays: false);
            var img = go.GetComponent<Image>();
            Image frame = parts.Input.GetComponent<Image>();
            if (frame != null)
            {
                img.sprite = frame.sprite;
                img.type = frame.type;
            }
            img.raycastTarget = false;
            var le = go.GetComponent<LayoutElement>();
            le.preferredWidth = 60f;
            le.preferredHeight = 44f;
            return img;
        }

        private static RectTransform ButtonRow(RectTransform parent)
        {
            var go = new GameObject("Buttons", typeof(RectTransform), typeof(HorizontalLayoutGroup), typeof(LayoutElement));
            var rt = (RectTransform)go.transform;
            rt.SetParent(parent, worldPositionStays: false);
            var h = go.GetComponent<HorizontalLayoutGroup>();
            h.spacing = 12f;
            h.childAlignment = TextAnchor.MiddleCenter;
            h.childControlWidth = h.childControlHeight = true;
            h.childForceExpandWidth = h.childForceExpandHeight = false;
            go.GetComponent<LayoutElement>().preferredHeight = 46f;
            return rt;
        }

        /// <summary>The footer row at the window's bottom edge, where the game's windows have their buttons.</summary>
        private static RectTransform Footer(RectTransform window)
        {
            var go = new GameObject("Footer", typeof(RectTransform), typeof(HorizontalLayoutGroup));
            var rt = (RectTransform)go.transform;
            rt.SetParent(window, worldPositionStays: false);
            rt.anchorMin = new Vector2(0f, 0f);
            rt.anchorMax = new Vector2(1f, 0f);
            rt.pivot = new Vector2(0.5f, 0f);
            rt.anchoredPosition = new Vector2(0f, 22f);
            rt.sizeDelta = new Vector2(-80f, 40f);
            var h = go.GetComponent<HorizontalLayoutGroup>();
            h.spacing = 16f;
            h.childAlignment = TextAnchor.MiddleCenter;
            h.childControlWidth = h.childControlHeight = true;
            h.childForceExpandWidth = h.childForceExpandHeight = false;
            return rt;
        }

        /// <summary>A copy of the window's button (196 x 38), as in the game; selected: shown pressed.</summary>
        private static TextMeshProUGUI Button(Parts parts, RectTransform parent, string text, Action onClick, bool selected = false)
        {
            BSGButton button = Object.Instantiate(parts.Button, parent, worldPositionStays: false);
            TextMeshProUGUI label = Wire(button, text, onClick);
            if (selected)
            {
                button.SetSelected(true, force_update: true);
            }
            return label;
        }

        private static TextMeshProUGUI Wire(BSGButton button, string text, Action onClick)
        {
            button.name = "Button";
            button.onClick = b =>
            {
                try
                {
                    onClick();
                }
                catch (Exception e)
                {
                    Plugin.Log.LogError($"Profile editor: {e}");
                }
            };
            button.Enable(true);
            var rt = (RectTransform)button.transform;
            var le = button.GetComponent<LayoutElement>() ?? button.gameObject.AddComponent<LayoutElement>();
            le.ignoreLayout = false;
            le.preferredWidth = rt.rect.width > 1f ? rt.rect.width : 196f;
            le.preferredHeight = rt.rect.height > 1f ? rt.rect.height : 38f;
            TextMeshProUGUI label = button.GetComponentInChildren<TextMeshProUGUI>(includeInactive: true);
            if (label != null)
            {
                UIText.SetText(label, text);
                label.enableAutoSizing = true;
                label.fontSizeMin = 10f;
                label.fontSizeMax = Math.Max(label.fontSize, 16f);
                label.overflowMode = TextOverflowModes.Ellipsis;
            }
            button.gameObject.SetActive(true);
            return label;
        }
    }
}
