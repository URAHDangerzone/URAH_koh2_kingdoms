using System.Linq;
using System.Text;
using UnityEngine;
using UnityEngine.UI;

namespace URAH_Kingdoms
{
    /// <summary>Logs a UI subtree (names, active, rects, layout and raycast parts), to place our controls in the game's UI.</summary>
    internal static class UiDump
    {
        public static void Log(string what, Transform t, int depth = 3)
        {
            if (t == null)
            {
                return;
            }
            var sb = new StringBuilder();
            sb.AppendLine($"UI dump: {what}");
            Append(sb, t, 0, depth);
            Plugin.Log.LogInfo(sb.ToString());
        }

        private static string Short(string s) => s == null ? "" : (s.Length > 30 ? s.Substring(0, 30) + "..." : s).Replace("\n", " ");

        private static void Append(StringBuilder sb, Transform t, int level, int depth)
        {
            var rt = t as RectTransform;
            sb.Append(' ', level * 2).Append(t.name).Append(t.gameObject.activeSelf ? "" : " (off)");
            if (rt != null)
            {
                sb.Append($" pos {rt.anchoredPosition} size {rt.rect.size} anchors {rt.anchorMin}-{rt.anchorMax} pivot {rt.pivot}");
            }
            foreach (Component c in t.GetComponents<Component>())
            {
                switch (c)
                {
                    case LayoutElement le:
                        sb.Append($" [LayoutElement ignore={le.ignoreLayout} pref={le.preferredWidth}x{le.preferredHeight} min={le.minWidth}x{le.minHeight}]");
                        break;
                    case HorizontalOrVerticalLayoutGroup g:
                        sb.Append($" [{g.GetType().Name} control={g.childControlWidth}/{g.childControlHeight} expand={g.childForceExpandWidth}/{g.childForceExpandHeight} spacing={g.spacing}]");
                        break;
                    case LayoutGroup g:
                        sb.Append($" [{g.GetType().Name}]");
                        break;
                    case CanvasGroup cg:
                        sb.Append($" [CanvasGroup interactable={cg.interactable} raycasts={cg.blocksRaycasts} alpha={cg.alpha}]");
                        break;
                    case TMPro.TMP_Text text:
                        sb.Append($" [{c.GetType().Name} '{Short(text.text)}' {text.fontSize}pt{(text.raycastTarget ? " raycast" : "")}]");
                        break;
                    case Image img:
                        sb.Append($" [Image {(img.sprite != null ? img.sprite.name : "-")} {img.type} color {img.color}{(img.raycastTarget ? " raycast" : "")}]");
                        break;
                    case Graphic gr when gr.raycastTarget && !(gr is Image) && !(gr is TMPro.TMP_Text):
                        sb.Append($" [{gr.GetType().Name} raycast]");
                        break;
                    case ContentSizeFitter _:
                    case Selectable _:
                    case Canvas _:
                        sb.Append($" [{c.GetType().Name}]");
                        break;
                }
            }
            sb.AppendLine();
            if (level < depth)
            {
                foreach (Transform child in t.Cast<Transform>())
                {
                    Append(sb, child, level + 1, depth);
                }
            }
        }
    }
}
