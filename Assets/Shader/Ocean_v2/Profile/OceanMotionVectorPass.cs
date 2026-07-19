// OceanMotionVectorPass.cs  (Ocean_v2)
// COORDINATOR / state-keeper du tampon de déplacement N-1 pour les Motion Vectors de la surface.
//
// CE N'EST PAS un HDRP CustomPass : la passe MotionVectors est déclenchée NATIVEMENT par le
// MeshRenderer de la surface (LightMode=MotionVectors + Per Object Motion). Le shader recalcule
// prevPositionCS en réappliquant le déplacement T-1 (pattern PR #4418 / HDRP MotionVectorTessellation,
// qui rejoue ApplyTessellationModification avec _LastTimeParameters). Ce coordinator se contente de
// FOURNIR au shader la texture de déplacement T-1 (_OceanDispPrev512/256), tenue à jour par une
// CopyTexture GPU.
//
// CADENCE (anti-race intra-frame, deux bloquants traités) :
//   1) La copie est faite dans le TICK DE SIMULATION via OceanFeatureModule.PreSimulate (appelé par
//      OceanSystem AVANT l'évolution du spectre), JAMAIS dans un callback de rendu. Au début du tick
//      de frame N, _OceanDisp* contient encore D[N-1] ; on le snapshot → prev = D[N-1] ; puis le spectre
//      écrit D[N]. Tous les contextes de rendu de la frame lisent _OceanDisp=D[N] et prev=D[N-1] à
//      l'identique → race éliminée par construction (aucune détection de « contexte principal »).
//   2) Le proxy de cadence est Time.frameCount (global Unity, monotone, avance à chaque repaint piloté
//      par WantsContinuousRepaint=true du spectre), PAS un champ du runtime du spectre (qui n'en a aucun → le
//      lire/ajouter violerait « spectre byte-à-byte intact »). lastSnapshotFrame garantit AU PLUS une copie
//      par frameCount (idempotence même si PreSimulate est atteint plusieurs fois).
//
// MIROIR STRICT : prev512/256 sont alloués en miroir EXACT (format + résolution + nombre de slices)
// des arrays du spectre lus via Shader.GetGlobalTexture (découplage total du spectre). CopyTexture couvre TOUTES
// les slices. Réallocation sur tout changement de structure (switch de preset). Anti-fuite : Release
// symétrique (ancienne RT avant ré-allocation, tout au Dispose).
//
// ANTI-BUG #1 : le BINDING nom→RT de _OceanDispPrev* est fait par OceanSurfaceModule.Apply via
// ctx.globals (tracké + restauré au Teardown). Ce coordinator ne touche JAMAIS Shader.SetGlobal* :
// la CopyTexture rafraîchit le CONTENU d'une RT déjà bindée, elle ne (re)binde aucun global.
using UnityEngine;
using UnityEngine.Rendering;

namespace Ombrage.OceanFeatures
{
    public sealed class OceanMotionVectorPass
    {
        // Sources du spectre (lues comme globaux publiés — jamais via le runtime privé du spectre).
        static readonly int ID_Disp512 = Shader.PropertyToID("_OceanDisp512");
        static readonly int ID_Disp256 = Shader.PropertyToID("_OceanDisp256");
        // Cibles T-1 publiées au shader de surface (bindées par OceanSurfaceModule via ctx.globals).
        public static readonly int ID_DispPrev512 = Shader.PropertyToID("_OceanDispPrev512");
        public static readonly int ID_DispPrev256 = Shader.PropertyToID("_OceanDispPrev256");
        // 0 le frame où prev vient d'être (ré)alloué → le shader échantillonne prev=current (MV nuls,
        // pas de flash). 1 sinon. Bindé via ctx.globals également.
        public static readonly int ID_MVValid = Shader.PropertyToID("_OceanMVValid");

        RenderTexture m_Prev512;
        RenderTexture m_Prev256;
        Texture2DArray m_BlackArray;    // fallback noir COMPATIBLE ARRAY (voir BlackArray)
        int m_LastSnapshotFrame = -1;   // proxy de cadence = Time.frameCount (PAS un champ du spectre)
        bool m_ValidThisFrame;          // false le frame d'une (ré)allocation → MV nuls ce frame-là

        /// Prêt dès que le coordinator est instancié (les RT prev sont gérées paresseusement en
        /// fonction de la disponibilité des arrays du spectre). Remplace l'ancien stub IsReady=false.
        public bool IsReady => true;

        public RenderTexture Prev512 => m_Prev512;
        public RenderTexture Prev256 => m_Prev256;
        public bool ValidThisFrame => m_ValidThisFrame;

