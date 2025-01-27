using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using System;

namespace at.ph3.unity.shadow.res
{
    [BepInPlugin(MyPluginInfo.PLUGIN_GUID, MyPluginInfo.PLUGIN_NAME, MyPluginInfo.PLUGIN_VERSION)]
    public class Plugin : BaseUnityPlugin
    {
        internal static new ManualLogSource Logger;

        // use to capture a screenshot of the replacement render texture
        private KeyboardShortcut key = new KeyboardShortcut(UnityEngine.KeyCode.T, UnityEngine.KeyCode.LeftControl);

        // replacement render texture, if supersampling is enabled
        private UnityEngine.RenderTexture replacementRT = null;
        private UnityEngine.RenderTexture secondaryReplacementRT = null; // used for > 2x supersampling

        // frame counter for saving screenshots
        private int frame = 0;

        // basic quality overrides
        private ConfigEntry<bool> overrideQualitySettings;

        // fixed shadow resolution, 0 = unchanged
        private ConfigEntry<int> shadowResolution;

        // sampling factor (in percent of side length, 100 = 1x pixels, 200 = 4x pixels, ...)
        private ConfigEntry<int> samplingFactor;

        // enable the rendertarget dumping feature
        private ConfigEntry<bool> enableRenderTargetDumping;

