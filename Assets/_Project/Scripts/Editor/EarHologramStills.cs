using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using VRShootingGallery.AnatomyDemo;
using Object = UnityEngine.Object;

namespace VRShootingGallery.EditorTools
{
    /// <summary>
    /// Renders stills of the ear hologram from a standing viewer's eye, in its main states — overview,
    /// exploded, a part selected, the hearing tones, a head turn — without entering play mode or
    /// needing the CAVE. For documentation, and for checking a change to the anatomy at a glance.
    /// </summary>
    /// <remarks>
    /// Each still runs the demo's own per-frame code for a few simulated seconds in edit mode — the
    /// controller, both simulations, every part and the label layout — so eases have settled and the
    /// animations are caught mid-cycle, exactly as they would be in play mode. The scene is reopened
    /// afterwards if it was clean before, since the run moves transforms about.
    ///
    /// Batch: <c>-executeMethod VRShootingGallery.EditorTools.EarHologramStills.RenderBatch -stillsDir &lt;dir&gt;</c>,
    /// with <c>-build</c> to rebuild the scene first.
    /// </remarks>
    static class EarHologramStills
    {
        const string k_Menu = "VR Shooting Gallery/Anatomy/Render Ear Hologram Stills";
        const int k_Width = 1920;
        const int k_Height = 1080;

        /// <summary>A viewer standing a step back from room centre, at eye height.</summary>
        static readonly Vector3 k_Eye = new(0f, 1.62f, -0.55f);

        sealed class Shot
        {
            public string Name;
            public Vector3 Eye = k_Eye;
            public Vector3 Look = new(0f, 1.3f, 0.45f);
            public float Fov = 72f;
            public string Select;
            public bool Explode;
            public float? Yaw = -25f;
            public float Zoom = 1f;
            public int Tone = -1;
            public LabelMode Labels = LabelMode.Main;
            public Vector3 HeadTurn;
            public float Time = 3.3f;
        }

        static readonly Shot[] k_Shots =
        {
            new() { Name = "01_overview" },
            new() { Name = "02_room", Eye = new Vector3(0.1f, 1.7f, -1.05f), Look = new Vector3(-0.1f, 1.15f, 0.6f), Fov = 88f },
            new() { Name = "03_exploded", Explode = true, Yaw = -35f },
            new() { Name = "04_labels_all", Labels = LabelMode.All, Yaw = -10f },
            new() { Name = "05_cochlea", Select = "Cochlea", Yaw = null },
            new() { Name = "06_ossicles", Select = "Incus", Yaw = null },
            new() { Name = "07_hearing_2k", Tone = 2, Time = 5.2f },
            new() { Name = "08_hearing_125", Tone = 0, Time = 5.2f },
            new() { Name = "09_hearing_8k", Tone = 3, Time = 12.4f },
            new() { Name = "10_balance_shake_no", HeadTurn = new Vector3(0f, 160f, 0f), Yaw = -40f },
            new() { Name = "11_section", Tone = 2, Eye = new Vector3(0.55f, 1.55f, -0.2f), Look = new Vector3(0.95f, 1.42f, 0.5f), Fov = 60f, Time = 8.7f },
            new() { Name = "12_auricle", Select = "Auricle", Yaw = null },
            new() { Name = "13_eardrum_otoscope", Select = "Tympanic_Membrane", Yaw = null },
            new() { Name = "14_labyrinth_zoom", Select = "Lateral_Canal", Yaw = null, HeadTurn = new Vector3(0f, 160f, 0f) },
            new() { Name = "15_organ_of_corti", Select = "Organ_Of_Corti", Tone = 2, Time = 6.1f },
        };

        [MenuItem(k_Menu, false, 620)]
        static void RenderFromMenu()
        {
            var hologram = Object.FindObjectOfType<EarHologram>(true);
            if (hologram == null)
            {
                Debug.LogError("[EarHologram] Open the ear hologram scene first.");
                return;
            }

            string dir = Path.GetFullPath(Path.Combine(Application.dataPath, "../Temp/EarHologramStills"));
            bool wasDirty = hologram.gameObject.scene.isDirty;
            Render(dir);
            if (!wasDirty)
                EditorSceneManager.OpenScene(hologram.gameObject.scene.path, OpenSceneMode.Single);

            EditorUtility.RevealInFinder(dir);
        }