        /// Noir COMPATIBLE Texture2DArray (1×1×1) pour rebinder _OceanDispPrev* quand un groupe de
        /// cascade n'a PAS de tampon prev (ex. preset Low → aucun groupe 512²). Deux raisons :
        ///  (1) éviter de laisser le global _OceanDispPrev* pointer une RT détruite (dangling) ;
        ///  (2) éviter le MISMATCH DE SAMPLER qu'induirait Texture2D.blackTexture : le shader déclare
        ///      _OceanDispPrev* en TEXTURE2D_ARRAY (SAMPLE_TEXTURE2D_ARRAY_LOD) — une texture 2D simple
        ///      y produirait un warning + lecture indéfinie. Ce noir est un vrai array (depth 1).
        /// Créé paresseusement (seulement si un groupe prev manque), libéré au Dispose (anti-fuite).
        public Texture BlackArray
        {
            get
            {
                if (m_BlackArray == null)
                {
                    m_BlackArray = new Texture2DArray(1, 1, 1, TextureFormat.RGBAFloat, false, true)
                    {
                        name = "OceanDispPrevBlack",
                        wrapMode = TextureWrapMode.Repeat,
                        filterMode = FilterMode.Point,
                        hideFlags = HideFlags.HideAndDontSave
                    };
                    m_BlackArray.SetPixels(new[] { Color.clear }, 0);
                    m_BlackArray.Apply(false, true);   // upload puis non-lisible CPU
                }
                return m_BlackArray;
            }
        }

        public void Setup()
        {
            // Allocation paresseuse : on attend que le spectre ait publié ses arrays (taille/slices connues).
            m_LastSnapshotFrame = -1;
            m_ValidThisFrame = false;
        }

        /// Snapshot D[N-1] → prev. Gardé par Time.frameCount (au plus une copie par frame). Idempotent.
        /// Appelé depuis OceanSurfaceModule.PreSimulate (lui-même appelé par OceanSystem avant l'évolution).
        public void SnapshotPrevious()
        {
            int frame = Time.frameCount;
            if (frame == m_LastSnapshotFrame) return;   // déjà fait ce frameCount (idempotence)
            m_LastSnapshotFrame = frame;

            using (OceanProfiler.MotionVector.Auto())
            {
                var src512 = Shader.GetGlobalTexture(ID_Disp512) as RenderTexture;
                var src256 = Shader.GetGlobalTexture(ID_Disp256) as RenderTexture;

                // EnsureMirror renvoie true si une (ré)allocation a eu lieu ce frame (→ MV nuls).
                bool realloc = false;
                realloc |= EnsureMirror(ref m_Prev512, src512);
                realloc |= EnsureMirror(ref m_Prev256, src256);

                // Copie de TOUTES les slices (CopyTexture sans argument copie l'array complet).
                if (src512 != null && m_Prev512 != null) Graphics.CopyTexture(src512, m_Prev512);
                if (src256 != null && m_Prev256 != null) Graphics.CopyTexture(src256, m_Prev256);

                // Le frame d'une (ré)allocation, prev == current (on vient de copier sans antériorité) :
                // on force le shader à traiter prev=current → MV nuls (pas de flash au 1er frame /
                // toggle / switch de preset / domain reload).
                m_ValidThisFrame = !realloc;
            }
        }

        /// (Ré)alloue `dst` en MIROIR STRICT de `src` (format + résolution + slices). Release de
        /// l'ancienne avant ré-allocation (anti-fuite). Retourne true si une (ré)allocation a eu lieu.
        static bool EnsureMirror(ref RenderTexture dst, RenderTexture src)
        {
            if (src == null)
            {
                // L'array source n'existe pas (ex. preset Low → pas de groupe 512²). Libère le miroir.
                if (dst != null) { Free(ref dst); return true; }
                return false;
            }

            bool mismatch = dst == null
                || dst.width != src.width
                || dst.height != src.height
                || dst.volumeDepth != src.volumeDepth
                || dst.graphicsFormat != src.graphicsFormat
                || dst.dimension != src.dimension;

            if (!mismatch) return false;

            Free(ref dst);
            var desc = src.descriptor;           // miroir EXACT (format, taille, dimension, slices)
            desc.enableRandomWrite = true;       // CopyTexture n'exige pas UAV, mais on reste homogène
            desc.useMipMap = false;
            desc.autoGenerateMips = false;
            dst = new RenderTexture(desc)
            {
                name = "OceanDispPrev",
                wrapMode = TextureWrapMode.Repeat,
                filterMode = FilterMode.Bilinear
            };
            dst.Create();
            return true;
        }

        static void Free(ref RenderTexture rt)
        {
            if (rt != null) { rt.Release(); Object.DestroyImmediate(rt); rt = null; }
        }

        /// Libération symétrique stricte (anti-fuite en [ExecuteAlways]). Aucun désabonnement de rendu
        /// à gérer : la copie ne vit pas dans un callback (cf. en-tête).
        public void Dispose()
        {
            Free(ref m_Prev512);
            Free(ref m_Prev256);
            if (m_BlackArray != null) { Object.DestroyImmediate(m_BlackArray); m_BlackArray = null; }
            m_LastSnapshotFrame = -1;
            m_ValidThisFrame = false;
        }
    }
}
