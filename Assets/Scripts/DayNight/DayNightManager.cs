namespace Ombrage.Systems.DayNight
{
	using System;
	using Ombrage.StaticSystem;
	using UnityEngine;

	[CreateAssetMenu(fileName = "DayNightManager", menuName = "Ombrage/StaticTools/Create New DayNightManager", order = 21)]
    public class DayNightManager : StaticSystemBase
    {
        public static DayNightManager Instance
        {
            get
            {
#if UNITY_EDITOR
                if (!Application.isPlaying)
                {
                    AssetHandler.FindAsset("DayNightManager", out _instance, ".asset");
                }
#endif
                return _instance;
            }
            internal set
            {
                _instance = value;
            }
        }

        private static DayNightManager _instance;

        [Header("Temps")]
        [Tooltip("Durée d'un cycle jour/nuit complet en secondes (temps réel)")]
        public float cycleDurationInSeconds = 600f;

        [Tooltip("Heure de départ du cycle (0-1, où 0 = minuit, 0.25 = 6h, 0.5 = midi, 0.75 = 18h)")]
        [Range(0f, 1f)]
        public float startTimeNormalized = 0.25f;

        [Tooltip("Temps normalisé courant dans le cycle (0-1)")]
        [Range(0f, 1f)]
        public float currentTimeNormalized;

        [Tooltip("Multiplicateur de vitesse du temps (1 = normal, 0 = pause)")]
        public float timeScale = 1f;

        [Header("Ambient")]
        [Tooltip("Couleur ambiante de jour")]
        public Color dayAmbientColor = new Color(0.5f, 0.5f, 0.6f);

        [Tooltip("Couleur ambiante de nuit")]
        public Color nightAmbientColor = new Color(0.05f, 0.05f, 0.1f);

        //Events
        public event Action<float> OnTimeChanged;
        public event Action OnDayStarted;
        public event Action OnNightStarted;

        private bool _wasDay;

        public override void Initialize()
        {
            _instance = this;
            base.Init(_instance);
            currentTimeNormalized = startTimeNormalized;
            _wasDay = IsDay;
        }

        public override void StaticUpdate()
        {
            if (status != Status.READY) return;

            float delta = (Time.deltaTime / cycleDurationInSeconds) * timeScale;
            currentTimeNormalized = (currentTimeNormalized + delta) % 1f;

            OnTimeChanged?.Invoke(currentTimeNormalized);

            bool isDay = IsDay;
            if (isDay && !_wasDay)
                OnDayStarted?.Invoke();
            else if (!isDay && _wasDay)
                OnNightStarted?.Invoke();

            _wasDay = isDay;
        }

        /// <summary>
        /// Est-ce qu'il fait jour ? (soleil au-dessus de l'horizon, approximé entre 0.2 et 0.8 du cycle)
        /// </summary>
        public bool IsDay => currentTimeNormalized > 0.2f && currentTimeNormalized < 0.8f;

        /// <summary>
        /// Retourne l'heure actuelle en format 24h (0-24)
        /// </summary>
        public float CurrentHour => currentTimeNormalized * 24f;

        /// <summary>
        /// Retourne l'heure formatée en string (ex: "14:35")
        /// </summary>
        public string CurrentTimeFormatted
        {
            get
            {
                float totalHours = currentTimeNormalized * 24f;
                int hours = Mathf.FloorToInt(totalHours);
                int minutes = Mathf.FloorToInt((totalHours - hours) * 60f);
                return $"{hours:D2}:{minutes:D2}";
            }
        }

        /// <summary>
        /// Calcule l'angle d'un astre dans le ciel à partir du temps normalisé et de son offset de phase
        /// </summary>
        public float GetCelestialAngle(float phaseOffset)
        {
            float adjustedTime = (currentTimeNormalized + phaseOffset) % 1f;
            // 0-360 degrés, 0 = sous l'horizon (minuit), 180 = zénith (midi pour le soleil)
            return adjustedTime * 360f;
        }

        public override Tuple<bool, string> Import()
        {
            return Load(this);
        }

        public override Tuple<bool, string> Export()
        {
            return Save(this);
        }
    }
}
