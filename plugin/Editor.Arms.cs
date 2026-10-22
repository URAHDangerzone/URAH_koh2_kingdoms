using System;
using System.Collections.Generic;
using System.Linq;
using Logic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using URAH_Kingdoms.Coa;

namespace URAH_Kingdoms
{
    using Action = System.Action;
    using Object = UnityEngine.Object;
    using CoaParts = URAH_Kingdoms.Coa.Parts;

    internal static partial class Editor
    {
        // ---- the editor's arms rows ----

        /// <summary>The editor's arms: a small preview of shield, flag and banner, and a row naming the arms with "Edit",
        /// which opens the arms window.</summary>
        private static void ArmsSection(Parts parts, Form form)
        {
            form.Parts = parts;
            Heading(parts, UiTexts.Get("sec_arms"));
            PreviewStrip(parts.Content, form.Previews, new[] { new Vector2(57f, 76f), new Vector2(124f, 62f), new Vector2(38f, 76f) }, 84f);
            GameObject row = ButtonRow(parts, "", UiTexts.Get("edit_arms"), () => OpenArmsWindow(form));
            form.ArmsLabel = Child<TextMeshProUGUI>(row, "id_Label");
        }

        /// <summary>After the arms changed: the names, the previews, and the window's layer list when the structure changed.</summary>
        private static void RebuildArms(Form form, bool layers = true)
        {
            string text = ArmsText(form);
            if (form.ArmsLabel != null)
            {
                UIText.SetText(form.ArmsLabel, text);
            }
            if (form.WindowBase != null)
            {
                UIText.SetText(form.WindowBase, text);
            }
            if (layers && form.Layers != null)
            {
                LayerRows(form);
            }
            UpdatePreview(form);
        }

        private static string ArmsText(Form form) =>
            form.Coa == null ? UiTexts.Get("own_arms")
                : UiTexts.Get("f_base") + ": " + (form.Coa.Base.HasValue ? CrestName(form.Coa.Base.Value) : UiTexts.Get("own_design"))
                  + (form.Coa.Edited ? " " + UiTexts.Get("edited") : "");

        /// <summary>The profile's arms into the form: its coa, else its kingdom arms as that kingdom's vanilla crest.</summary>
        private static void SetArms(Form form, Profile p)
        {
            form.Coa = null;
            form.ArmsKey = p.Arms;
            CoaParts cp = CoaParts.Get();
            if (p.Coa != null)
            {
                form.Coa = CoaSpec.Parse(p.Coa, cp?.Palette ?? CrestDefs.Palette(), out _);
            }
            else if (p.Arms != null)
            {
                int id = CoAMapping.GetMapping("europe")?.Get(p.Arms) ?? 0;
                if (id > 0)
                {
                    form.Coa = new CoaSpec { Base = id };
                }
            }
            // a vanilla crest as it is: its layers ready to edit, still saved as the crest alone
            if (form.Coa != null && !form.Coa.Edited && form.Coa.Base.HasValue && cp != null && cp.Crests.TryGetValue(form.Coa.Base.Value, out Crest c))
            {
                form.Coa = CoaSpec.FromCrest(c, cp.Catalogue, cp.Palette);
                form.Coa.Edited = false;
            }
        }

        /// <summary>A change to a layer: the arms count as edited from now on; the list is built again if its rows changed.</summary>
        private static void Changed(Form form, bool rebuild = false)
        {
            if (form.Coa != null)
            {
                form.Coa.Edited = true;
            }
            RebuildArms(form, rebuild);
        }

        // ---- the arms window ----

