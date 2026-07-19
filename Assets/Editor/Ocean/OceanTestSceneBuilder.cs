// OceanTestSceneBuilder.cs  (outillage éditeur de test)
// Fabrique la scène de test par EDITOR-SCRIPT one-shot (déterministe, versionné, JAMAIS de YAML
// écrit à la main). Traite les blocages B/C/D.
//
//   Menu : Ombrage/Ocean/Build Test Scene
//
// Garanties :
//   • (B) SaveCurrentModifiedScenesIfUserWantsTo() AVANT NewScene → aucune scène détruite silencieusement
//         (NewScene(EmptyScene, Single) remplace la scène ouverte sans prompt). Annuler = abort propre.
//   • (C) Volume GLOBAL (isGlobal) câblé sur le VolumeProfile de test DÉTERMINISTE (GradientSky +
//         Exposure Fixed + Fog off) → « eau non noire » teste la SURFACE, pas l'absence de ciel ;
//         colonne Deferred Lighting reproductible. Échoue BRUYAMMENT si l'env n'est pas conforme.
//   • Caméra HDRP : TAA + Post-processing + Motion Vectors + Object Motion Vectors (sinon le critère
//         « pas de ghosting TAA » passerait TRIVIALEMENT sans exercer les MV).
//   • Vérifie que le HDRP Asset supporte les Motion Vectors (sinon le test échoue sans indice Console).
//   • Assertions de fabrication BRUYANTES (abort si non satisfaites) : profil Spectrum+Surface actifs,
//         env Sky+ExposureFixed.
//
// MCP CoplayDev reste un repli documenté ; l'editor-script est la méthode PRIMAIRE.
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.HighDefinition;
using UnityEngine.SceneManagement;

namespace Ombrage.OceanFeatures.EditorTools
{
    public static class OceanTestSceneBuilder
    {
        public const string ScenePath = "Assets/Shader/Ocean_v2/Tests/Scenes/OceanTest.unity";

        // Rotation SOLEIL ÉPINGLÉE (déterminisme). Avec GradientSky le ciel ne dépend pas de l'angle,
        // mais la lumière DIRECTE oui → on fige l'orientation pour un éclairage reproductible.
        static readonly Vector3 kSunEuler = new Vector3(50f, -30f, 0f);
        const float kSunIntensityLux = 10000f; // documenté au MANIFEST 06 ; réutilisé identique ON/OFF.

        [MenuItem("Ombrage/Ocean/Build Test Scene")]
        public static void BuildScene()
        {
            // (0) CORRECTIF B — sauvegarde/annulation AVANT toute destruction de scène.
            var active = SceneManager.GetActiveScene();
            Debug.Log($"[Ocean] Scène active avant build : « {active.name} » (isDirty={active.isDirty}). " +
                      "Demande de sauvegarde des modifications non enregistrées…");
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())
            {
                Debug.Log("[Ocean] Build ANNULÉ par l'utilisateur — aucune scène détruite.");
                return;
            }

            // (1) Construire les assets de test (env + profil) — assertions internes propres à chacun.
            // NB : Validate(out envReason) est sorti du court-circuit '||' — sinon, sur le chemin
            // envProfile==null, Validate n'est pas appelée et envReason reste non assignée (CS0165).
            var envProfile = OceanTestEnvBuilder.BuildEnvProfile();
            string envReason = null;
            bool envOk = envProfile != null && OceanTestEnvBuilder.Validate(envProfile, out envReason);
            if (!envOk)
            {
                Debug.LogError($"[Ocean] Build ABORTÉ : environnement de test invalide ({(envProfile == null ? "null" : envReason)}).");
                return;
            }
            var profile = OceanTestProfileBuilder.BuildProfile();
            if (profile == null)
            {
                Debug.LogError("[Ocean] Build ABORTÉ : profil de test introuvable.");
                return;
            }

            // (2) Nouvelle scène vide en mémoire.
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

