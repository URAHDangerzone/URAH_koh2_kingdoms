using System;
using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using Logic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace URAH_Kingdoms
{
    using Action = System.Action;
    using Component = UnityEngine.Component;
    using Object = UnityEngine.Object;

    /// <summary>
    /// The profile editor, a main-menu window ("Kingdoms (URAH)" button, MainMenu). A copy of the game's
    /// Settings window (TitleUI.m_Preferences) without its logic: its caption, its close X, its tab list
    /// on the left with one tab per profile (+ New last, the language tabs hidden), its content list on
    /// the right with rows from the Settings' own row prefabs (Preferences.def: label, dropdown, button;
    /// a text field takes the dropdown's place in a dropdown row, a copy of the display name window's
    /// input), its bottom buttons as Save and Delete with a status line above them. Opening it closes the
    /// other menu windows and the other windows close it (TitleUI.CloseAnyOpenMenus), like the game's own.
    /// Saves through the ProfileStore, which validates like everywhere else; the arms come from the map's
    /// CoA table (CoAMapping), so no map is needed.
    /// </summary>
    internal static partial class Editor
    {
        private const string TabType = "urah_kingdom_profile";

        private static readonly Color Error = new Color(1f, 0.45f, 0.35f, 1f);

        private static readonly AccessTools.FieldRef<TitleUI, UIPreferences> preferences =
            AccessTools.FieldRefAccess<TitleUI, UIPreferences>("m_Preferences");

        private static GameObject root;
        private static Form current; // the open editor's

        /// <summary>DevCapture: opens the arms window, the crest browser or the colour popup over the open editor.</summary>
        internal static void DevOpen(string what)
        {
            if (root == null || current == null)
            {
                return;
            }
            Run(() =>
            {
                if (what == "arms_full" && current.Coa != null && Coa.Parts.Get() is Coa.Parts cp)
                {
                    // a few layers to see (only in the form: nothing is saved)
                    current.Coa.Division = "Division_Per_Fess";
                    current.Coa.Ordinaries.Add((cp.Catalogue.Ids[Coa.PartKind.Ordinary][0], false, false, current.Coa.Fills[0].Color));
                    foreach (string preset in new[] { Coa.Presets.Centred, Coa.Presets.Triangle })
                    {
                        current.Coa.Charges.Add(new Coa.ChargeSpec { Part = cp.Catalogue.Ids[Coa.PartKind.Charge][5], Color = current.Coa.Fills[0].Color, Preset = preset, Size = 0.3f });
                    }
                    current.Coa.Edited = true;
                }
                OpenArmsWindow(current);
                if (what == "browser")
                {
                    OpenBrowser(current);
                }
                else if (what == "color")
                {
                    OpenColorPopup(current.Parts, UiTexts.Get("f_colour"), () => new Color32(180, 33, 34, 255), c => { });
                }
            });
        }
        private static TitleUI title;

        /// <summary>The copied window's parts, and the templates its rows are made of.</summary>
        private sealed class Parts
        {
            public DT.Field Def;
            public RectTransform Tabs, Content;
            public GameObject TabPrototype, LabelRow, DropdownRow, ButtonRow, SliderRow;
            public TMP_InputField Input;
            public BSGButton Save, Delete, Close;
            public TextMeshProUGUI Caption, SaveLabel, DeleteLabel;
        }

        private sealed class Form
        {
            public string Id;
            public TMP_InputField Name, Ian, Anian, Ians, Ruler, Color;
            public Image Swatch;
            public Parts Parts;
            public string ArmsKey; // a kingdom whose arms the chosen vanilla crest is (other maps find them by it)
            public global::URAH_Kingdoms.Coa.CoaSpec Coa; // null: the kingdom's own arms
            public TextMeshProUGUI ArmsLabel; // the editor's arms row
            public readonly RawImage[] Previews = new RawImage[3]; // the editor's arms row
            public readonly RawImage[] WindowPreviews = new RawImage[3]; // the arms window, while open
            public readonly Texture2D[] PreviewTex = new Texture2D[3]; // shown by both
            public TextMeshProUGUI WindowBase;
            public RectTransform Layers; // the arms window's layer list, while open
            public TextMeshProUGUI Status;
            public TextMeshProUGUI DeleteLabel;
            public bool DeleteArmed;
        }

        /// <summary>Opens on the profile id; null: a new one.</summary>
        public static void Open(TitleUI on, string id, string status = null)
        {
            Close();
            title = on;
            try
            {
                title.CloseAnyOpenMenus(); // closes this one too: built after it
                Build(id, status);
                title.m_LogosAndText?.SetActive(false); // the window's place, as with the game's windows
            }
            catch (Exception e)
            {
                Plugin.Log.LogError($"Profile editor: {e}");
                Close();
            }
        }

        /// <summary>Opens on the first profile, or on a new one if there is none.</summary>
        public static void OpenFirst(TitleUI on) => Open(on, Plugin.Profiles.Ids().FirstOrDefault());

        public static void Close()
        {
            CloseAllPopups();
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

        // ---- the window ----

        private static Parts Copy()
        {
            UIPreferences source = preferences(title);
            if (source == null)
            {
                throw new InvalidOperationException("no settings window to copy");
            }
            // copied inactive: the copy's UIPreferences goes before it would wake up
            GameObject src = source.gameObject;
            bool active = src.activeSelf;
            src.SetActive(false);
            try
            {
                root = Object.Instantiate(src, src.transform.parent, worldPositionStays: false);
            }
            finally
            {
                src.SetActive(active);
            }
            root.name = "URAH_KingdomEditor";
            root.transform.SetSiblingIndex(src.transform.GetSiblingIndex() + 1);
            Object.DestroyImmediate(root.GetComponent<UIPreferences>());

            DT.Field def = Defs.GetDefField("Preferences");
            var parts = new Parts
            {
                Def = def,
                Tabs = Find<RectTransform>("id_CategoriesContainer"),
                Content = Find<RectTransform>("id_CategoryContent"),
                TabPrototype = Find<Transform>("id_CategoryPrototype")?.gameObject,
                LabelRow = Defs.GetObj<GameObject>(def, "label"),
                DropdownRow = Defs.GetObj<GameObject>(def, "dropdown"),
                ButtonRow = Defs.GetObj<GameObject>(def, "button"),
                SliderRow = Defs.GetObj<GameObject>(def, "slider"),
                Input = title.m_ChooseDisplayName != null
                    ? title.m_ChooseDisplayName.GetComponentsInChildren<TMP_InputField>(includeInactive: true).FirstOrDefault(i => i.name == "id_DisplayNameInput")
                    : null,
                Save = Find<BSGButton>("id_ApplyChanges"),
                SaveLabel = Find<TextMeshProUGUI>("id_ApplyChangesLabel"),
                Delete = Find<BSGButton>("id_RevertDefaults"),
                DeleteLabel = Find<TextMeshProUGUI>("id_RevertDefaultsLabel"),
                Close = Find<BSGButton>("id_Button_Close"),
                Caption = Find<TextMeshProUGUI>("id_Caption"),
            };
            var missing = new List<string>();
            if (parts.Tabs == null) missing.Add("tab list");
            if (parts.Content == null) missing.Add("content");
            if (parts.TabPrototype == null) missing.Add("tab prototype");
            if (parts.LabelRow == null) missing.Add("label row");
            if (parts.DropdownRow == null) missing.Add("dropdown row");
            if (parts.ButtonRow == null) missing.Add("button row");
            if (parts.SliderRow == null) missing.Add("slider row");
            if (parts.Input == null) missing.Add("display name input");
            if (parts.Save == null) missing.Add("apply button");
            if (missing.Count > 0)
            {
                throw new InvalidOperationException("the settings window has changed, missing: " + string.Join(", ", missing));
            }

            // what the game or other plugins (URAH_PluginManager's buttons and status line) put into it
            foreach (Transform t in root.GetComponentsInChildren<Transform>(includeInactive: true)
                .Where(t => t != root.transform && t.name.StartsWith("URAH_")).ToList())
            {
                if (t != null)
                {
                    Object.DestroyImmediate(t.gameObject);
                }
            }
            foreach (Transform child in parts.Tabs.Cast<Transform>().ToList())
            {
                if (child.gameObject != parts.TabPrototype)
                {
                    Object.DestroyImmediate(child.gameObject);
                }
            }
            foreach (UIPreferences.Category c in parts.TabPrototype.GetComponents<UIPreferences.Category>())
            {
                Object.DestroyImmediate(c);
            }
            parts.TabPrototype.SetActive(false);
            foreach (Transform child in parts.Content.Cast<Transform>().ToList())
            {
                Object.DestroyImmediate(child.gameObject);
            }
            GameObject languages = (Find<Transform>("Group_LanguageCategories") ?? Find<Transform>("id_LanguageCategoriesContainer"))?.gameObject;
            languages?.SetActive(false);
            Find<Transform>("id_ApplyChangesPopup")?.gameObject.SetActive(false);
            Find<BSGButton>("id_OpenWorkshop")?.gameObject.SetActive(false);
            return parts;

            // the outermost of that name: the tab prototype has its own id_CategoriesContainer
            T Find<T>(string name) where T : Component =>
                root.GetComponentsInChildren<T>(includeInactive: true).Where(c => c.name == name).OrderBy(c => Depth(c.transform)).FirstOrDefault();
        }

        private static int Depth(Transform t)
        {
            int depth = 0;
            for (; t != null && t != root.transform; t = t.parent)
            {
                depth++;
            }
            return depth;
        }

        private static void Build(string id, string status)
        {
            Parts parts = Copy();
            if (parts.Caption != null)
            {
                UIText.SetText(parts.Caption, UiTexts.Get("menu"));
            }
            if (parts.Close != null)
            {
                parts.Close.onClick = b => Close();
                parts.Close.Enable(true);
            }

            Profile profile = id != null ? Plugin.Profiles.Load(id) : null;
            var form = new Form { Id = id };
            current = form;

            // left: the profiles as tabs, + New last
            foreach (string pid in Plugin.Profiles.Ids())
            {
                Profile p = Plugin.Profiles.Load(pid);
                string target = pid;
                Tab(parts, pid, p != null ? p.Name : pid + " (" + UiTexts.Get("invalid") + ")", () => Open(title, target), pid == form.Id);
            }
            Tab(parts, "urah_new", UiTexts.Get("new"), () => Open(title, null), form.Id == null);

            // right: the rows
            Heading(parts, UiTexts.Get("sec_kingdom"));
            form.Name = TextRow(parts, UiTexts.Get("f_name"), Profile.MaxName);
            form.Ruler = TextRow(parts, UiTexts.Get("f_ruler"), Profile.MaxName);
            form.Color = TextRow(parts, UiTexts.Get("f_color"), 7, UiTexts.Get("color_tip"));
            form.Swatch = Swatch(form.Color);
            form.Color.onValueChanged.AddListener(_ => UpdateSwatch(form));
            ColorPicker(parts, form.Color, form.Swatch, UiTexts.Get("f_color"));
            Heading(parts, UiTexts.Get("sec_adjective"), UiTexts.Get("adjective_hint"));
            form.Ian = TextRow(parts, UiTexts.Get("f_ian"), Profile.MaxName, UiTexts.Get("adjective_hint"));
            form.Anian = TextRow(parts, UiTexts.Get("f_anian"), Profile.MaxName, UiTexts.Get("adjective_hint"));
            form.Ians = TextRow(parts, UiTexts.Get("f_ians"), Profile.MaxName, UiTexts.Get("adjective_hint"));
            ArmsSection(parts, form);
            Heading(parts, UiTexts.Get("sec_code"), UiTexts.Get("code_tip"));
            ButtonRow(parts, UiTexts.Get("copy_code"), UiTexts.Get("copy"), () => CopyCode(form));
            ButtonRow(parts, UiTexts.Get("paste_code"), UiTexts.Get("paste"), () => PasteCode(form));

            // bottom: Save in Apply Changes' place, Delete in Revert Defaults'
            Wire(parts.Save, parts.SaveLabel, UiTexts.Get("save"), () => Save(form));
            if (parts.Delete != null)
            {
                parts.Delete.gameObject.SetActive(form.Id != null);
                if (form.Id != null)
                {
                    Wire(parts.Delete, parts.DeleteLabel, UiTexts.Get("delete"), () => Delete(form));
                    form.DeleteLabel = parts.DeleteLabel;
                }
            }

            root.SetActive(true);
            Canvas.ForceUpdateCanvases();
            LayoutRebuilder.ForceRebuildLayoutImmediate((RectTransform)root.transform);
            form.Status = StatusLine(parts);

            if (profile != null)
            {
                Fill(form, profile);
            }
            else if (id != null)
            {
                Status(form, string.Format(UiTexts.Get("invalid_file"), id), error: true); // the log has why
            }
            UpdateSwatch(form);
            RebuildArms(form);
            if (status != null)
            {
                Status(form, status, error: false);
            }
        }

        // ---- the profile ----

        private static void Fill(Form form, Profile p)
        {
            form.Name.text = p.Name ?? "";
            form.Ruler.text = p.Ruler ?? "";
            form.Color.text = p.Color ?? "";
            SetArms(form, p);
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
                Arms = form.Coa != null ? form.ArmsKey : null,
                Coa = form.Coa?.ToString(),
                Adjective = new Adjective { Ian = form.Ian.text, Anian = form.Anian.text, Ians = form.Ians.text },
            };
            var errors = p.Normalize();
            if (errors.Count > 0)
            {
                Status(form, string.Join(" ", errors), error: true);
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
                if (form.DeleteLabel != null)
                {
                    UIText.SetText(form.DeleteLabel, UiTexts.Get("delete_confirm"));
                }
                return;
            }
            if (Plugin.Profiles.Delete(form.Id))
            {
                Plugin.Log.LogInfo($"Profile '{form.Id}' deleted");
                OpenFirst(title);
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
            RebuildArms(form);
            Status(form, UiTexts.Get("pasted"), error: false);
        }

        private static void Status(Form form, string text, bool error)
        {
            if (form.Status == null)
            {
                return;
            }
            form.Status.color = error ? Error : Defs.GetColor("Preferences", "category_settings.color_label_normal");
            UIText.SetText(form.Status, text);
            form.Status.gameObject.SetActive(!string.IsNullOrEmpty(text));
        }

        private static void UpdateSwatch(Form form)
        {
            string text = form.Color.text.Trim();
            form.Swatch.color = text.Length == 7 && ColorUtility.TryParseHtmlString(text, out Color c) ? c : new Color(1f, 1f, 1f, 0.15f);
        }

        // ---- arms ----

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

        // ---- building blocks: the settings window's own ----

        private static void Tab(Parts parts, string key, string text, Action onSelect, bool selected)
        {
            var def = new DT.Field(parts.Def?.dt) { key = key, type = TabType, children = new List<DT.Field>() };
            var tab = UIPreferences.Category.Create<ProfileTab>(def, parts.TabPrototype, parts.Tabs) as ProfileTab;
            if (tab == null)
            {
                return;
            }
            tab.name = "Tab";
            tab.OnSelect = c => Run(onSelect);
            tab.Show(text, selected);
        }

        /// <summary>A row of the settings' prefab, without its preference logic, its label set.</summary>
        private static GameObject Row(Parts parts, GameObject prefab, string label, string tip = null, Transform parent = null)
        {
            GameObject row = Object.Instantiate(prefab, parent ?? parts.Content, worldPositionStays: false);
            row.name = "Row";
            foreach (UIPreferences.Preference p in row.GetComponentsInChildren<UIPreferences.Preference>(includeInactive: true))
            {
                Object.DestroyImmediate(p);
            }
            // the "needs a restart" note: its container, and the label that some prefabs show outside it
            Child<Transform>(row, "id_RestartWarning")?.gameObject.SetActive(false);
            Child<Transform>(row, "id_RestartWarningLabel")?.gameObject.SetActive(false);
            TextMeshProUGUI l = Child<TextMeshProUGUI>(row, "id_Label");
            if (l != null)
            {
                UIText.SetText(l, label);
                l.raycastTarget = tip != null;
                if (tip != null)
                {
                    Tooltip.Get(l.gameObject).SetText("#" + tip);
                }
                else
                {
                    Tooltip.Get(l.gameObject).Clear();
                }
            }
            row.SetActive(true);
            return row;
        }

        private static void Heading(Parts parts, string text, string tip = null) => Row(parts, parts.LabelRow, text, tip);

        /// <summary>A dropdown row whose dropdown makes way for a text field in its place and size.</summary>
        private static TMP_InputField TextRow(Parts parts, string label, int limit, string tip = null)
        {
            GameObject row = Row(parts, parts.DropdownRow, label, tip);
            TMP_Dropdown dropdown = Child<TMP_Dropdown>(row, "id_Dropdown");
            if (dropdown == null)
            {
                throw new InvalidOperationException("the settings' dropdown row has no id_Dropdown");
            }
            TMP_InputField input = Object.Instantiate(parts.Input, dropdown.transform.parent, worldPositionStays: false);
            input.name = "Input";
            Take((RectTransform)dropdown.transform, (RectTransform)input.transform);
            if (dropdown.captionText != null && input.textComponent != null)
            {
                input.textComponent.enableAutoSizing = false;
                input.textComponent.fontSize = dropdown.captionText.fontSize;
            }
            Object.DestroyImmediate(dropdown.gameObject);

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
            input.interactable = true;
            input.gameObject.SetActive(true);
            return input;
        }

        /// <summary>
        /// to takes from's place: its rect, its sibling slot, and the size a layout group would give it
        /// (its layout element overrides the one the copied input brings).
        /// </summary>
        private static void Take(RectTransform from, RectTransform to)
        {
            to.anchorMin = from.anchorMin;
            to.anchorMax = from.anchorMax;
            to.pivot = from.pivot;
            to.anchoredPosition = from.anchoredPosition;
            to.sizeDelta = from.sizeDelta;
            to.localScale = from.localScale;
            to.SetSiblingIndex(from.GetSiblingIndex());
            float minW = LayoutUtility.GetMinWidth(from), prefW = LayoutUtility.GetPreferredWidth(from), flexW = LayoutUtility.GetFlexibleWidth(from);
            float minH = LayoutUtility.GetMinHeight(from), prefH = LayoutUtility.GetPreferredHeight(from), flexH = LayoutUtility.GetFlexibleHeight(from);
            LayoutElement fromElement = from.GetComponent<LayoutElement>();
            bool ignore = fromElement != null && fromElement.ignoreLayout;
            if (to.GetComponent<LayoutElement>() == null)
            {
                to.gameObject.AddComponent<LayoutElement>();
            }
            foreach (LayoutElement le in to.GetComponents<LayoutElement>())
            {
                le.layoutPriority = 99;
                le.ignoreLayout = ignore;
                le.minWidth = minW;
                le.preferredWidth = prefW;
                le.flexibleWidth = flexW;
                le.minHeight = minH;
                le.preferredHeight = prefH;
                le.flexibleHeight = flexH;
            }
        }

        /// <summary>The colour preview: a square at the right end of the colour field; a click opens the colour popup.</summary>
        private static Image Swatch(TMP_InputField input)
        {
            var go = new GameObject("Swatch", typeof(RectTransform), typeof(Image), typeof(LayoutElement));
            var rt = (RectTransform)go.transform;
            rt.SetParent(input.transform, worldPositionStays: false);
            rt.anchorMin = new Vector2(1f, 0.18f);
            rt.anchorMax = new Vector2(1f, 0.82f);
            rt.pivot = new Vector2(1f, 0.5f);
            rt.sizeDelta = new Vector2(40f, 0f);
            rt.anchoredPosition = new Vector2(-8f, 0f);
            go.GetComponent<LayoutElement>().ignoreLayout = true;
            var img = go.GetComponent<Image>();
            img.raycastTarget = true; // ColorPicker opens its popup from it
            return img;
        }

        private static GameObject ButtonRow(Parts parts, string label, string text, Action onClick, Transform parent = null)
        {
            GameObject row = Row(parts, parts.ButtonRow, label, parent: parent);
            BSGButton button = Child<BSGButton>(row, "id_Button");
            if (button != null)
            {
                Wire(button, Child<TextMeshProUGUI>(row, "id_ButtonLabel"), text, onClick);
            }
            return row;
        }

        private static void Wire(BSGButton button, TextMeshProUGUI label, string text, Action onClick)
        {
            button.onClick = b => Run(onClick);
            button.Enable(true);
            if (label != null)
            {
                UIText.SetText(label, text);
            }
            button.gameObject.SetActive(true);
        }

        private static void Run(Action action)
        {
            try
            {
                action();
            }
            catch (Exception e)
            {
                Plugin.Log.LogError($"Profile editor: {e}");
            }
        }

        /// <summary>
        /// The status line: right above the bottom buttons, as wide as the content's scroll view, in the
        /// buttons' font (as URAH_PluginManager's Plugins tab has it). Fixed, so a message never moves the rows.
        /// </summary>
        private static TextMeshProUGUI StatusLine(Parts parts)
        {
            ScrollRect scroll = parts.Content.GetComponentInParent<ScrollRect>();
            Transform parent = parts.Save.transform.parent;
            var corners = new Vector3[4];
            ((RectTransform)parts.Save.transform).GetWorldCorners(corners);
            float buttonTop = corners[1].y, buttonHeight = corners[1].y - corners[0].y;
            ((RectTransform)(scroll != null ? scroll.transform : parts.Content)).GetWorldCorners(corners);
            float left = corners[0].x, right = corners[2].x;

            var go = new GameObject("Status", typeof(RectTransform), typeof(TextMeshProUGUI), typeof(LayoutElement));
            go.GetComponent<LayoutElement>().ignoreLayout = true;
            var rt = (RectTransform)go.transform;
            rt.SetParent(parent, worldPositionStays: false);
            Vector3 scale = parent.lossyScale;
            rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f);
            rt.pivot = Vector2.zero;
            rt.position = new Vector3(left, buttonTop + buttonHeight * 0.1f, rt.position.z);
            rt.sizeDelta = new Vector2((right - left) / scale.x, buttonHeight * 0.55f / scale.y);

            var text = go.GetComponent<TextMeshProUGUI>();
            TextMeshProUGUI style = parts.SaveLabel ?? parts.Save.GetComponentInChildren<TextMeshProUGUI>(includeInactive: true);
            if (style != null)
            {
                text.font = style.font;
                text.fontSharedMaterial = style.fontSharedMaterial;
                text.fontSizeMax = style.fontSize * 0.8f;
            }
            text.enableAutoSizing = true;
            text.fontSizeMin = 8f;
            text.alignment = TextAlignmentOptions.Midline;
            text.overflowMode = TextOverflowModes.Ellipsis;
            text.enableWordWrapping = false;
            text.raycastTarget = false;
            go.SetActive(false);
            return text;
        }

        private static T Child<T>(GameObject go, string name) where T : Component =>
            go.GetComponentsInChildren<T>(includeInactive: true).FirstOrDefault(c => c.name == name);
    }

    /// <summary>A tab of the editor: the settings' tab look (category_settings), a profile's name, no sub tabs.</summary>
    public class ProfileTab : UIPreferences.Category
    {
        private string text;

        internal void Show(string name, bool selected)
        {
            text = name;
            Select(selected);
            Refresh();
        }

        public override void SetDefField(DT.Field def)
        {
            base.SetDefField(def);
            settings = Defs.GetDefField("Preferences")?.FindChild("category_settings");
            Refresh();
        }

        protected override void Refresh()
        {
            if (m_CategoryName != null)
            {
                UIText.SetText(m_CategoryName, text ?? "");
            }
            if (m_CategoriesContainer != null)
            {
                m_CategoriesContainer.gameObject.SetActive(false);
            }
            Updatehighlight();
        }
    }
}