        /// <summary>
        /// The arms window: on the left, never scrolling, the preview (shield, flag, banner), what the arms start
        /// from and the buttons to choose a vanilla crest or go back to the kingdom's own; on the right the layers,
        /// one line each in the game's own terms: the division with its fields (a colour, or a pattern in its
        /// colours), the ordinaries, the charges (placement, size). Changes show at once and go into the profile
        /// with Save.
        /// </summary>
        private static void OpenArmsWindow(Form form)
        {
            CoaParts cp = CoaParts.Get();
            if (cp == null)
            {
                Status(form, UiTexts.Get("arms_failed"), error: true);
                return;
            }
            Parts parts = form.Parts;
            const float width = 1440f, height = 560f;
            RectTransform content = OpenSystemPopup(UiTexts.Get("sec_arms"), width);

            var body = new GameObject("Body", typeof(RectTransform), typeof(HorizontalLayoutGroup), typeof(LayoutElement));
            body.transform.SetParent(content, worldPositionStays: false);
            body.GetComponent<LayoutElement>().preferredHeight = height;
            var hl = body.GetComponent<HorizontalLayoutGroup>();
            hl.padding = new RectOffset(40, 0, 0, 0); // the frame's inner border is further in on the left
            hl.spacing = 20f;
            hl.childControlWidth = hl.childControlHeight = true;
            hl.childForceExpandWidth = false;
            hl.childForceExpandHeight = true;

            // left: preview, base, buttons (one under the other), the rest empty
            RectTransform left = Column(body.transform, 300f, 0f);
            PreviewStrip(left, form.WindowPreviews, new[] { new Vector2(150f, 200f) }, 210f, first: 0);
            PreviewStrip(left, form.WindowPreviews, new[] { new Vector2(200f, 100f), new Vector2(50f, 100f) }, 110f, first: 1);
            GameObject baseRow = Row(parts, parts.LabelRow, "", parent: left);
            form.WindowBase = Child<TextMeshProUGUI>(baseRow, "id_Label");
            if (form.WindowBase != null)
            {
                form.WindowBase.enableWordWrapping = true;
            }
            foreach (LayoutElement le in baseRow.GetComponents<LayoutElement>())
            {
                le.flexibleHeight = 0f;
            }
            SmallButton(parts, Line(left, 46f, center: true), UiTexts.Get("choose_base"), 280f, () => OpenBrowser(form));
            SmallButton(parts, Line(left, 46f, center: true), UiTexts.Get("own_arms_button"), 280f, () =>
            {
                form.Coa = null;
                form.ArmsKey = null;
                RebuildArms(form);
            });
            Spacer(left, vertical: true);

            // right: the layers, scrolling when there are many
            RectTransform right = Column(body.transform, 0f, 1f);
            form.Layers = ScrollList(right);
            form.Layers.gameObject.AddComponent<WindowWatch>().OnGone = () =>
            {
                form.Layers = null;
                form.WindowBase = null;
                for (int i = 0; i < 3; i++)
                {
                    form.WindowPreviews[i] = null;
                }
            };

            FitPopup(content);
            RebuildArms(form);
        }

        /// <summary>Tells when the arms window is gone (closed by OK, a click outside or the editor closing).</summary>
        private sealed class WindowWatch : MonoBehaviour
        {
            public Action OnGone;

            private void OnDestroy() => OnGone?.Invoke();
        }

        private static RectTransform Column(Transform parent, float width, float flex)
        {
            var go = new GameObject("Column", typeof(RectTransform), typeof(VerticalLayoutGroup), typeof(LayoutElement));
            go.transform.SetParent(parent, worldPositionStays: false);
            var le = go.GetComponent<LayoutElement>();
            le.preferredWidth = width;
            le.flexibleWidth = flex;
            var vl = go.GetComponent<VerticalLayoutGroup>();
            vl.spacing = 6f;
            vl.childAlignment = TextAnchor.UpperCenter;
            vl.childControlWidth = vl.childControlHeight = true;
            vl.childForceExpandWidth = true;
            vl.childForceExpandHeight = false;
            return (RectTransform)go.transform;
        }

        /// <summary>A scroll view filling its parent; the returned list grows with its rows.</summary>
        private static RectTransform ScrollList(Transform parent)
        {
            var scroll = new GameObject("Scroll", typeof(RectTransform), typeof(Image), typeof(RectMask2D), typeof(ScrollRect), typeof(LayoutElement));
            scroll.transform.SetParent(parent, worldPositionStays: false);
            scroll.GetComponent<LayoutElement>().flexibleHeight = 1f;
            scroll.GetComponent<Image>().color = new Color(0f, 0f, 0f, 0.15f);
            var list = new GameObject("List", typeof(RectTransform), typeof(VerticalLayoutGroup), typeof(ContentSizeFitter));
            var lrt = (RectTransform)list.transform;
            lrt.SetParent(scroll.transform, worldPositionStays: false);
            lrt.anchorMin = new Vector2(0f, 1f);
            lrt.anchorMax = new Vector2(1f, 1f);
            lrt.pivot = new Vector2(0.5f, 1f);
            lrt.offsetMin = lrt.offsetMax = Vector2.zero;
            var vl = list.GetComponent<VerticalLayoutGroup>();
            vl.padding = new RectOffset(12, 12, 10, 10);
            vl.spacing = 6f;
            vl.childControlWidth = vl.childControlHeight = true;
            vl.childForceExpandWidth = true;
            vl.childForceExpandHeight = false;
            list.GetComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            var sr = scroll.GetComponent<ScrollRect>();
            sr.content = lrt;
            sr.viewport = (RectTransform)scroll.transform;
            sr.horizontal = false;
            sr.movementType = ScrollRect.MovementType.Clamped;
            sr.scrollSensitivity = 40f;
            return lrt;
        }