            // (3) Caméra HDRP au-dessus de l'eau (dans gridExtent), TAA + Post + Motion Vectors.
            CreateCamera();

            // (3b) Lumière directionnelle épinglée.
            CreateSun();

            // (3c) Volume GLOBAL câblé sur l'env déterministe (correctif C).
            CreateGlobalVolume(envProfile);

            // (3d) Vérifier le support Motion Vectors au niveau du HDRP Asset (sinon test muet).
            VerifyHdrpMotionVectors();

            // (4) OceanSystem + assignation du profil de test.
            var systemGo = new GameObject("OceanSystem");
            var system = systemGo.AddComponent<OceanSystem>();
            system.profile = profile;

            // (5) ASSERTIONS DE FABRICATION BRUYANTES (abort si non satisfaites).
            if (!AssertFabrication(system, envProfile))
            {
                Debug.LogError("[Ocean] Assertions de fabrication ÉCHOUÉES — scène NON sauvegardée. Voir erreurs ci-dessus.");
                return;
            }

            // (6) Sauvegarde de la scène (dossiers créés si absents). SaveScene n'écrit AUCUN YAML à la main.
            OceanEditorIO.EnsureFolder("Assets/Shader/Ocean_v2/Tests/Scenes");
            bool saved = EditorSceneManager.SaveScene(scene, ScenePath);
            if (saved)
                Debug.Log($"[Ocean] Scène de test SAUVEGARDÉE : {ScenePath}\n" +
                          "[Ocean] profil OK: Spectrum+Surface actifs ; env OK: Sky+ExposureFixed.\n" +
                          "VÉRIFICATION UTILISATEUR : ouvrir la scène, confirmer 0 « Missing Script » + Console 0 erreur avant les tests.");
            else
                Debug.LogError($"[Ocean] Échec de sauvegarde de la scène : {ScenePath}");
        }

        // ── Caméra ────────────────────────────────────────────────────────────
        static void CreateCamera()
        {
            var camGo = new GameObject("Main Camera (Test)");
            camGo.tag = "MainCamera";
            camGo.transform.position = new Vector3(0f, 12f, -40f);
            camGo.transform.rotation = Quaternion.Euler(12f, 0f, 0f); // regard vers l'avant, légèrement plongeant

            var cam = camGo.AddComponent<Camera>();
            cam.fieldOfView = 60f;

            var camData = camGo.AddComponent<HDAdditionalCameraData>();

            // TAA + Post-processing : sans TAA, le critère « pas de ghosting » est VACUOUS
            // (aucun MV exercé). On force donc le pipeline temporel.
            camData.antialiasing = HDAdditionalCameraData.AntialiasingMode.TemporalAntialiasing;

            camData.customRenderingSettings = true;
            ForceFrameSetting(camData, FrameSettingsField.Postprocess, true);
            ForceFrameSetting(camData, FrameSettingsField.MotionVectors, true);
            ForceFrameSetting(camData, FrameSettingsField.ObjectMotionVectors, true);
        }

        // Force un FrameSettingsField à une valeur ET marque l'override (accès par CHAMP → mutation persistée).
        static void ForceFrameSetting(HDAdditionalCameraData camData, FrameSettingsField field, bool value)
        {
            camData.renderingPathCustomFrameSettings.SetEnabled(field, value);
            camData.renderingPathCustomFrameSettingsOverrideMask.mask[(uint)field] = true;
        }

        // ── Soleil ──────────────────────────────────────────────────────────
        static void CreateSun()
        {
            var sunGo = new GameObject("Directional Light (Test)");
            sunGo.transform.rotation = Quaternion.Euler(kSunEuler);

            var light = sunGo.AddComponent<Light>();
            light.type = LightType.Directional;

            // HDRP requiert le composant additionnel ; l'ajouter AVANT de poser l'intensité (son init par
            // défaut peut sinon écraser la valeur).
            sunGo.AddComponent<HDAdditionalLightData>();

            // API moderne (Unity 6 / HDRP 17.4) : intensité posée sur le Light dans son unité native (Lux
            // pour un directionnel). PAS HDAdditionalLightData.intensity (obsolète #from(2023.3)).
            light.lightUnit = LightUnit.Lux;
            light.intensity = kSunIntensityLux; // calibré au MANIFEST 06, tunable une fois.
        }