        public static void RenderBatch()
        {
            string dir = Argument("-stillsDir") ?? Path.GetFullPath(Path.Combine(Application.dataPath, "../Temp/EarHologramStills"));
            if (Environment.GetCommandLineArgs().Contains("-build") ||
                AssetDatabase.LoadAssetAtPath<SceneAsset>(EarHologramBuilder.ScenePath) == null)
                EarHologramBuilder.CreateSceneUnattended();
            else
                EditorSceneManager.OpenScene(EarHologramBuilder.ScenePath, OpenSceneMode.Single);

            Render(dir);
        }

        static void Render(string dir)
        {
            Directory.CreateDirectory(dir);

            var hologram = Object.FindObjectOfType<EarHologram>(true);
            var hearing = Object.FindObjectOfType<HearingSimulation>(true);
            var balance = Object.FindObjectOfType<BalanceSimulation>(true);
            var labels = Object.FindObjectOfType<HologramLabels>(true);
            var parts = Object.FindObjectsOfType<HologramPart>(true);
            var texts = Object.FindObjectsOfType<TextMeshPro>(true);

            var camera = new GameObject("Stills Camera").AddComponent<Camera>();
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = Color.black;
            camera.nearClipPlane = 0.03f;
            camera.farClipPlane = 30f;

            try
            {
                foreach (var shot in k_Shots)
                {
                    var selected = shot.Select == null ? null : parts.FirstOrDefault(p => p.name == shot.Select);
                    hologram.Pose(selected, shot.Explode, shot.Yaw, shot.Zoom);
                    labels.Mode = shot.Labels;
                    hearing.SetTone(shot.Tone, 0f);

                    // A few simulated seconds of the real frame loop, ending at the shot's time.
                    const float dt = 1f / 30f;
                    int frames = 120;
                    for (int f = 0; f <= frames; f++)
                    {
                        float time = shot.Time - (frames - f) * dt;
                        hologram.Tick(shot.Eye, time, dt);
                        hearing.Simulate(time, dt);
                        balance.Inject(shot.HeadTurn);
                        balance.Apply(dt);
                        foreach (var part in parts)
                            part.Apply(dt);
                        labels.Layout(shot.Eye, dt);
                    }

                    foreach (var text in texts)
                        if (text.isActiveAndEnabled)
                            text.ForceMeshUpdate();

                    camera.transform.position = shot.Eye;
                    camera.transform.LookAt(shot.Look);
                    camera.fieldOfView = shot.Fov;
                    Save(camera, Path.Combine(dir, shot.Name + ".png"));
                }

                Debug.Log($"[EarHologram] {k_Shots.Length} stills written to {dir}");
            }
            finally
            {
                Object.DestroyImmediate(camera.gameObject);
                hologram.ResetView();
                hearing.SetTone(-1, 0f);
                hearing.Simulate(0f, 10f);
                balance.Inject(Vector3.zero);
                hologram.Tick(k_Eye, 0f, 10f);
                foreach (var part in parts)
                    part.Apply(10f);
            }
        }

        static void Save(Camera camera, string path)
        {
            var rt = new RenderTexture(k_Width, k_Height, 24, RenderTextureFormat.ARGB32) { antiAliasing = 8 };
            camera.targetTexture = rt;

            var request = new RenderPipeline.StandardRequest { destination = rt };
            if (RenderPipeline.SupportsRenderRequest(camera, request))
                RenderPipeline.SubmitRenderRequest(camera, request);
            else
                camera.Render();

            var previous = RenderTexture.active;
            RenderTexture.active = rt;
            var texture = new Texture2D(k_Width, k_Height, TextureFormat.RGB24, false);
            texture.ReadPixels(new Rect(0, 0, k_Width, k_Height), 0, 0);
            texture.Apply();
            RenderTexture.active = previous;

            File.WriteAllBytes(path, texture.EncodeToPNG());
            camera.targetTexture = null;
            Object.DestroyImmediate(texture);
            rt.Release();
            Object.DestroyImmediate(rt);
        }

        static string Argument(string name)
        {
            var args = Environment.GetCommandLineArgs();
            for (int i = 0; i < args.Length - 1; i++)
                if (args[i] == name)
                    return args[i + 1];

            return null;
        }
    }
}