        // ---- the layer lines ----

        private static void LayerRows(Form form)
        {
            RectTransform list = form.Layers;
            foreach (Transform child in list.Cast<Transform>().ToList())
            {
                Object.DestroyImmediate(child.gameObject);
            }
            Parts parts = form.Parts;
            CoaParts cp = CoaParts.Get();
            CoaSpec s = form.Coa;
            if (s == null || cp == null)
            {
                Row(parts, parts.LabelRow, UiTexts.Get("own_arms_hint"), parent: list);
                return;
            }
            Catalogue cat = cp.Catalogue;

            // the division: its kind, then per field a colour, or a pattern and its colours
            List<string> divisions = cat.Ids[PartKind.Division].OrderBy(id => id == "Solid_Color" ? 0 : 1).ToList();
            List<string> patterns = new List<string> { null }.Concat(cat.Ids[PartKind.Pattern]).ToList();
            RectTransform line = Line(list, 50f);
            LineLabel(parts, line, UiTexts.Get("f_division"));
            Dropdown(parts, line, 220f, divisions.Select(id => id == "Solid_Color" ? UiTexts.Get("plain") : cat.Name(id)).ToList(),
                divisions.IndexOf(s.Division), i =>
                {
                    s.Division = divisions[i];
                    Changed(form, rebuild: true);
                });
            bool plain = s.Division == "Solid_Color";
            for (int f = 0; f < 2; f++)
            {
                Fill fill = s.Fills[f];
                line = Line(list, 50f);
                LineLabel(parts, line, UiTexts.Get(f == 0 ? (plain ? "f_field" : "f_field1") : "f_field2"));
                if (f == 1 && plain)
                {
                    Object.DestroyImmediate(line.gameObject);
                    break;
                }
                if (fill.Pattern == null)
                {
                    Swatch(form, line, UiTexts.Get("f_colour"), () => fill.Color, t => fill.Color = t);
                }
                Dropdown(parts, line, 200f, patterns.Select(id => id == null ? UiTexts.Get("no_pattern") : cat.Name(id)).ToList(),
                    Math.Max(0, patterns.IndexOf(fill.Pattern)), i =>
                    {
                        fill.Pattern = patterns[i];
                        if (fill.Pattern != null && fill.PatternColors.All(t => t.Name == null && t.Value.R == 0 && t.Value.G == 0 && t.Value.B == 0))
                        {
                            fill.PatternColors = new[] { Named("Or", cp), fill.Color, Named("Sable", cp) };
                        }
                        Changed(form, rebuild: true);
                    });
                if (fill.Pattern != null)
                {
                    bool[] uses = cp.PatternChannels(cat.TexById.TryGetValue(fill.Pattern, out string ptex) ? ptex : null);
                    foreach (int ch in new[] { 1, 0, 2 }.Where(c => uses[c])) // g (the ground) first, then r, b; only the ones it has
                    {
                        int c = ch;
                        Swatch(form, line, UiTexts.Get("f_patcol_short"), () => fill.PatternColors[c], t =>
                        {
                            fill.PatternColors[c] = t;
                            if (c == 1)
                            {
                                fill.Color = t;
                            }
                        });
                    }
                }
            }

            // ordinaries
            List<string> ords = cat.Ids[PartKind.Ordinary];
            for (int o = 0; o < s.Ordinaries.Count; o++)
            {
                int index = o;
                line = Line(list, 50f);
                LineLabel(parts, line, string.Format(UiTexts.Get("f_ordinary"), o + 1));
                Dropdown(parts, line, 230f, ords.Select(cat.Name).ToList(), ords.IndexOf(s.Ordinaries[o].Part), i =>
                {
                    var v = s.Ordinaries[index];
                    s.Ordinaries[index] = (ords[i], v.MirrorX, v.MirrorY, v.Color);
                    Changed(form);
                });
                Swatch(form, line, UiTexts.Get("f_colour"), () => s.Ordinaries[index].Color, t =>
                {
                    var v = s.Ordinaries[index];
                    s.Ordinaries[index] = (v.Part, v.MirrorX, v.MirrorY, t);
                });
                SmallButton(parts, line, UiTexts.Get("remove_short"), 60f, () =>
                {
                    s.Ordinaries.RemoveAt(index);
                    Changed(form, rebuild: true);
                }, UiTexts.Get("remove"));
            }

            // charges
            List<string> charges = cat.Ids[PartKind.Charge];
            for (int c = 0; c < s.Charges.Count; c++)
            {
                ChargeSpec cs = s.Charges[c];
                line = Line(list, 50f);
                LineLabel(parts, line, string.Format(UiTexts.Get("f_charge"), c + 1));
                Dropdown(parts, line, 230f, charges.Select(cat.Name).ToList(), charges.IndexOf(cs.Part), i =>
                {
                    cs.Part = charges[i];
                    Changed(form);
                });
                Swatch(form, line, UiTexts.Get("f_colour"), () => cs.Color, t => cs.Color = t);
                var places = Presets.All.Select(p => (Key: p, Name: UiTexts.Get("p_" + p))).ToList();
                if (cs.Preset == null)
                {
                    places.Add((null, UiTexts.Get("p_original")));
                }
                Dropdown(parts, line, 180f, places.Select(p => p.Name).ToList(), places.FindIndex(p => p.Key == cs.Preset), i =>
                {
                    bool hadOriginal = places.Any(p => p.Key == null);
                    if (places[i].Key != null)
                    {
                        cs.Preset = places[i].Key;
                        cs.Rot = 0f;
                        cs.MirrorX = cs.MirrorY = false;
                    }
                    Changed(form, rebuild: hadOriginal && places[i].Key != null);
                });
                SliderControl(parts, line, 120f, 0.1f, 1.5f, cs.Size, UiTexts.Get("f_size"), v =>
                {
                    cs.Size = v;
                    Changed(form);
                });
                SmallButton(parts, line, UiTexts.Get("remove_short"), 60f, () =>
                {
                    s.Charges.Remove(cs);
                    Changed(form, rebuild: true);
                }, UiTexts.Get("remove"));
            }

            int layers = s.Ordinaries.Count + s.Charges.Count;
            if (layers < CoaSpec.MaxLayers)
            {
                line = Line(list, 52f);
                if (ords.Count > 0)
                {
                    SmallButton(parts, line, UiTexts.Get("add_ordinary"), 0f, () =>
                    {
                        s.Ordinaries.Add((ords.FirstOrDefault(id => id.Contains("Cross")) ?? ords[0], false, false, Named("Or", cp)));
                        Changed(form, rebuild: true);
                    });
                }
                if (charges.Count > 0)
                {
                    string lion = charges.FirstOrDefault(id => id.StartsWith("Charges_Lion")) ?? charges[0];
                    SmallButton(parts, line, UiTexts.Get("add_charge"), 0f, () =>
                    {
                        s.Charges.Add(new ChargeSpec { Part = lion, Color = Named("Or", cp) });
                        Changed(form, rebuild: true);
                    });
                }
            }
            LayoutRebuilder.ForceRebuildLayoutImmediate(list);
        }

