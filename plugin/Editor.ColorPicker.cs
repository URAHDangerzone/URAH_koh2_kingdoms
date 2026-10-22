using System;
using System.Collections.Generic;
using System.Linq;
using Logic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace URAH_Kingdoms
{
    using Action = System.Action;
    using Object = UnityEngine.Object;

    internal static partial class Editor
    {
        // ---- popups: copies of the game's system window ----

        /// <summary>The open popups, the top one last (the colour popup can open over the arms window).</summary>
        private static readonly List<GameObject> popups = new List<GameObject>();

        /// <summary>Closes the top popup.</summary>
        private static void ClosePopup()
        {
            popups.RemoveAll(p => p == null);
            if (popups.Count > 0)
            {
                GameObject top = popups[popups.Count - 1];
                popups.RemoveAt(popups.Count - 1);
                Object.Destroy(top);
            }
        }

        private static void CloseAllPopups()
        {
            foreach (GameObject p in popups)
            {
                if (p != null)
                {
                    Object.Destroy(p);
                }
            }
            popups.Clear();
        }

        /// <summary>
        /// A popup as the game's system windows look: a copy of the display name window's frame (background with
        /// footer, caption frame, its first button as OK in the footer), centred over the editor, the rest dimmed.
        /// Its rows go into the returned list (a vertical layout, as wide as the window less its margins);
        /// FitPopup sizes the window to them. OK or a click outside closes it.
        /// </summary>
        private static RectTransform OpenSystemPopup(string caption, float width)
        {
            Transform template = title.m_ChooseDisplayName != null ? title.m_ChooseDisplayName.transform.Find("RegisterWindow") : null;
            if (template == null)
            {
                throw new InvalidOperationException("the display name window has no RegisterWindow to copy");
            }

            // the dimmed rest of the screen: a click there closes the popup
            var popup = new GameObject("Popup", typeof(RectTransform), typeof(Image), typeof(Button), typeof(LayoutElement));
            popups.Add(popup);
            var catcher = (RectTransform)popup.transform;
            // on the whole screen (the editor sits right of the menu): centred there, the rest dimmed
            Canvas canvas = root.GetComponentInParent<Canvas>();
            catcher.SetParent(canvas != null ? canvas.rootCanvas.transform : root.transform, worldPositionStays: false);
            catcher.anchorMin = Vector2.zero;
            catcher.anchorMax = Vector2.one;
            catcher.offsetMin = catcher.offsetMax = Vector2.zero;
            popup.GetComponent<LayoutElement>().ignoreLayout = true;
            popup.GetComponent<Image>().color = new Color(0f, 0f, 0f, 0.45f);
            popup.GetComponent<Button>().transition = Selectable.Transition.None;
            popup.GetComponent<Button>().onClick.AddListener(ClosePopup);
            catcher.SetAsLastSibling();

            GameObject window = Object.Instantiate(template.gameObject, catcher, worldPositionStays: false);
            window.name = "Window";
            window.SetActive(true);
            var wrt = (RectTransform)window.transform;
            wrt.anchorMin = wrt.anchorMax = wrt.pivot = new Vector2(0.5f, 0.5f);
            wrt.anchoredPosition = Vector2.zero;
            wrt.sizeDelta = new Vector2(width, 400f);
            // the frame's parts are placed for its own size: stretched to the window, the caption centred on top
            foreach (string part in new[] { "Background", "Body", "Body/Buttons" })
            {
                if (window.transform.Find(part) is RectTransform p)
                {
                    p.anchorMin = Vector2.zero;
                    p.anchorMax = Vector2.one;
                    p.pivot = new Vector2(0.5f, 0.5f);
                    p.offsetMin = p.offsetMax = Vector2.zero;
                }
            }
            if (window.transform.Find("CaptionFrame") is RectTransform cf)
            {
                float y = cf.anchoredPosition.y;
                cf.anchorMin = cf.anchorMax = new Vector2(0.5f, 1f);
                cf.anchoredPosition = new Vector2(0f, y);
            }
            Transform body = window.transform.Find("Body");
            foreach (Transform child in body.Cast<Transform>().ToList())
            {
                if (child.name != "Buttons")
                {
                    Object.DestroyImmediate(child.gameObject);
                }
            }
            TextMeshProUGUI captionLabel = Child<TextMeshProUGUI>(window, "id_CaptionLabel");
            if (captionLabel != null)
            {
                UIText.SetText(captionLabel, caption);
            }
            Child<Transform>(window, "btn_Back")?.gameObject.SetActive(false);
            BSGButton ok = Child<BSGButton>(window, "btn_ChooseDisplayName");
            if (ok != null)
            {
                Wire(ok, Child<TextMeshProUGUI>(window, "id_ChooseNameLabel"), UiTexts.Get("ok"), ClosePopup);
                var ort = (RectTransform)ok.transform;
                ort.pivot = new Vector2(0.5f, 0f);
                ort.anchoredPosition = new Vector2(0f, 26f);
            }

            var content = new GameObject("Content", typeof(RectTransform), typeof(VerticalLayoutGroup), typeof(ContentSizeFitter));
            var rt = (RectTransform)content.transform;
            rt.SetParent(body, worldPositionStays: false);
            rt.SetAsFirstSibling();
            rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(0.5f, 1f);
            rt.anchoredPosition = new Vector2(0f, -PopupTop);
            rt.sizeDelta = new Vector2(width - 2f * PopupSide, 0f); // inside the frame's inner border
            var layout = content.GetComponent<VerticalLayoutGroup>();
            layout.padding = new RectOffset(0, 0, 0, 18); // clear of the frame's inner border
            layout.spacing = 4f;
            layout.childControlWidth = layout.childControlHeight = true;
            layout.childForceExpandWidth = true;
            layout.childForceExpandHeight = false;
            content.GetComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            return rt;
        }

        private const float PopupTop = 78f, PopupFooter = 130f, PopupSide = 120f;

        /// <summary>The popup's window as high as its rows, the caption above and the footer below.</summary>
        private static void FitPopup(RectTransform content)
        {
            LayoutRebuilder.ForceRebuildLayoutImmediate(content);
            var window = (RectTransform)content.parent.parent;
            window.sizeDelta = new Vector2(window.sizeDelta.x, content.rect.height + PopupTop + PopupFooter);
            LayoutRebuilder.ForceRebuildLayoutImmediate(window);
        }

        // ---- the colour popup ----

        /// <summary>A click on the swatch opens the colour popup for the #RRGGBB field (empty: none).</summary>
        private static void ColorPicker(Parts parts, TMP_InputField hex, Image swatch, string caption)
        {
            ClickToPick(swatch, () => OpenColorPopup(parts, caption,
                () => TryParse(hex.text, out Color32 c) ? c : (Color32?)null,
                c => hex.text = "#" + ColorUtility.ToHtmlStringRGB(c)));
        }

        private static void ClickToPick(Image swatch, Action open)
        {
            Button button = swatch.gameObject.GetComponent<Button>();
            if (button == null) // Unity's null: no ?? on components
            {
                button = swatch.gameObject.AddComponent<Button>();
            }
            button.targetGraphic = swatch;
            button.transition = Selectable.Transition.None;
            swatch.raycastTarget = true;
            Tooltip.Get(swatch.gameObject).SetText("#" + UiTexts.Get("pick_color"));
            button.onClick.RemoveAllListeners();
            button.onClick.AddListener(() => Run(open));
        }

        /// <summary>
        /// The colour popup: a bar in the colour, red, green and blue as the settings' slider rows (0-255, the
        /// value shown, the fill in the channel's colour) and the heraldic colours of the game's arms
        /// (CoatOfArmsPaletteColors) as swatches to click. get gives the colour to start from (null: none, grey),
        /// set takes every change at once.
        /// </summary>
        private static void OpenColorPopup(Parts parts, string caption, Func<Color32?> get, Action<Color32> set)
        {
            RectTransform rt = OpenSystemPopup(caption, 820f);

            var bar = new GameObject("Preview", typeof(RectTransform), typeof(Image), typeof(LayoutElement));
            bar.transform.SetParent(rt, worldPositionStays: false);
            bar.GetComponent<LayoutElement>().preferredHeight = 34f;
            Image preview = bar.GetComponent<Image>();
            preview.raycastTarget = false;

            var sliders = new Slider[3];
            var values = new TextMeshProUGUI[3];
            string[] labels = { "f_red", "f_green", "f_blue" };
            Color[] fills = { new Color(0.85f, 0.25f, 0.25f), new Color(0.3f, 0.75f, 0.3f), new Color(0.3f, 0.45f, 0.9f) };
            for (int i = 0; i < 3; i++)
            {
                sliders[i] = SliderRow(parts, rt, UiTexts.Get(labels[i]), 0f, 255f, true, out values[i]);
                Image fill = sliders[i].fillRect != null ? sliders[i].fillRect.GetComponent<Image>() : null;
                if (fill != null)
                {
                    fill.color = fills[i];
                }
                sliders[i].onValueChanged.AddListener(_ => Write());
            }
            PaletteRow(parts, rt, c =>
            {
                set(c);
                Follow();
            });

            Follow();
            FitPopup(rt);

            void Follow()
            {
                Color32? has = get();
                Color32 c = has ?? new Color32(128, 128, 128, 255);
                Show(c.r, c.g, c.b);
                preview.color = has.HasValue ? (Color)c : new Color(1f, 1f, 1f, 0.15f);
            }

            void Write()
            {
                var c = new Color32((byte)sliders[0].value, (byte)sliders[1].value, (byte)sliders[2].value, 255);
                Show(c.r, c.g, c.b);
                preview.color = c;
                set(c);
            }

            void Show(byte r, byte g, byte b)
            {
                byte[] v = { r, g, b };
                for (int i = 0; i < 3; i++)
                {
                    sliders[i].SetValueWithoutNotify(v[i]);
                    if (values[i] != null)
                    {
                        UIText.SetText(values[i], v[i].ToString());
                    }
                }
            }
        }

        /// <summary>A slider row of the settings (its "needs a restart" note hidden by Row), its value label returned.</summary>
        private static Slider SliderRow(Parts parts, Transform parent, string label, float min, float max, bool whole, out TextMeshProUGUI value)
        {
            GameObject r = Row(parts, parts.SliderRow, label, parent: parent);
            Slider slider = Child<Slider>(r, "id_Slider");
            if (slider == null)
            {
                throw new InvalidOperationException("the settings' slider row has no id_Slider");
            }
            slider.onValueChanged.RemoveAllListeners();
            slider.minValue = min;
            slider.maxValue = max;
            slider.wholeNumbers = whole;
            slider.interactable = true;
            value = Child<TextMeshProUGUI>(r, "id_ValueLabel");
            return slider;
        }

        private static bool TryParse(string text, out Color32 c)
        {
            c = default;
            text = text?.Trim();
            if (text == null || text.Length != 7 || text[0] != '#' || !ColorUtility.TryParseHtmlString(text, out Color color))
            {
                return false;
            }
            c = color;
            return true;
        }

        /// <summary>A dropdown row whose dropdown makes way for a strip of swatches, one per heraldic colour.</summary>
        private static void PaletteRow(Parts parts, Transform parent, Action<Color> pick)
        {
            List<KeyValuePair<string, Color>> palette = Palette();
            if (palette.Count == 0)
            {
                return;
            }
            RectTransform rt = ReplaceDropdown(Row(parts, parts.DropdownRow, UiTexts.Get("f_palette"), UiTexts.Get("palette_tip"), parent),
                new GameObject("Palette", typeof(RectTransform), typeof(HorizontalLayoutGroup)));
            var layout = rt.GetComponent<HorizontalLayoutGroup>();
            layout.spacing = 3f;
            layout.padding = new RectOffset(0, 0, 4, 4);
            layout.childControlWidth = layout.childControlHeight = true;
            layout.childForceExpandWidth = layout.childForceExpandHeight = true;

            foreach (KeyValuePair<string, Color> kv in palette)
            {
                var go = new GameObject(kv.Key, typeof(RectTransform), typeof(Image), typeof(Button));
                go.transform.SetParent(rt, worldPositionStays: false);
                var img = go.GetComponent<Image>();
                img.color = kv.Value;
                var outline = go.AddComponent<Outline>(); // Sable would vanish on the dark window
                outline.effectColor = new Color(0.75f, 0.68f, 0.5f, 0.6f);
                outline.effectDistance = new Vector2(1f, -1f);
                var button = go.GetComponent<Button>();
                button.targetGraphic = img;
                Color color = kv.Value;
                button.onClick.AddListener(() => Run(() => pick(color)));
                Tooltip.Get(go).SetText("#" + kv.Key);
            }
        }

        /// <summary>The row's dropdown makes way for go, in its place and size; go's RectTransform returned.</summary>
        private static RectTransform ReplaceDropdown(GameObject row, GameObject go)
        {
            TMP_Dropdown dropdown = Child<TMP_Dropdown>(row, "id_Dropdown");
            if (dropdown == null)
            {
                throw new InvalidOperationException("the settings' dropdown row has no id_Dropdown");
            }
            var rt = (RectTransform)go.transform;
            rt.SetParent(dropdown.transform.parent, worldPositionStays: false);
            Take((RectTransform)dropdown.transform, rt);
            Object.DestroyImmediate(dropdown.gameObject);
            return rt;
        }

        /// <summary>CoatOfArmsPaletteColors (Defs/CoatOfArms/coatOfArms.def): name and colour, in the def's order.</summary>
        private static List<KeyValuePair<string, Color>> Palette()
        {
            var list = new List<KeyValuePair<string, Color>>();
            DT.Field def = Defs.GetDefField("CoatOfArmsPaletteColors");
            if (def?.children == null)
            {
                return list;
            }
            foreach (DT.Field f in def.children)
            {
                if (!string.IsNullOrEmpty(f.key) && f.value_str != null)
                {
                    list.Add(new KeyValuePair<string, Color>(f.key, Defs.ColorFromString(f.value_str, Color.white)));
                }
            }
            return list;
        }
    }
}
