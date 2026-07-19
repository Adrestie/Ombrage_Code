// OceanP2GateEnvBuilder.cs  (outillage éditeur de test)
// Fabrique PAR SCRIPT un VolumeProfile de test DÉTERMINISTE, réponse au blocage C (edge-case) :
//   la surface océan est un Lit TRÈS lisse (smoothness≈0.92) DOMINÉ par la RÉFLEXION d'environnement.
//   Sans ciel, l'ambient + la reflection probe par défaut sont NOIRS → l'eau paraît noire à cause de
//   l'ENVIRONNEMENT manquant, pas de la surface → le critère « eau non noire » devient un FAUX NÉGATIF,
//   et l'auto-exposition rend la colonne « Deferred Lighting » NON reproductible.
//
// Décisions verrouillées :
//   • Sky = GradientSky : 100 % DÉTERMINISTE, INDÉPENDANT de l'angle solaire (contrairement au
//     PhysicallyBasedSky où un soleil sous l'horizon rouvrirait le faux négatif « ciel noir »), et
//     SANS prérequis sur le HDRenderPipelineAsset (PBSky exige un flag d'asset, sinon fallback silencieux).
//   • VisualEnvironment.skyAmbientMode = Dynamic : l'ambient du ciel est appliqué SANS bake
//     (Static exigerait un bake manuel → non déterministe / noir sans bake).
//   • Exposure = Fixed (valeur connue documentée) : supprime l'auto-exposition → rendu reproductible.
//   • Fog explicitement OFF.
//   • APV RETIRÉE (bake non déterministe) : le seul Sky suffit pour ambient + réflexion.
//
// ORDRE OBLIGATOIRE : CreateAsset(profil) AVANT tout Add<T>() — VolumeProfile.Add fait
// AddObjectToAsset seulement si le profil est déjà persisté ; sinon les VolumeComponent ne sont PAS
// sauvegardés comme sous-assets (profil rechargé vide depuis le disque).
//
// Ré-exécutable : supprime puis recrée l'asset de façon déterministe.
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.HighDefinition;

namespace Ombrage.OceanFeatures.GateTools
{
    public static class OceanP2GateEnvBuilder
    {
        public const string EnvProfilePath = "Assets/Shader/Ocean_v2/Tests/OceanP2GateEnv.volumeprofile.asset";

        // Valeur d'exposition FIXE (EV100). Documentée au MANIFEST 06 ; réutilisée À L'IDENTIQUE
        // ON/OFF → n'affecte PAS le delta, seulement l'appréciation visuelle.
        // Choisie en milieu de plage pour éviter un clip vers le noir OU vers le blanc (glint saturé sur
        // une surface quasi-miroir). À affiner une seule fois si l'image est trop sombre/claire.
        public const float FixedExposureEV = 10f;

        // Couleurs du GradientSky (dégradé tri-bande). Milieu de plage → réflexion de ciel LISIBLE sur
        // une surface lisse (ni noir ni blanc saturé), gradient de surface visible.
        static readonly Color kSkyTop    = new Color(0.32f, 0.52f, 0.80f, 1f); // zénith bleu
        static readonly Color kSkyMiddle = new Color(0.62f, 0.72f, 0.82f, 1f); // horizon clair
        static readonly Color kSkyBottom = new Color(0.28f, 0.30f, 0.34f, 1f); // sol/sous-horizon neutre

        [MenuItem("Ombrage/Ocean/Build Test Environment (VolumeProfile)")]
        public static void BuildMenu()
        {
            var env = BuildEnvProfile();
            if (env != null)
                Selection.activeObject = env;
        }

        /// Construit (ou reconstruit) le VolumeProfile de test et le renvoie. Retourne null en cas d'échec.
        public static VolumeProfile BuildEnvProfile()
        {
            OceanEditorIO.EnsureFolder("Assets/Shader/Ocean_v2/Tests");

            if (AssetDatabase.LoadAssetAtPath<VolumeProfile>(EnvProfilePath) != null)
                AssetDatabase.DeleteAsset(EnvProfilePath);

            var env = ScriptableObject.CreateInstance<VolumeProfile>();
            // (b) AVANT (c) : persister le conteneur pour que les sous-assets soient sauvegardés.
            AssetDatabase.CreateAsset(env, EnvProfilePath);

            // (a) VisualEnvironment : pointe le Sky sur GradientSky + ambient DYNAMIQUE (sans bake).
            var visualEnv = env.Add<VisualEnvironment>(true);
            visualEnv.skyType.overrideState = true;
            visualEnv.skyType.value = (int)SkyType.Gradient;
            visualEnv.skyAmbientMode.overrideState = true;
            visualEnv.skyAmbientMode.value = SkyAmbientMode.Dynamic;

            // (b) GradientSky ACTIF : fournit à la fois l'ambient probe ET la réflexion d'environnement.
            var sky = env.Add<GradientSky>(true);
            sky.active = true;
            sky.top.overrideState = true;    sky.top.value = kSkyTop;
            sky.middle.overrideState = true; sky.middle.value = kSkyMiddle;
            sky.bottom.overrideState = true; sky.bottom.value = kSkyBottom;
            sky.gradientDiffusion.overrideState = true; sky.gradientDiffusion.value = 1f;

            // (c) Exposure = Fixed (valeur connue) → pas d'auto-exposition (déterminisme du rendu).
            var exposure = env.Add<Exposure>(true);
            exposure.mode.overrideState = true;
            exposure.mode.value = ExposureMode.Fixed;
            exposure.fixedExposure.overrideState = true;
            exposure.fixedExposure.value = FixedExposureEV;

            // (d) Fog explicitement OFF.
            var fog = env.Add<Fog>(true);
            fog.enabled.overrideState = true;
            fog.enabled.value = false;

            EditorUtility.SetDirty(env);
            AssetDatabase.SaveAssets();
            AssetDatabase.ImportAsset(EnvProfilePath);

            if (Validate(env, out var reason))
                Debug.Log($"[Ocean] Environnement de test OK : GradientSky (ambient Dynamic) + Exposure Fixed(EV{FixedExposureEV}) + Fog off → {EnvProfilePath}");
            else
                Debug.LogError($"[Ocean] Environnement de test INVALIDE : {reason}");

            return env;
        }

        /// Vérifie que le profil contient bien le trio déterministe (Sky Gradient actif + ambient Dynamic + Exposure Fixed).
        public static bool Validate(VolumeProfile env, out string reason)
        {
            reason = null;
            if (env == null) { reason = "profil null"; return false; }

            if (!env.TryGet<VisualEnvironment>(out var ve) || ve == null)
            { reason = "VisualEnvironment absent"; return false; }
            if (ve.skyType.value != (int)SkyType.Gradient)
            { reason = $"skyType={ve.skyType.value} ≠ Gradient({(int)SkyType.Gradient})"; return false; }
            if (ve.skyAmbientMode.value != SkyAmbientMode.Dynamic)
            { reason = "skyAmbientMode ≠ Dynamic (ambient non déterministe sans bake)"; return false; }

            if (!env.TryGet<GradientSky>(out var sky) || sky == null || !sky.active)
            { reason = "GradientSky absent ou inactif"; return false; }

            if (!env.TryGet<Exposure>(out var exp) || exp == null || exp.mode.value != ExposureMode.Fixed)
            { reason = "Exposure absent ou mode ≠ Fixed (auto-exposition non déterministe)"; return false; }

            return true;
        }
    }
}