        private static Tincture Named(string name, CoaParts cp) =>
            Tincture.TryParse(name, cp.Palette, out Tincture t) ? t : new Tincture { Value = new Rgb(255, 255, 255) };

        // ---- compact controls: the settings' own, taken out of their rows ----

        /// <summary>A line of controls side by side.</summary>
        private static RectTransform Line(Transform parent, float height, bool center = false)
        {
            var go = new GameObject("Line", typeof(RectTransform), typeof(HorizontalLayoutGroup), typeof(LayoutElement));
            go.transform.SetParent(parent, worldPositionStays: false);
            var le = go.GetComponent<LayoutElement>();
            le.minHeight = le.preferredHeight = height;
            var hl = go.GetComponent<HorizontalLayoutGroup>();
            hl.spacing = 10f;
            hl.childAlignment = center ? TextAnchor.MiddleCenter : TextAnchor.MiddleLeft;
            hl.childControlWidth = hl.childControlHeight = true;
            hl.childForceExpandWidth = hl.childForceExpandHeight = false;
            return (RectTransform)go.transform;
        }

        /// <summary>Takes the free room of a line (or, vertical, of a column).</summary>
        private static void Spacer(Transform line, bool vertical = false)
        {
            var go = new GameObject("Spacer", typeof(RectTransform), typeof(LayoutElement));
            go.transform.SetParent(line, worldPositionStays: false);
            if (vertical)
            {
                go.GetComponent<LayoutElement>().flexibleHeight = 1f;
            }
            else
            {
                go.GetComponent<LayoutElement>().flexibleWidth = 1f;
            }
        }

