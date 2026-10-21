using System;
using System.Collections.Generic;
using System.Linq;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace URAH_Kingdoms
{
    using Object = UnityEngine.Object;

    /// <summary>
    /// The profile editor, a main-menu window ("Kingdoms (URAH)" button, MainMenuPatch): the profiles on
    /// the left (+ New), the selected one's fields on the right. Plain uGUI parts in the game's font (the
    /// main menu has no window with input fields to copy). TMP input fields: the game ignores its hotkeys
    /// while one has the focus (UICommon.IsInInput). Saves through the ProfileStore, which validates like
    /// everywhere else; the arms come from the map's CoA table (CoAMapping), so no map has to be loaded.
    /// </summary>
    internal static class Editor
    {
        private static readonly Color Back = new Color(0.10f, 0.085f, 0.07f, 0.97f);
        private static readonly Color Field = new Color(0.24f, 0.21f, 0.18f, 1f);
        private static readonly Color ButtonColor = new Color(0.36f, 0.29f, 0.20f, 1f);
        private static readonly Color Selected = new Color(0.62f, 0.47f, 0.24f, 1f);
        private static readonly Color Text = new Color(0.93f, 0.89f, 0.80f, 1f);
        private static readonly Color Error = new Color(1f, 0.55f, 0.45f, 1f);

        private static GameObject root;

        private sealed class Form
        {
            public string Id;
            public TMP_InputField Name, Ian, Anian, Ians, Ruler, Color, Arms;
            public Image Swatch;
            public TextMeshProUGUI ArmsName, Status;
            public TextMeshProUGUI DeleteLabel;
            public bool DeleteArmed;
            public List<string> ArmsKeys;
        }

        private static Canvas canvas;

        public static void Open(Canvas on, string id, string status = null)
        {
            Close();
            canvas = on;
            try
            {
                Build(id, status);
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
            }
        }

        private static TMP_FontAsset font;

        private static void Build(string id, string status)
        {
            if (canvas == null)
            {
                Plugin.Log.LogWarning("Profile editor: no canvas");
                return;
            }
            font = canvas.GetComponentInChildren<TextMeshProUGUI>(includeInactive: true)?.font ?? TMP_Settings.defaultFontAsset;

            // full-screen blocker: nothing behind the window takes clicks
            root = new GameObject("URAH_KingdomEditor", typeof(RectTransform), typeof(Image));
            var rootRt = (RectTransform)root.transform;
            rootRt.SetParent(canvas.transform, worldPositionStays: false);
            rootRt.anchorMin = Vector2.zero;
            rootRt.anchorMax = Vector2.one;
            rootRt.offsetMin = rootRt.offsetMax = Vector2.zero;
            root.GetComponent<Image>().color = new Color(0f, 0f, 0f, 0.55f);
            rootRt.SetAsLastSibling();

            var window = new GameObject("Window", typeof(RectTransform), typeof(Image), typeof(HorizontalLayoutGroup), typeof(ContentSizeFitter));
            var wrt = (RectTransform)window.transform;
            wrt.SetParent(rootRt, worldPositionStays: false);
            wrt.anchorMin = wrt.anchorMax = wrt.pivot = new Vector2(0.5f, 0.5f);
            wrt.sizeDelta = new Vector2(940f, 0f);
            window.GetComponent<Image>().color = Back;
            var h = window.GetComponent<HorizontalLayoutGroup>();
            h.padding = new RectOffset(20, 20, 20, 20);
            h.spacing = 20f;
            h.childControlWidth = h.childControlHeight = true;
            h.childForceExpandWidth = false;
            h.childForceExpandHeight = true;
            window.GetComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;

            Profile profile = id != null ? Plugin.Profiles.Load(id) : null;
            var form = new Form { Id = profile != null ? id : null };

            // left: the profiles
            RectTransform list = Column(wrt, 260f, 0f);
            Label(list, UiTexts.Get("profiles"), 22f, FontStyles.Bold);
            foreach (string pid in Plugin.Profiles.Ids())
            {
                Profile p = Plugin.Profiles.Load(pid);
                string text = p != null ? p.Name : pid + " (" + UiTexts.Get("invalid") + ")";
                string target = pid;
                TextMeshProUGUI l = Button(list, text, 0f, () => Open(canvas, target), pid == form.Id);
                l.alignment = TextAlignmentOptions.MidlineLeft;
            }
            Button(list, UiTexts.Get("new"), 0f, () => Open(canvas, null), form.Id == null);

            // right: the fields
            RectTransform rt = Column(wrt, 0f, 1f);
            Label(rt, UiTexts.Get(form.Id != null ? "editor_edit" : "editor_new"), 24f, FontStyles.Bold);
            form.Name = Input(Row(rt, UiTexts.Get("f_name")), Profile.MaxName);
            form.Ruler = Input(Row(rt, UiTexts.Get("f_ruler")), Profile.MaxName);

            RectTransform colorRow = Row(rt, UiTexts.Get("f_color"));
            form.Color = Input(colorRow, 7);
            form.Swatch = Box(colorRow, 40f, Color.white);
            form.Color.onValueChanged.AddListener(_ => UpdateSwatch(form));

            RectTransform armsRow = Row(rt, UiTexts.Get("f_arms"));
            form.Arms = Input(armsRow, Profile.MaxKey);
            Button(armsRow, "<", 40f, () => CycleArms(form, -1));
            Button(armsRow, ">", 40f, () => CycleArms(form, +1));
            form.ArmsName = Label(rt, "", 15f, FontStyles.Italic);
            form.Arms.onValueChanged.AddListener(_ => UpdateArmsName(form));

            Label(rt, UiTexts.Get("adjective_hint"), 15f, FontStyles.Italic);
            form.Ian = Input(Row(rt, UiTexts.Get("f_ian")), Profile.MaxName);
            form.Anian = Input(Row(rt, UiTexts.Get("f_anian")), Profile.MaxName);
            form.Ians = Input(Row(rt, UiTexts.Get("f_ians")), Profile.MaxName);

            RectTransform codeRow = ButtonRow(rt);
            Button(codeRow, UiTexts.Get("copy_code"), 0f, () => CopyCode(form));
            Button(codeRow, UiTexts.Get("paste_code"), 0f, () => PasteCode(form));

            form.Status = Label(rt, "", 16f, FontStyles.Normal);
            form.Status.color = Error;

            RectTransform buttons = ButtonRow(rt);
            Button(buttons, UiTexts.Get("save"), 0f, () => Save(form));
            if (form.Id != null)
            {
                form.DeleteLabel = Button(buttons, UiTexts.Get("delete"), 0f, () => Delete(form));
            }
            Button(buttons, UiTexts.Get("close"), 0f, Close);

            form.ArmsKeys = ArmsKeys();
            if (profile != null)
            {
                Fill(form, profile);
            }
            else if (id != null)
            {
                Status(form, string.Format(UiTexts.Get("invalid_file"), id), error: true); // the log has why
            }
            UpdateSwatch(form);
            UpdateArmsName(form);
            if (status != null)
            {
                Status(form, status, error: false);
            }
        }

        private static void Fill(Form form, Profile p)
        {
            form.Name.text = p.Name ?? "";
            form.Ruler.text = p.Ruler ?? "";
            form.Color.text = p.Color ?? "";
            form.Arms.text = p.Arms ?? "";
            form.Ian.text = p.Adjective?.Ian ?? "";
            form.Anian.text = p.Adjective?.Anian ?? "";
            form.Ians.text = p.Adjective?.Ians ?? "";
        }

        /// <summary>The profile in the fields, normalized; null with the errors in the status line.</summary>
        private static Profile Read(Form form)
        {
            var p = new Profile
            {
                Name = form.Name.text,
                Ruler = form.Ruler.text,
                Color = form.Color.text,
                Arms = form.Arms.text,
                Adjective = new Adjective { Ian = form.Ian.text, Anian = form.Anian.text, Ians = form.Ians.text },
            };
            var errors = p.Normalize();
            if (p.Arms != null && form.ArmsKeys.Count > 0 && !form.ArmsKeys.Contains(p.Arms))
            {
                errors.Add(string.Format(UiTexts.Get("unknown_arms"), p.Arms));
            }
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
            Open(canvas, id, UiTexts.Get("saved"));
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
                Open(canvas, null);
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
            Status(form, UiTexts.Get("pasted"), error: false);
        }

        private static void Status(Form form, string text, bool error)
        {
            form.Status.color = error ? Error : Text;
            UIText.SetText(form.Status, text);
        }

        private static void UpdateSwatch(Form form)
        {
            bool ok = ColorUtility.TryParseHtmlString(form.Color.text.Trim(), out Color c) && form.Color.text.Trim().Length == 7;
            form.Swatch.color = ok ? c : new Color(0f, 0f, 0f, 0.3f);
        }

        /// <summary>Keys of the kingdoms with arms in the map's CoA table (without the era variants), sorted.</summary>
        private static List<string> ArmsKeys()
        {
            string map = GameLogic.Get(create: false)?.map_name;
            Logic.CoAMapping mapping = Logic.CoAMapping.GetMapping(string.IsNullOrEmpty(map) ? "europe" : map);
            if (mapping?.mapping == null)
            {
                return new List<string>();
            }
            return mapping.mapping.Where(kv => kv.Value > 0 && kv.Key.IndexOf('.') < 0).Select(kv => kv.Key)
                .OrderBy(k => k, StringComparer.Ordinal).ToList();
        }

        private static void CycleArms(Form form, int step)
        {
            if (form.ArmsKeys.Count == 0)
            {
                return;
            }
            int i = form.ArmsKeys.IndexOf(form.Arms.text.Trim());
            i = i < 0 ? (step > 0 ? 0 : form.ArmsKeys.Count - 1) : (i + step + form.ArmsKeys.Count) % form.ArmsKeys.Count;
            form.Arms.text = form.ArmsKeys[i];
        }

        private static void UpdateArmsName(Form form)
        {
            string key = form.Arms.text.Trim();
            string text = "";
            if (key.Length > 0)
            {
                text = form.ArmsKeys.Contains(key) ? string.Format(UiTexts.Get("arms_of"), Defs.Localize("tn_" + key)) : string.Format(UiTexts.Get("unknown_arms"), key);
            }
            UIText.SetText(form.ArmsName, text);
        }

        // ---- building blocks ----

        private static TextMeshProUGUI Label(RectTransform parent, string text, float size, FontStyles style)
        {
            var go = new GameObject("Label", typeof(RectTransform), typeof(TextMeshProUGUI));
            go.transform.SetParent(parent, worldPositionStays: false);
            var t = go.GetComponent<TextMeshProUGUI>();
            t.font = font;
            t.fontSize = size;
            t.fontStyle = style;
            t.color = Text;
            t.enableWordWrapping = true;
            t.raycastTarget = false;
            UIText.SetText(t, text);
            return t;
        }

        private static RectTransform Column(RectTransform parent, float width, float flexible)
        {
            var go = new GameObject("Column", typeof(RectTransform), typeof(VerticalLayoutGroup), typeof(LayoutElement));
            var rt = (RectTransform)go.transform;
            rt.SetParent(parent, worldPositionStays: false);
            var v = go.GetComponent<VerticalLayoutGroup>();
            v.spacing = 8f;
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

        /// <summary>A row with a label in front; the controls added to it share the rest of the width.</summary>
        private static RectTransform Row(RectTransform parent, string label)
        {
            var go = new GameObject("Row", typeof(RectTransform), typeof(HorizontalLayoutGroup), typeof(LayoutElement));
            var rt = (RectTransform)go.transform;
            rt.SetParent(parent, worldPositionStays: false);
            var h = go.GetComponent<HorizontalLayoutGroup>();
            h.spacing = 8f;
            h.childControlWidth = h.childControlHeight = true;
            h.childForceExpandWidth = h.childForceExpandHeight = false;
            go.GetComponent<LayoutElement>().preferredHeight = 36f;
            TextMeshProUGUI l = Label(rt, label, 18f, FontStyles.Normal);
            l.alignment = TextAlignmentOptions.MidlineLeft;
            l.gameObject.AddComponent<LayoutElement>().preferredWidth = 190f;
            return rt;
        }

        private static RectTransform ButtonRow(RectTransform parent)
        {
            var go = new GameObject("Buttons", typeof(RectTransform), typeof(HorizontalLayoutGroup), typeof(LayoutElement));
            var rt = (RectTransform)go.transform;
            rt.SetParent(parent, worldPositionStays: false);
            var h = go.GetComponent<HorizontalLayoutGroup>();
            h.spacing = 10f;
            h.childControlWidth = h.childControlHeight = true;
            h.childForceExpandWidth = true;
            h.childForceExpandHeight = false;
            go.GetComponent<LayoutElement>().preferredHeight = 40f;
            return rt;
        }

        private static TMP_InputField Input(RectTransform row, int limit)
        {
            GameObject go = TMP_DefaultControls.CreateInputField(new TMP_DefaultControls.Resources());
            go.transform.SetParent(row, worldPositionStays: false);
            var input = go.GetComponent<TMP_InputField>();
            input.fontAsset = font;
            input.pointSize = 18f;
            input.characterLimit = limit;
            input.lineType = TMP_InputField.LineType.SingleLine;
            input.textComponent.color = Text;
            if (input.placeholder is TMP_Text placeholder)
            {
                placeholder.text = "";
            }
            go.GetComponent<Image>().color = Field;
            var le = go.AddComponent<LayoutElement>();
            le.flexibleWidth = 1f;
            le.preferredHeight = 36f;
            return input;
        }

        private static Image Box(RectTransform row, float width, Color color)
        {
            var go = new GameObject("Swatch", typeof(RectTransform), typeof(Image), typeof(LayoutElement));
            go.transform.SetParent(row, worldPositionStays: false);
            var le = go.GetComponent<LayoutElement>();
            le.preferredWidth = width;
            le.preferredHeight = 36f;
            var img = go.GetComponent<Image>();
            img.color = color;
            img.raycastTarget = false;
            return img;
        }

        /// <summary>A plain button; width 0: shares the row.</summary>
        private static TextMeshProUGUI Button(RectTransform row, string text, float width, Action onClick, bool selected = false)
        {
            var go = new GameObject("Button", typeof(RectTransform), typeof(Image), typeof(Button), typeof(LayoutElement));
            go.transform.SetParent(row, worldPositionStays: false);
            go.GetComponent<Image>().color = selected ? Selected : ButtonColor;
            var le = go.GetComponent<LayoutElement>();
            le.preferredHeight = 36f;
            if (width > 0f)
            {
                le.preferredWidth = width;
            }
            else
            {
                le.flexibleWidth = 1f;
            }
            go.GetComponent<Button>().onClick.AddListener(() =>
            {
                try
                {
                    onClick();
                }
                catch (Exception e)
                {
                    Plugin.Log.LogError($"Profile editor: {e}");
                }
            });
            TextMeshProUGUI label = Label((RectTransform)go.transform, text, 18f, FontStyles.Normal);
            label.alignment = TextAlignmentOptions.Center;
            var lrt = (RectTransform)label.transform;
            lrt.anchorMin = Vector2.zero;
            lrt.anchorMax = Vector2.one;
            lrt.offsetMin = new Vector2(10f, 0f);
            lrt.offsetMax = new Vector2(-10f, 0f);
            return label;
        }
    }
}