        private void Awake()
        {
            Logger = base.Logger;
            Logger.LogInfo($"Plugin {MyPluginInfo.PLUGIN_GUID} is loaded!");

            overrideQualitySettings = Config.Bind(
                "General", "overrideQualitySettings", true,
                "override basic quality settings to the maximum");
            shadowResolution = Config.Bind(
                "General", "shadowResolution", 0,
                "fixed shadow resolution, 0 = unchanged");
            samplingFactor = Config.Bind(
                "General", "samplingFactor", 100,
                "[EXPERIMENTAL] main camera sampling factor \n(in percent of side length, 100 = unchanged, 200 = 4x pixels, ...)");
            enableRenderTargetDumping = Config.Bind(
                "General", "enableRenderTargetDumping", false,
                "enable rendertarget dumping (press Ctrl+T, only works with samplingFactor != 100)");


            Logger.LogInfo($"overrideQualitySettings: {overrideQualitySettings.Value}");
            Logger.LogInfo($"shadowResolution: {shadowResolution.Value}");
            Logger.LogInfo($"samplingFactor: {samplingFactor.Value}");


            // perform requested actions when a new scene is loaded
            UnityEngine.SceneManagement.SceneManager.sceneLoaded += (scene, mode) =>
            {
                Logger.LogInfo($"Scene loaded: {scene.name}");

                if (overrideQualitySettings.Value)
                {
                    Logger.LogInfo("-> Overriding basic quality settings");
                    UnityEngine.QualitySettings.shadowResolution = UnityEngine.ShadowResolution.VeryHigh;
                    UnityEngine.QualitySettings.shadowCascades = 4;
                    UnityEngine.QualitySettings.anisotropicFiltering = UnityEngine.AnisotropicFiltering.ForceEnable;
                    UnityEngine.QualitySettings.lodBias = 100.0f;
                    UnityEngine.QualitySettings.maximumLODLevel = 0;
                }

                if (shadowResolution.Value > 0)
                {
                    Logger.LogInfo("-> Overriding light shadow resolutions");
                    UnityEngine.Light[] lights = UnityEngine.Object.FindObjectsOfType<UnityEngine.Light>();
                    foreach (UnityEngine.Light light in lights)
                    {
                        var targetResolution = shadowResolution.Value;
                        Logger.LogInfo($"    * Setting shadow resolution to {targetResolution} for light {light}");
                        light.shadowCustomResolution = targetResolution;
                    }
                }

                if (samplingFactor.Value > 100)
                {
                    Logger.LogInfo("-> Overriding main camera sampling factor");
                    UnityEngine.Camera[] cams = UnityEngine.Object.FindObjectsOfType<UnityEngine.Camera>();
                    foreach (UnityEngine.Camera cam in cams)
                    {
                        if (cam != UnityEngine.Camera.main)
                        {
                            Logger.LogInfo($"    -> Skipping camera {cam}");
                            continue;
                        }

                        Logger.LogInfo($"    -> Rendering path: {cam.actualRenderingPath}");
                        Logger.LogInfo($"    -> MSAA: {cam.allowMSAA}");
                        Logger.LogInfo($"    -> Dynamic resolution: {cam.allowDynamicResolution}");

                        if (cam.targetTexture == null)
                        {
                            var replacementWidth = UnityEngine.Screen.width * samplingFactor.Value / 100;
                            var replacementHeight = UnityEngine.Screen.height * samplingFactor.Value / 100;
                            replacementRT = new UnityEngine.RenderTexture(replacementWidth, replacementHeight, 32);
                            cam.targetTexture = replacementRT;
                            cam.allowDynamicResolution = false;
                            Logger.LogInfo($"    -> Created new target texture for {cam} with resolution {replacementWidth}x{replacementHeight}");
                            if(samplingFactor.Value > 200)
                            {
                                var secondaryReplacementWidth = UnityEngine.Screen.width * 2;
                                var secondaryReplacementHeight = UnityEngine.Screen.height * 2;
                                secondaryReplacementRT = new UnityEngine.RenderTexture(secondaryReplacementWidth, secondaryReplacementHeight, 32);
                                Logger.LogInfo($"    -> >200 scale, created secondary target texture for {cam} with resolution {secondaryReplacementWidth}x{secondaryReplacementHeight}");
                            }
                        }
                    }

                    // when rendering is done, and we did a camera target replacement, scale the replacement render texture to the screen

                    UnityEngine.Camera.onPostRender += (cam) => {
                        if (replacementRT != null && cam == UnityEngine.Camera.main)
                        {
                            frame++;
                            var dumpRT = (UnityEngine.RenderTexture rt, string fn) =>
                            {
                                if (enableRenderTargetDumping.Value && key.IsPressed())
                                {
                                    Utils.SaveRenderTextureToFile(rt, fn, Utils.SaveTextureFileFormat.PNG);
                                }
                            };
                            dumpRT(replacementRT, String.Format("{0,8:D8}-mod-replacement-rt", frame));

                            // no idea as to the difference between how X and Y behave in the target rect here, but this works in KeyWe

                            var targetRT = replacementRT;
                            var targetWidth = (UnityEngine.Screen.width * 100) / samplingFactor.Value;
                            if (secondaryReplacementRT != null)
                            {
                                UnityEngine.GL.PushMatrix();
                                UnityEngine.GL.LoadPixelMatrix(0, UnityEngine.Screen.width*2, UnityEngine.Screen.height*2, 0);
                                UnityEngine.RenderTexture.active = secondaryReplacementRT;
                                UnityEngine.Graphics.DrawTexture(new UnityEngine.Rect(0, 0, UnityEngine.Screen.width, UnityEngine.Screen.height * 2), replacementRT);
                                UnityEngine.GL.PopMatrix();
                                dumpRT(secondaryReplacementRT, String.Format("{0,8:D8}-mod-secondary-replacement-rt", frame));
                                targetRT = secondaryReplacementRT;
                                targetWidth = UnityEngine.Screen.width / 2;
                            }

                            UnityEngine.RenderTexture.active = null;
                            UnityEngine.GL.PushMatrix();
                            UnityEngine.GL.LoadPixelMatrix(0, UnityEngine.Screen.width, UnityEngine.Screen.height, 0);
                            var targetRect = new UnityEngine.Rect(0, 0, targetWidth, UnityEngine.Screen.height);
                            UnityEngine.Graphics.DrawTexture(targetRect, targetRT);
                            UnityEngine.GL.PopMatrix();
                        }
                    };
                }
            };

        }
    }
}