        /// <summary>A control out of one of the settings' row prefabs, moved into the line (the rest of the row dropped).</summary>
        private static GameObject TakeControl(Parts parts, GameObject prefab, string name, Transform line, float width, float height)
        {
            GameObject row = Row(parts, prefab, "", parent: line);
            Transform c = row.GetComponentsInChildren<Transform>(includeInactive: true).FirstOrDefault(t => t.name == name);
            if (c == null)
            {
                Object.DestroyImmediate(row);
                throw new InvalidOperationException($"the settings' row has no {name}");
            }
            c.SetParent(line, worldPositionStays: false);
            Object.DestroyImmediate(row);
            foreach (LayoutElement old in c.GetComponents<LayoutElement>())
            {
                Object.DestroyImmediate(old);
            }
            var le = c.gameObject.AddComponent<LayoutElement>();
            le.layoutPriority = 99;
            le.preferredWidth = width > 0f ? width : -1f;
            le.minWidth = width > 0f ? width : -1f; // fixed: the lines' controls stay in columns
            le.minHeight = le.preferredHeight = height;
            c.gameObject.SetActive(true);
            return c.gameObject;
        }

        private static void LineLabel(Parts parts, Transform line, string text)
        {
            GameObject go = TakeControl(parts, parts.DropdownRow, "id_Label", line, 130f, 40f);
            var label = go.GetComponent<TextMeshProUGUI>();
            UIText.SetText(label, text);
            label.alignment = TextAlignmentOptions.MidlineLeft;
            label.enableWordWrapping = false;
            label.overflowMode = TextOverflowModes.Ellipsis;
        }

        private static TMP_Dropdown Dropdown(Parts parts, Transform line, float width, List<string> options, int index, Action<int> onChange)
        {
            TMP_Dropdown dropdown = TakeControl(parts, parts.DropdownRow, "id_Dropdown", line, width, 36f).GetComponent<TMP_Dropdown>();
            dropdown.onValueChanged.RemoveAllListeners();
            dropdown.interactable = true;
            if (dropdown is BSG_TMP_DropDown bsg)
            {
                bsg.indexesToHide.Clear();
            }
            dropdown.options.Clear();
            dropdown.options.AddRange(options.Select(o => new TMP_Dropdown.OptionData(o)));
            if (dropdown.template != null)
            {
                dropdown.template.sizeDelta = new Vector2(dropdown.template.sizeDelta.x, 25f * 14f);
            }
            dropdown.SetValueWithoutNotify(Mathf.Clamp(index, 0, Math.Max(0, options.Count - 1)));
            dropdown.RefreshShownValue();
            dropdown.onValueChanged.AddListener(i => Run(() => onChange(i)));
            return dropdown;
        }

        private static void SmallButton(Parts parts, Transform line, string text, float width, Action onClick, string tip = null)
        {
            GameObject go = TakeControl(parts, parts.ButtonRow, "id_Button", line, width, 40f);
            BSGButton button = go.GetComponent<BSGButton>();
            TextMeshProUGUI label = Child<TextMeshProUGUI>(go, "id_ButtonLabel");
            Wire(button, label, text, onClick);
            if (width <= 0f && label != null)
            {
                go.GetComponent<LayoutElement>().preferredWidth = label.GetPreferredValues(text).x + 48f;
            }
            if (tip != null)
            {
                Tooltip.Get(go).SetText("#" + tip);
            }
        }

        private static void SliderControl(Parts parts, Transform line, float width, float min, float max, float value, string tip, Action<float> onChange)
        {
            Slider slider = TakeControl(parts, parts.SliderRow, "id_Slider", line, width, 30f).GetComponent<Slider>();
            slider.onValueChanged.RemoveAllListeners();
            slider.minValue = min;
            slider.maxValue = max;
            slider.wholeNumbers = false;
            slider.interactable = true;
            slider.SetValueWithoutNotify(value);
            TextMeshProUGUI label = TakeControl(parts, parts.SliderRow, "id_ValueLabel", line, 48f, 30f).GetComponent<TextMeshProUGUI>();
            SetNumber(label, value);
            Tooltip.Get(slider.gameObject).SetText("#" + tip);
            slider.onValueChanged.AddListener(v => Run(() =>
            {
                float r = (float)Math.Round(v, 2);
                SetNumber(label, r);
                onChange(r);
            }));
        }

