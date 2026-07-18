// TerrainDeformationManager.cs  — SHIM DE COMPATIBILITÉ
// La logique de déformation a été déplacée dans Ombrage.TerrainFeatures.DeformationModule
// (+ DeformationRuntime), pilotée par le TerrainProfileController.
// Ce composant ne subsiste que pour préserver l'API publique consommée par GrassController
// (deformManager.DeformationRT / .bufferWorldSize) SANS modifier GrassController.
// À retirer quand le système d'herbe sera refondu.
//
// - Namespace GLOBAL volontaire : GrassController référence le type sans namespace.
// - La GUID du fichier est préservée (réécriture en place) → la référence de scène
//   (m_Script + GrassController.deformManager) reste valide.
using UnityEngine;
using Ombrage.TerrainFeatures;

[AddComponentMenu("Ombrage/Terrain/Terrain Deformation Manager (compat shim)")]
public class TerrainDeformationManager : MonoBehaviour
{
    [Tooltip("Contrôleur source. Auto-résolu (ce GameObject, sinon premier de la scène) si vide.")]
    public TerrainProfileController controller;

    TerrainProfileController Resolve()
    {
        if (controller == null)
        {
            controller = GetComponent<TerrainProfileController>();
            if (controller == null) controller = FindFirstObjectByType<TerrainProfileController>();
        }
        return controller;
    }

    /// RenderTexture de déformation (lue par GrassController).
    public RenderTexture DeformationRT
    {
        get { var c = Resolve(); return c != null ? c.DeformationRT : null; }
    }

    /// Taille world-space d'une tuile de buffer (lue par GrassController).
    public float bufferWorldSize
    {
        get { var c = Resolve(); return c != null ? c.DeformationBufferWorldSize : 40f; }
    }
}
