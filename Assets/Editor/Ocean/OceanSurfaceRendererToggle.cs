// OceanSurfaceRendererToggle.cs  (banc de validation P2 — outillage éditeur)
// Bascule le MeshRenderer de la surface océan ON/OFF pour la MESURE DE DELTA du gate 4 (correctifs A + D).
//
// CORRECTIF A : le delta NE se mesure PAS sur le flag `active` du module (il ne gate que
//   PreSimulate/Apply/Tick ; le MeshRenderer vit de OnModuleEnable à Teardown → décocher `active`
//   laisse la surface DANS le GBuffer → delta ~0 FAUX). Le mode OFF réel = MeshRenderer.enabled=false.
//
// CORRECTIF D : le GameObject runtime réel s'appelle « OceanSurface (runtime) » (espaces inclus,
//   cf. OceanSurfaceModule L273) et porte HideFlags.HideAndDontSave (L276) → INVISIBLE et NON
//   sélectionnable en hiérarchie. Transform.Find("OceanSurface") renvoie null (mauvais nom) et toute
//   manipulation manuelle est physiquement IMPOSSIBLE. On le retrouve donc TOUJOURS via
//   GetComponentsInChildren<MeshRenderer>(includeInactive:true) (traverse les objets HideAndDontSave),
//   avec filtre de secours au nom EXACT « OceanSurface (runtime) ». JAMAIS Transform.Find.
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace Ombrage.OceanFeatures.GateTools
{
    public static class OceanSurfaceRendererToggle
    {
        public const string RuntimeChildName = "OceanSurface (runtime)";

        /// Retrouve le MeshRenderer de la surface runtime sous un OceanSystem donné (objet caché inclus).
        /// Renvoie null si absent (surface non instanciée / gate d'entrée en échec).
        public static MeshRenderer FindSurfaceRenderer(OceanSystem system)
        {
            if (system == null) return null;

            // Traverse les enfants CACHÉS (HideAndDontSave) — Transform.Find échouerait ici.
            var renderers = system.GetComponentsInChildren<MeshRenderer>(includeInactive: true);
            if (renderers == null || renderers.Length == 0) return null;

            // Filtre de secours par nom exact (au cas où d'autres MeshRenderer seraient enfants).
            foreach (var r in renderers)
                if (r != null && r.gameObject.name == RuntimeChildName)
                    return r;

            // Sinon, l'unique MeshRenderer enfant est la surface (banc de gate = un seul).
            return renderers.Length == 1 ? renderers[0] : null;
        }

        /// Retrouve tous les OceanSystem de la scène active (banc de gate = un seul, mais robuste).
        static List<OceanSystem> FindSystems()
        {
#if UNITY_2023_1_OR_NEWER
            // CORRECTIF CS0618 : l'overload à 2 args (FindObjectsInactive, FindObjectsSortMode) est LUI AUSSI
            // déprécié en Unity 6000.x (confirmé par le journal de compilation live). La surcharge réellement
            // non dépréciée, indiquée par le message Unity, prend le SEUL argument FindObjectsInactive.
            // Exclude → un OceanSystem désactivé n'a de toute façon pas de surface runtime instanciée, rien à basculer.
            var systems = Object.FindObjectsByType<OceanSystem>(FindObjectsInactive.Exclude);
#else
            var systems = Object.FindObjectsOfType<OceanSystem>();
#endif
            return new List<OceanSystem>(systems);
        }

        [MenuItem("Ombrage/Ocean/Toggle Surface Renderer")]
        public static void ToggleSurfaceRenderer()
        {
            var systems = FindSystems();
            if (systems.Count == 0)
            {
                Debug.LogError("[P2Gate] Aucun OceanSystem dans la scène active — ouvrir la scène de gate d'abord.");
                return;
            }

            int toggled = 0;
            foreach (var system in systems)
            {
                var r = FindSurfaceRenderer(system);
                if (r == null)
                {
                    Debug.LogWarning($"[P2Gate] '{system.name}' : MeshRenderer « {RuntimeChildName} » introuvable (surface non instanciée ?).");
                    continue;
                }
                r.enabled = !r.enabled;
                toggled++;
                Debug.Log($"[P2Gate] Surface MeshRenderer.enabled = {r.enabled} " +
                          $"({(r.enabled ? "ON — surface dans le GBuffer" : "OFF — draw call OceanSurface ABSENT ; confirmer la disparition au FrameDebugger")}).");
            }

            if (toggled == 0)
                Debug.LogError("[P2Gate] Aucun MeshRenderer de surface basculé (voir avertissements ci-dessus).");
        }

        [MenuItem("Ombrage/Ocean/Verify Surface Runtime Present")]
        public static void VerifySurfaceRuntimePresent()
        {
            var systems = FindSystems();
            if (systems.Count == 0)
            {
                Debug.LogError("[P2Gate] Aucun OceanSystem dans la scène active.");
                return;
            }

            foreach (var system in systems)
            {
                var r = FindSurfaceRenderer(system);
                if (r == null)
                    Debug.LogError($"[P2Gate] '{system.name}' : child runtime « {RuntimeChildName} » ABSENT — gate d'entrée EN ÉCHEC (aucun gate 1–4 ne doit démarrer).");
                else
                    Debug.Log($"[P2Gate] '{system.name}' : child runtime « {r.gameObject.name} » présent, MeshRenderer.enabled={r.enabled} — gate d'entrée OK.");
            }
        }
    }
}