        private static void SetNumber(TextMeshProUGUI label, float v)
        {
            if (label != null)
            {
                UIText.SetText(label, v.ToString("0.00", System.Globalization.CultureInfo.InvariantCulture));
            }
        }

        /// <summary>A square in the colour; a click opens the colour popup over the arms window.</summary>
        private static void Swatch(Form form, Transform line, string caption, Func<Tincture> get, Action<Tincture> set)
        {
            var go = new GameObject("Swatch", typeof(RectTransform), typeof(Image), typeof(LayoutElement));
            go.transform.SetParent(line, worldPositionStays: false);
            var le = go.GetComponent<LayoutElement>();
            le.preferredWidth = 44f;
            le.minHeight = le.preferredHeight = 32f;
            Image img = go.GetComponent<Image>();
            var outline = go.AddComponent<Outline>();
            outline.effectColor = new Color(0.75f, 0.68f, 0.5f, 0.6f); // a light edge: dark colours show on the dark window
            outline.effectDistance = new Vector2(1f, -1f);
            CoaParts cp = CoaParts.Get();
            void Show()
            {
                Tincture t = get();
                img.color = t.Value.ToColor32();
                Tooltip.Get(go).SetText("#" + (t.Name ?? t.ToString()));
            }
            Show();
            ClickToPick(img, () => OpenColorPopup(form.Parts, caption, () => get().Value.ToColor32(), c =>
            {
                set(Tincture.Of(Rgb.From(c), cp.Palette));
                Show();
                Changed(form);
            }));
            Show(); // ClickToPick set its own tooltip
        }

        // ---- preview ----

        private static readonly string[] PreviewModes = { Modes.Shield, Modes.Flag, Modes.Banner };

        /// <summary>A centred strip of previews (images[first..] get them), height high.</summary>
        private static void PreviewStrip(Transform parent, RawImage[] images, Vector2[] sizes, float height, int first = 0)
        {
            var go = new GameObject("Preview", typeof(RectTransform), typeof(LayoutElement), typeof(HorizontalLayoutGroup));
            go.transform.SetParent(parent, worldPositionStays: false);
            go.GetComponent<LayoutElement>().minHeight = go.GetComponent<LayoutElement>().preferredHeight = height;
            var layout = go.GetComponent<HorizontalLayoutGroup>();
            layout.childAlignment = TextAnchor.MiddleCenter;
            layout.spacing = 24f;
            layout.childControlWidth = layout.childControlHeight = false;
            layout.childForceExpandWidth = layout.childForceExpandHeight = false;
            for (int i = 0; i < sizes.Length; i++)
            {
                var img = new GameObject(PreviewModes[first + i], typeof(RectTransform), typeof(RawImage));
                img.transform.SetParent(go.transform, worldPositionStays: false);
                ((RectTransform)img.transform).sizeDelta = sizes[i];
                images[first + i] = img.GetComponent<RawImage>();
                images[first + i].raycastTarget = false;
            }
        }

        private static void UpdatePreview(Form form)
        {
            CoaParts cp = CoaParts.Get();
            Crest crest = cp != null ? form.Coa?.ToCrest(cp) : null;
            for (int i = 0; i < 3; i++)
            {
                if (form.PreviewTex[i] != null)
                {
                    Object.Destroy(form.PreviewTex[i]);
                }
                form.PreviewTex[i] = crest != null ? cp.Paint(crest, PreviewModes[i]).ToTexture() : null;
                foreach (RawImage img in new[] { form.Previews[i], form.WindowPreviews[i] })
                {
                    if (img != null)
                    {
                        img.texture = form.PreviewTex[i];
                        img.gameObject.SetActive(form.PreviewTex[i] != null);
                    }
                }
            }
        }

        // ---- the browser of vanilla crests ----

