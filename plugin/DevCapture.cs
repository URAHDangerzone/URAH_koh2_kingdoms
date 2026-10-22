using System.Collections;
using System.IO;
using BepInEx;
using BepInEx.Configuration;
using UnityEngine;

namespace URAH_Kingdoms
{
    /// <summary>
    /// For working on the editor's looks: [Debug] AutoOpen opens the editor (editor), its arms window (arms; arms_full: with a few layers added, not saved),
    /// the crest browser over it (browser) or the colour popup over it (color) once the main menu shows, and
    /// [Debug] Capture then saves a screenshot of the game to BepInEx/urah_kingdoms_capture.png (also while
    /// the game's window is covered). Both off by default.
    /// </summary>
    internal static class DevCapture
    {
        private static ConfigEntry<string> autoOpen;
        private static ConfigEntry<bool> capture;
        private static bool done;

        public static string CapturePath => Path.Combine(Paths.BepInExRootPath, "urah_kingdoms_capture.png");

        public static void Bind(ConfigFile config)
        {
            autoOpen = config.Bind("Debug", "AutoOpen", "",
                new ConfigDescription("Opens this in the main menu: editor, arms, browser or color (for working on the editor)",
                    new AcceptableValueList<string>("", "editor", "arms", "arms_full", "browser", "color")));
            capture = config.Bind("Debug", "Capture", false, "After AutoOpen, saves a screenshot to BepInEx/urah_kingdoms_capture.png");
        }

        /// <summary>The main menu is there (its button was just made).</summary>
        public static void MainMenuReady(TitleUI title)
        {
            if (done || string.IsNullOrEmpty(autoOpen?.Value))
            {
                return;
            }
            done = true;
            Plugin.Instance.StartCoroutine(Run(title, autoOpen.Value));
        }

        private static IEnumerator Run(TitleUI title, string what)
        {
            yield return new WaitForSecondsRealtime(2f);
            Editor.OpenFirst(title);
            yield return new WaitForSecondsRealtime(0.5f);
            if (what != "editor")
            {
                Editor.DevOpen(what);
            }
            if (!capture.Value)
            {
                yield break;
            }
            yield return new WaitForSecondsRealtime(1.5f);
            ScreenCapture.CaptureScreenshot(CapturePath);
            Plugin.Log.LogInfo("Captured the screen to " + CapturePath);
        }
    }
}
