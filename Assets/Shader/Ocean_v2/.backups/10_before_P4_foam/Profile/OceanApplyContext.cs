// OceanApplyContext.cs
// Contexte passé aux modules océan à chaque Apply/Tick. Porte le matériau cible (surface, P2+),
// le profil, le système (état runtime), le mode édition, le temps, et le cache de globaux
// NON CUMULATIF (par lequel TOUTE écriture de global doit passer — contrat anti-bug n°1).
// Calqué sur TerrainApplyContext.
namespace Ombrage.OceanFeatures
{
    public class OceanApplyContext
    {
        public UnityEngine.Material material;   // matériau de surface (alimenté à partir de P2)
        public OceanProfile profile;
        public OceanSystem system;
        public bool editMode;
        public float time;
        public float deltaTime;

        /// Cache de push global non cumulatif/restaurable. JAMAIS de Shader.SetGlobal* direct
        /// hors de ce cache (sinon le contrat anti-bug n°1 est rompu).
        public OceanGlobalCache globals;

        /// Un module de ce type est-il présent ET actif dans le profil ?
        public bool IsActive<T>() where T : OceanFeatureModule
        {
            if (profile == null) return false;
            var m = profile.Get<T>();
            return m != null && m.active;
        }

        // --- État runtime par module (RT spectre/cascade, etc.) détenu par le système ---
        public object GetRuntime(OceanFeatureModule m) => system != null ? system.GetRuntime(m) : null;
        public void SetRuntime(OceanFeatureModule m, object state) { if (system != null) system.SetRuntime(m, state); }
    }
}
