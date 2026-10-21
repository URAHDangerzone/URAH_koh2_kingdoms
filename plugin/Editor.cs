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
    /// The profile editor, opened from the profile list ("+ New profile", "Edit profile"): a modal window
    /// over the kingdom selection, built from plain uGUI parts in the game's font (no game window has
    /// input fields to copy on this screen). TMP input fields: the game ignores its hotkeys while one has
    /// the focus (UICommon.IsInInput). Saves through the ProfileStore, which validates like everywhere else.
    /// </summary>
    internal static class Editor
    {
        private static readonly Color Back = new Color(0.10f, 0.085f, 0.07f, 0.97f);
        private static readonly Color Field = new Color(0.24f, 0.21f, 0.18f, 1f);
        private static readonly Color ButtonColor = new Color(0.36f, 0.29f, 0.20f, 1f);
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
            public Action<string> Done;
        }

        public static void Open(UICampaignKingdomInfo panel, string id, Action<string> done)
        {
            Close();
            try
            {
                Build(panel, id, done);
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

        private static void Build(UICampaignKingdomInfo panel, string id, Action<string> done)
        {
            Canvas canvas = panel.GetComponentInParent<Canvas>()?.rootCanvas;
            if (canvas == null)
            {
                Plugin.Log.LogWarning("Profile editor: no canvas");
                return;
            }
            font = panel.GetComponentInChildren<TextMeshProUGUI>(includeInactive: true)?.font ?? TMP_Settings.defaultFontAsset;

            // full-screen blocker: nothing behind the window takes clicks
            root = new GameObject("URAH_KingdomEditor", typeof(RectTransform), typeof(Image));
            var rootRt = (RectTransform)root.transform;
            rootRt.SetParent(canvas.transform, worldPositionStays: false);
            rootRt.anchorMin = Vector2.zero;
            rootRt.anchorMax = Vector2.one;
            rootRt.offsetMin = rootRt.offsetMax = Vector2.zero;
            root.GetComponent<Image>().color = new Color(0f, 0f, 0f, 0.55f);
            rootRt.SetAsLastSibling();

            var window = new GameObject("Window", typeof(RectTransform), typeof(Image), typeof(VerticalLayoutGroup), typeof(ContentSizeFitter));
            var rt = (RectTransform)window.transform;
            rt.SetParent(rootRt, worldPositionStays: false);
            rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(0.5f, 0.5f);
            rt.sizeDelta = new Vector2(620f, 0f);
            window.GetComponent<Image>().color = Back;
            var layout = window.GetComponent<VerticalLayoutGroup>();
            layout.padding = new RectOffset(24, 24, 20, 20);
            layout.spacing = 8f;
            layout.childControlWidth = layout.childControlHeight = true;
            layout.childForceExpandWidth = true;
            layout.childForceExpandHeight = false;
            window.GetComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;

            Profile profile = id != null ? Plugin.Profiles.Load(id) : null;
            var form = new Form { Id = profile != null ? id : null, Done = done };

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
            Button(buttons, UiTexts.Get("cancel"), 0f, Close);

            form.ArmsKeys = ArmsKeys(panel.Kingdom?.game);
            if (profile != null)
            {
                Fill(form, profile);
            }
            else
            {
                // a new profile starts with the look of the kingdom it is made for
                form.Arms.text = panel.Kingdom?.Name ?? "";
                if (panel.Kingdom?.visuals is global::Kingdom view && Assignments.Get(panel.Kingdom.Name) == null)
                {
                    form.Color.text = "#" + ColorUtility.ToHtmlStringRGB(view.MapColor);
                }
            }
            UpdateSwatch(form);
            UpdateArmsName(form);
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
            Close();
            form.Done?.Invoke(id);
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
                Close();
                form.Done?.Invoke("");
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

        /// <summary>Keys of the kingdoms on the menu map that have arms, sorted.</summary>
        private static List<string> ArmsKeys(Logic.Game game)
        {
            if (game?.kingdoms == null)
            {
                return new List<string>();
            }
            return game.kingdoms.Where(k => k != null && k.CoAIndex > 0 && !string.IsNullOrEmpty(k.Name))
                .Select(k => k.Name).Distinct().OrderBy(n => n, StringComparer.Ordinal).ToList();
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
        private static TextMeshProUGUI Button(RectTransform row, string text, float width, Action onClick)
        {
            var go = new GameObject("Button", typeof(RectTransform), typeof(Image), typeof(Button), typeof(LayoutElement));
            go.transform.SetParent(row, worldPositionStays: false);
            go.GetComponent<Image>().color = ButtonColor;
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
            lrt.offsetMin = lrt.offsetMax = Vector2.zero;
            return label;
        }
    }
}