        // ── Volume global (env déterministe) ──────────────────────────────────
        static void CreateGlobalVolume(VolumeProfile envProfile)
        {
            var volGo = new GameObject("Global Volume (Test)");
            var vol = volGo.AddComponent<Volume>();
            vol.isGlobal = true;
            vol.priority = 0f;
            vol.sharedProfile = envProfile; // correctif C : un Volume SANS profil ne fait RIEN.
        }

        // ── Vérif support Motion Vectors du HDRP Asset ────────────────────────
        static void VerifyHdrpMotionVectors()
        {
            var hdrp = GraphicsSettings.currentRenderPipeline as HDRenderPipelineAsset;
            if (hdrp == null)
            {
                Debug.LogWarning("[Ocean] Pipeline actif ≠ HDRenderPipelineAsset — impossible de vérifier le support Motion Vectors.");
                return;
            }
            if (!hdrp.currentPlatformRenderPipelineSettings.supportMotionVectors)
                Debug.LogError("[Ocean] HDRP Asset : Motion Vectors DÉSACTIVÉS — activer dans Project Settings > Graphics (HDRP Asset) > Rendering > Motion Vectors. Le test échouerait sinon.");
            else
                Debug.Log("[Ocean] HDRP Asset : support Motion Vectors OK.");
        }

        // ── Assertions de fabrication ─────────────────────────────────────────
        static bool AssertFabrication(OceanSystem system, VolumeProfile envProfile)
        {
            bool ok = true;

            if (system == null || system.profile == null)
            {
                Debug.LogError("[Ocean] Assertion : OceanSystem.profile == null.");
                return false;
            }

            var modules = system.profile.modules;
            bool surfaceActive = false, spectrumActive = false;
            foreach (var m in modules)
            {
                if (m is OceanSurfaceModule sm && sm.active) surfaceActive = true;
                if (m is OceanSpectrumModule sp && sp.active) spectrumActive = true;
            }
            if (!spectrumActive) { Debug.LogError("[Ocean] Assertion : aucun OceanSpectrumModule ACTIF dans le profil de test."); ok = false; }
            if (!surfaceActive) { Debug.LogError("[Ocean] Assertion : aucun OceanSurfaceModule ACTIF dans le profil de test."); ok = false; }

            if (!OceanTestEnvBuilder.Validate(envProfile, out var reason))
            {
                Debug.LogError($"[Ocean] Assertion : environnement de test non conforme ({reason}).");
                ok = false;
            }

            // PRÉREQUIS DU PROXY BUILD poste (a) = GBuffer total ≈ surface seule :
            // la scène de test ne doit contenir QU'UN écriveur de GBuffer (le MeshRenderer d'OceanSurface (runtime)).
            // Log informatif (non bloquant : le child runtime HideAndDontSave est créé au Setup de l'OceanSystem ;
            // s'il manque, ce n'est pas cette assertion qui doit abort — la vérification « Verify Surface Runtime » le fait).
            var meshRenderers = UnityEngine.Object.FindObjectsByType<MeshRenderer>(FindObjectsInactive.Exclude);
            Debug.Log($"[Ocean] MeshRenderer actifs dans la scène de test = {meshRenderers.Length} " +
                      "(ATTENDU = 1 : « OceanSurface (runtime) »). Prérequis du proxy build poste (a)=GBuffer total ≈ surface. " +
                      "Si > 1, un autre écriveur GBuffer fausserait la mesure build (a) → à retirer avant relevé.");

            return ok;
        }
    }
}
