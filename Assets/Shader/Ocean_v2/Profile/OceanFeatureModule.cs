// OceanFeatureModule.cs
// Base d'un module de feature océan (équivalent VolumeComponent HDRP), stocké en SOUS-ASSET
// d'un OceanProfile. Chaque sous-système (spectre, surface, underwater, ...) = une sous-classe.
//
// Cycle : OnModuleEnable (ressources) -> Apply (props/globaux, chaque frame) + Tick (dynamique)
//         -> OnModuleDisable (libération). Calqué 1:1 sur TerrainFeatureModule.
//
// Actuellement les sous-classes sont des STUBS (Apply = no-op). Aucune logique métier, aucun global poussé.
using UnityEngine;

namespace Ombrage.OceanFeatures
{
    public abstract class OceanFeatureModule : ScriptableObject
    {
        [Tooltip("Active le module (et son keyword shader).")]
        [SerializeField] public bool active = true;

        public virtual string DisplayName
        {
            get
            {
                var attr = (OceanModuleMenuAttribute)System.Attribute.GetCustomAttribute(GetType(), typeof(OceanModuleMenuAttribute));
                return attr != null ? attr.displayName : GetType().Name;
            }
        }

        /// Keyword shader gardé par ce module (null = aucun). Renseigné par le module de surface.
        public virtual string Keyword => null;
        /// Le keyword doit-il être ON ? (par défaut = actif ; surchargeable pour le gating).
        public virtual bool KeywordEnabled(OceanApplyContext ctx) => active;

        /// Ce module anime-t-il la surface chaque frame (et exige-t-il donc un repaint continu de la
        /// SceneView hors Play) ? Défaut = false. Les stubs inertes ne forcent aucun repaint.
        /// Les modules animés (spectre/surface/sillage) le passeront à true.
        public virtual bool WantsContinuousRepaint => false;

        public virtual void OnModuleEnable(OceanApplyContext ctx) { }
        public virtual void OnModuleDisable(OceanApplyContext ctx) { }

        /// Hook de PRÉ-SIMULATION : invoqué par OceanSystem sur TOUS les modules actifs en une
        /// passe distincte AVANT toute passe Apply/Tick (donc avant l'évolution du spectre).
        ///
        /// Pourquoi : la passe MotionVector de la surface a besoin du déplacement de la frame N-1.
        /// Au début du tick de frame N, le global _OceanDisp* contient ENCORE D[N-1] (sortie de
        /// l'évolution de N-1) ; un module peut donc le « snapshoter » ici, à un point UNIQUE et
        /// déterministe par frame, AVANT que le spectre n'écrive D[N]. Ainsi tous les contextes de
        /// rendu de la frame (Scene + Game + sondes + previews) lisent _OceanDisp=D[N] et le snapshot
        /// _OceanDispPrev=D[N-1] de façon identique : la copie ne tombe dans aucun intervalle
        /// inter-contexte → la race intra-frame est éliminée PAR CONSTRUCTION.
        ///
        /// Défaut = no-op : INERTE pour tous les modules existants (ils ne l'overrident pas, donc
        /// le spectre reste byte-à-byte intact). Seule la surface l'override.
        public virtual void PreSimulate(OceanApplyContext ctx) { }

        /// Pousse les propriétés statiques (matériau de surface et/ou globaux via ctx.globals).
        /// ⚠️ Toute écriture de global DOIT passer par ctx.globals (anti-cumul, restaurable).
        public abstract void Apply(OceanApplyContext ctx);

        /// Travail dynamique par frame (évolution temporelle du spectre, direction soleil, sillage...).
        public virtual void Tick(OceanApplyContext ctx) { }

        public virtual void DrawGizmos(OceanApplyContext ctx) { }
    }
}
