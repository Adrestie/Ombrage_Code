using System;

namespace Ombrage.ClaudeCodeGUI
{
    /// <summary>
    /// Représentation sérialisable d'un bloc du chat. Stockée dans un champ [SerializeField] de la
    /// fenêtre pour survivre aux rechargements de domaine (recompilation, entrée en mode Play) et
    /// reconstruire fidèlement l'affichage.
    /// </summary>
    [Serializable]
    public class ChatEntry
    {
        public string kind;          // prompt | text | thinking | tool | result | system | net | error | done | ask | wf_fold | wf_checkpoint | wf_interrupted
        public string header = "";
        public string content = "";
        public bool   isError;
        public bool   expanded;

        // Outils (pour reconstruire le diff)
        public string toolName = "";
        public string inputJson = "";

        // AskUserQuestion (pour reconstruire une carte encore active)
        public string questionsJson = "";
        public string toolUseId = "";
        public bool   answered;

        // Workflow agentique
        public string cssClass = "";   // classe USS d'un foldout générique (wf_fold)
        public bool   markdown;        // wf_fold : rendre le contenu en Markdown
        public string dataJson = "";   // wf_checkpoint : JSON de l'événement checkpoint
    }
}
