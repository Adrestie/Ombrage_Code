// OceanP2GateProfileBuilder.cs  (banc de validation P2 — outillage éditeur)
// Fabrique PAR SCRIPT un OceanProfile DÉDIÉ au banc de gate, contenant EXACTEMENT 2 modules ACTIFS :
//   Simulation/Spectrum (P1) → Rendering/Surface (P2), dans cet ordre (invariant copie T-1).
// Réponse au point 1 du verdict (fabrication déterministe, jamais de YAML) + blocage « profil sans
// surface » de la revue antérieure (le profil racine à 7 modules pollue la mesure ; un profil
// Spectrum-only ne crée jamais la surface → faux négatif).
//
// Pourquoi un profil DÉDIÉ : la mesure de delta du gate 4 exige un GBuffer sans les 5 stubs
// contaminants (Underwater/Reflection/Absorption/Shore/Wake) ; et l'assertion « Surface active »
// doit être vraie par construction. CreateInstance<T> TYPÉ évite toute dépendance à un
// m_EditorClassIdentifier hérité (« Assembly-CSharp:: ») → pas de Missing Script.
//
// Ré-exécutable : supprime puis recrée l'asset de façon déterministe.
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace Ombrage.OceanFeatures.GateTools
{
    public static class OceanP2GateProfileBuilder
    {
        public const string ProfilePath = "Assets/Shader/Ocean_v2/Tests/OceanP2Gate.profile.asset";

        // Paramètres canoniques Ocean_v2 pour le spectre du banc (état de mer déterministe).
        const OceanSpectrumModule.CascadeQuality kCascadeQuality = OceanSpectrumModule.CascadeQuality.Ultra;
        const float kMasterTileLength = 293f;
        const float kGamma = 3.3f;
        const float kDepth = 191f;
        const bool  kUseTMA = false;
        const bool  kRunIdentityTest = false;

        // Compute shaders du spectre/FFT. DOIVENT être SÉRIALISÉS dans le profil de gate — sinon la
        // surface océan est INVISIBLE EN BUILD (cause racine du blocage gate 4 « océan non visible en
        // build ») : le repli AssetDatabase de OceanSpectrumModule.ResolveShaders est sous #if UNITY_EDITOR,
        // donc en build les références restent nulles → OnModuleEnable log « compute introuvables — module
        // spectre inactif » → aucun _OceanDisp* poussé → surface non déplacée/absente. De plus, un .compute
        // qu'AUCUN asset buildé ne référence est STRIPPÉ du build. Assigner ici les référence explicitement
        // règle les deux (inclusion dans le build + résolution non nulle dans le player).
        const string kFftPath = "Assets/Shader/Ocean_v2/Shaders/OceanFFT.compute";
        const string kSpectrumPath = "Assets/Shader/Ocean_v2/Shaders/OceanSpectrum.compute";

        // Shader/matériau de SURFACE. DOIT être SÉRIALISÉ (via un matériau d'asset référencé par le profil)
        // — sinon la surface océan reste INVISIBLE EN BUILD, cause racine restante du blocage gate 4 « océan
        // non visible en build » (verdict Réviseur, problème bloquant #2). Sans matériau sérialisé, la surface
        // est créée en runtime par OceanSurfaceModule.EnsureMaterial via `Shader.Find("Custom/HDRP/OceanSurface")` :
        // en build, Shader.Find n'aboutit que si le shader est inclus, et le repli `AssetDatabase` est sous
        // #if UNITY_EDITOR. Même avec le shader forcé dans les *Always Included Shaders* (inclusion du shader
        // ENTIER), la reachability des VARIANTS de rendu effectivement échantillonnés reste tributaire du
        // stripping. Un MATÉRIAU d'asset référencé par le profil (donc par le build) est le chemin déterministe :
        // il inclut le shader ET tire les variants que le matériau utilise, et `EnsureMaterial` l'emploie
        // directement (surfaceMaterialOverride != null → aucun Shader.Find en build). Les propriétés matériau
        // (couleur, tessellation…) restent poussées chaque frame par OceanSurfaceModule.PushMaterialProps :
        // un matériau nu issu du shader suffit ici.
        const string kSurfaceShaderPath = "Assets/Shader/Ocean_v2/Shaders/OceanSurface.shader";
        const string kSurfaceMatPath = "Assets/Shader/Ocean_v2/Tests/OceanP2GateSurface.mat";

        [MenuItem("Ombrage/Ocean/Build P2 Gate Profile")]
        public static void BuildMenu()
        {
            var profile = BuildProfile();
            if (profile != null)
                Selection.activeObject = profile;
        }

        /// Construit (ou reconstruit) le profil de gate et le renvoie. Retourne null en cas d'échec.
        public static OceanProfile BuildProfile()
        {
            OceanEditorIO.EnsureFolder("Assets/Shader/Ocean_v2/Tests");

            // Déterminisme : on repart d'un asset neuf (les sous-assets sont recréés proprement).
            if (AssetDatabase.LoadAssetAtPath<OceanProfile>(ProfilePath) != null)
                AssetDatabase.DeleteAsset(ProfilePath);

            var profile = ScriptableObject.CreateInstance<OceanProfile>();
            // CreateAsset AVANT AddObjectToAsset : les sous-assets ne sont persistés que si le conteneur
            // est déjà sur disque (sinon rechargés vides).
            AssetDatabase.CreateAsset(profile, ProfilePath);

            // Résolution DÉTERMINISTE des compute shaders (abort BRUYANT si introuvables : un profil de gate
            // sans compute sérialisés produirait un océan invisible en build → mesure gate 4 impossible).
            var fftShader = AssetDatabase.LoadAssetAtPath<ComputeShader>(kFftPath);
            var spectrumShader = AssetDatabase.LoadAssetAtPath<ComputeShader>(kSpectrumPath);
            if (fftShader == null || spectrumShader == null)
            {
                Debug.LogError($"[P2Gate] Build ABORTÉ : compute shader(s) introuvable(s) " +
                               $"(FFT={(fftShader == null ? "MANQUANT " + kFftPath : "OK")}, " +
                               $"Spectrum={(spectrumShader == null ? "MANQUANT " + kSpectrumPath : "OK")}). " +
                               "Le profil de gate DOIT les sérialiser, sinon océan invisible en build.");
                AssetDatabase.DeleteAsset(ProfilePath); // ne pas laisser un profil incomplet sur disque
                return null;
            }

            // Résolution DÉTERMINISTE du shader de surface + fabrication d'un MATÉRIAU d'asset (référencé par le
            // profil → inclus dans le build, chemin surface build-déterministe : cf. commentaire kSurfaceMatPath).
            // Abort BRUYANT si le shader est introuvable (un profil de gate sans matériau surface → océan invisible).
            var surfaceShader = AssetDatabase.LoadAssetAtPath<Shader>(kSurfaceShaderPath);
            if (surfaceShader == null)
            {
                Debug.LogError($"[P2Gate] Build ABORTÉ : shader de surface introuvable ({kSurfaceMatPath} ← {kSurfaceShaderPath}). " +
                               "Le profil de gate DOIT sérialiser un matériau de surface, sinon océan invisible en build.");
                AssetDatabase.DeleteAsset(ProfilePath);
                return null;
            }
            // (Re)crée le matériau de gate déterministe à partir du shader (props runtime poussées par le module).
            if (AssetDatabase.LoadAssetAtPath<Material>(kSurfaceMatPath) != null)
                AssetDatabase.DeleteAsset(kSurfaceMatPath);
            var surfaceMaterial = new Material(surfaceShader) { name = "OceanP2GateSurface" };
            AssetDatabase.CreateAsset(surfaceMaterial, kSurfaceMatPath);

            var spectrum = ScriptableObject.CreateInstance<OceanSpectrumModule>();
            spectrum.name = nameof(OceanSpectrumModule);
            spectrum.active = true;
            spectrum.cascadeQuality = kCascadeQuality;
            spectrum.masterTileLength.overridden = true; spectrum.masterTileLength.value = kMasterTileLength;
            spectrum.gamma.overridden = true;            spectrum.gamma.value = kGamma;
            spectrum.depth.overridden = true;            spectrum.depth.value = kDepth;
            spectrum.useTMA.overridden = true;           spectrum.useTMA.value = kUseTMA;
            spectrum.runIdentityTest = kRunIdentityTest;
            // SÉRIALISATION des compute (survivent au build + résolution non nulle dans le player).
            spectrum.fftShader = fftShader;
            spectrum.spectrumShader = spectrumShader;

            var surface = ScriptableObject.CreateInstance<OceanSurfaceModule>();
            surface.name = nameof(OceanSurfaceModule);
            surface.active = true;
            // SÉRIALISATION du matériau de surface (référence d'asset → survit au build + chemin surface
            // build-déterministe, aucun Shader.Find requis dans le player).
            surface.surfaceMaterialOverride = surfaceMaterial;

            AssetDatabase.AddObjectToAsset(spectrum, profile);
            AssetDatabase.AddObjectToAsset(surface, profile);

            // Ordre Spectrum → Surface : le spectre publie _OceanDisp* dans Tick ; la surface lit prev/current.
            profile.modules = new List<OceanFeatureModule> { spectrum, surface };

            EditorUtility.SetDirty(profile);
            AssetDatabase.SaveAssets();
            AssetDatabase.ImportAsset(ProfilePath);

            // Assertion POSITIVE (log vert / rouge, jamais silencieux).
            bool spectrumOk = spectrum.active;
            bool surfaceOk = surface.active;
            bool computeInMemoryOk = spectrum.fftShader != null && spectrum.spectrumShader != null;

            // DURCISSEMENT (verdict Réviseur — problème mineur #3) : l'assertion EN MÉMOIRE ci-dessus ne
            // prouve PAS que les références compute ont survécu à la SÉRIALISATION sur disque (les refs
            // vivent en RAM même si SaveAssets ne les avait pas persistées). Or c'est précisément la
            // sérialisation des .compute DANS le profil qui conditionne la visibilité de l'océan EN BUILD
            // (inclusion anti-stripping + résolution non nulle dans le player). On recharge donc l'asset
            // et son sous-module Spectrum DEPUIS LE DISQUE (après SaveAssets + ImportAsset) et on ré-assert
            // sur les références SÉRIALISÉES, pour prouver le round-trip disque — pas seulement l'état RAM.
            OceanSpectrumModule reloadedSpectrum = null;
            var reloadedProfile = AssetDatabase.LoadAssetAtPath<OceanProfile>(ProfilePath);
            if (reloadedProfile != null)
            {
                foreach (var sub in AssetDatabase.LoadAllAssetsAtPath(ProfilePath))
                {
                    if (sub is OceanSpectrumModule sp) { reloadedSpectrum = sp; break; }
                }
            }
            bool computeOnDiskOk = reloadedSpectrum != null
                                   && reloadedSpectrum.fftShader != null
                                   && reloadedSpectrum.spectrumShader != null;

            // Même round-trip disque pour le MATÉRIAU de surface : la référence surfaceMaterialOverride doit
            // avoir survécu à la sérialisation (build-safety de la visibilité surface, verdict Réviseur #2), et
            // ce matériau doit pointer un shader non nul (sinon océan invisible en build même profil serialisé).
            OceanSurfaceModule reloadedSurface = null;
            if (reloadedProfile != null)
            {
                foreach (var sub in AssetDatabase.LoadAllAssetsAtPath(ProfilePath))
                {
                    if (sub is OceanSurfaceModule su) { reloadedSurface = su; break; }
                }
            }
            var reloadedMat = AssetDatabase.LoadAssetAtPath<Material>(kSurfaceMatPath);
            bool surfaceMatOnDiskOk = reloadedSurface != null
                                      && reloadedSurface.surfaceMaterialOverride != null
                                      && reloadedMat != null
                                      && reloadedMat.shader != null;

            bool computeOk = computeInMemoryOk && computeOnDiskOk;   // requis en BUILD (RAM + disque)
            if (profile.modules.Count == 2 && spectrumOk && surfaceOk && computeOk && surfaceMatOnDiskOk)
                Debug.Log($"[P2Gate] Profil de gate OK : 2 modules actifs (Spectrum={spectrumOk}, Surface={surfaceOk}), " +
                          $"compute FFT/Spectrum + matériau de surface SÉRIALISÉS ET RE-VÉRIFIÉS SUR DISQUE (build-safe) " +
                          $"→ {ProfilePath} (+ {kSurfaceMatPath})");
            else
                Debug.LogError($"[P2Gate] Profil de gate INVALIDE : modules={profile.modules.Count}, " +
                               $"Spectrum.active={spectrumOk}, Surface.active={surfaceOk}, " +
                               $"computeMémoire={computeInMemoryOk}, computeDisque={computeOnDiskOk}, " +
                               $"surfaceMatDisque={surfaceMatOnDiskOk} " +
                               $"(Spectrum rechargé={(reloadedSpectrum != null)}, Surface rechargée={(reloadedSurface != null)}, " +
                               $"matériau rechargé={(reloadedMat != null)}).");

            return profile;
        }
    }
}