        /// <summary>Names of the crests the map's kingdoms use (europe), by crest id: key and shown name.</summary>
        private static Dictionary<int, (string Key, string Name)> CrestNames()
        {
            var names = new Dictionary<int, (string, string)>();
            CoAMapping mapping = CoAMapping.GetMapping("europe");
            if (mapping?.mapping == null)
            {
                return names;
            }
            foreach (var kv in mapping.mapping.OrderBy(kv => kv.Key.IndexOf('.') >= 0 ? 1 : 0))
            {
                if (kv.Value <= 0 || names.ContainsKey(kv.Value))
                {
                    continue;
                }
                string key = kv.Key.Split('.')[0];
                if (Defs.FindTextField("tn_" + key) != null)
                {
                    names[kv.Value] = (key, Defs.Localize("tn_" + key));
                }
            }
            return names;
        }

        private static string CrestName(int id) => CrestNames().TryGetValue(id, out var n) ? n.Name : "#" + id;

        /// <summary>Every crest of the game as a grid of its shields (the atlas' own tiles); a click takes it as the base.</summary>
        private static void OpenBrowser(Form form)
        {
            CoaParts cp = CoaParts.Get();
            if (cp == null)
            {
                Status(form, UiTexts.Get("arms_failed"), error: true);
                return;
            }
            const float width = 980f;
            RectTransform rt = OpenSystemPopup(UiTexts.Get("choose_arms"), width);

            var scroll = new GameObject("Scroll", typeof(RectTransform), typeof(Image), typeof(RectMask2D), typeof(ScrollRect), typeof(LayoutElement));
            scroll.transform.SetParent(rt, worldPositionStays: false);
            scroll.GetComponent<LayoutElement>().preferredHeight = 560f;
            scroll.GetComponent<Image>().color = new Color(0f, 0f, 0f, 0.2f);
            var grid = new GameObject("Grid", typeof(RectTransform), typeof(GridLayoutGroup), typeof(ContentSizeFitter));
            var grt = (RectTransform)grid.transform;
            grt.SetParent(scroll.transform, worldPositionStays: false);
            grt.anchorMin = new Vector2(0f, 1f);
            grt.anchorMax = new Vector2(1f, 1f);
            grt.pivot = new Vector2(0.5f, 1f);
            grt.offsetMin = grt.offsetMax = Vector2.zero;
            var gl = grid.GetComponent<GridLayoutGroup>();
            gl.cellSize = new Vector2(60f, 80f);
            gl.spacing = new Vector2(10f, 10f);
            gl.padding = new RectOffset(10, 10, 10, 10);
            gl.constraint = GridLayoutGroup.Constraint.FixedColumnCount;
            gl.constraintCount = Mathf.Max(1, Mathf.FloorToInt((width - 2f * PopupSide - 20f + 10f) / 70f));
            gl.childAlignment = TextAnchor.UpperCenter;
            grid.GetComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            var sr = scroll.GetComponent<ScrollRect>();
            sr.content = grt;
            sr.viewport = (RectTransform)scroll.transform;
            sr.horizontal = false;
            sr.vertical = true;
            sr.movementType = ScrollRect.MovementType.Clamped;
            sr.scrollSensitivity = 40f;

            Dictionary<int, (string Key, string Name)> names = CrestNames();
            IEnumerable<int> ids = cp.Crests.Keys.Where(id => names.ContainsKey(id)).OrderBy(id => names[id].Name, StringComparer.CurrentCultureIgnoreCase)
                .Concat(cp.Crests.Keys.Where(id => !names.ContainsKey(id)).OrderBy(id => id));
            foreach (int id in ids)
            {
                var cell = new GameObject("Crest" + id, typeof(RectTransform), typeof(RawImage), typeof(Button));
                cell.transform.SetParent(grt, worldPositionStays: false);
                var img = cell.GetComponent<RawImage>();
                img.texture = cp.ShieldAtlas;
                img.uvRect = cp.ShieldUv(id);
                var button = cell.GetComponent<Button>();
                button.targetGraphic = img;
                int crest = id;
                Tooltip.Get(cell).SetText("#" + (names.TryGetValue(id, out var n) ? n.Name : "#" + id));
                button.onClick.AddListener(() => Run(() =>
                {
                    form.Coa = CoaSpec.FromCrest(cp.Crests[crest], cp.Catalogue, cp.Palette);
                    form.Coa.Edited = false;
                    form.ArmsKey = names.TryGetValue(crest, out var k) ? k.Key : null;
                    ClosePopup();
                    RebuildArms(form);
                }));
            }
            FitPopup(rt);
        }
    }
}
