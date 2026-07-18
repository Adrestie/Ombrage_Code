using UnityEngine;

namespace RCC3
{
	[AddComponentMenu("BoneCracker Games/Realistic Car Controller V3/Misc/Exhaust")]
	public class RCC_Exhaust : MonoBehaviour
	{
		private RCC_CarControllerV3 carController;

		private ParticleSystem particle;

		private ParticleSystem.EmissionModule emission;

		private ParticleSystem.MainModule mainModule;

		private ParticleSystem.MinMaxCurve emissionRate;

		public ParticleSystem flame;

		private ParticleSystem.EmissionModule subEmission;

		private ParticleSystem.MainModule flameMainModule;

		private ParticleSystem.MinMaxCurve subEmissionRate;

		private Light flameLight;

		public float flameTime;

		private AudioSource flameSource;

		public Color flameColor = Color.red;

		public Color boostFlameColor = Color.blue;

		public AnimationCurve audioCurve;

		private void Start()
		{
			carController = GetComponentInParent<RCC_CarControllerV3>();
			particle = GetComponent<ParticleSystem>();
			emission = particle.emission;
			mainModule = particle.main;
			if ((bool)flame)
			{
				subEmission = flame.emission;
				flameMainModule = flame.main;
				flameLight = flame.GetComponentInChildren<Light>();
				flameSource = RCC_CreateAudioSource.NewAudioSource(base.gameObject, "Exhaust Flame AudioSource", 10f, 50f, 10f, RCC_Settings.Instance.exhaustFlameClips[0], false, false, false, audioCurve);
			}
		}

		private void Update()
		{
			if (!carController || !particle)
			{
				return;
			}
			if (carController.engineRunning)
			{
				if (carController.speed < 150f)
				{
					if (!emission.enabled)
					{
						emission.enabled = true;
					}
					if (carController._gasInput > 0.05f)
					{
						emissionRate.constantMax = 50f;
						emission.rateOverTime = emissionRate;
						mainModule.startSpeed = 5f;
						mainModule.startSize = 8f;
					}
					else
					{
						emissionRate.constantMax = 5f;
						emission.rateOverTime = emissionRate;
						mainModule.startSpeed = 0.5f;
						mainModule.startSize = 4f;
					}
				}
				else if (emission.enabled)
				{
					emission.enabled = false;
				}
				if (carController._gasInput >= 0.25f)
				{
					flameTime = 0f;
				}
				if ((carController.useExhaustFlame && carController.engineRPM >= 5000f && carController.engineRPM <= 5500f && carController._gasInput <= 0.25f && flameTime <= 0.5f) || carController._boostInput >= 1.5f)
				{
					flameTime += Time.deltaTime;
					subEmission.enabled = true;
					if ((bool)flameLight)
					{
						flameLight.intensity = flameSource.pitch * 3f * Random.Range(0.25f, 1f);
					}
					if (carController._boostInput >= 1.5f && (bool)flame)
					{
						flameMainModule.startColor = boostFlameColor;
						flameLight.color = boostFlameColor;
					}
					else
					{
						flameMainModule.startColor = flameColor;
						flameLight.color = flameColor;
					}
					if (!flameSource.isPlaying)
					{
						flameSource.clip = RCC_Settings.Instance.exhaustFlameClips[Random.Range(0, RCC_Settings.Instance.exhaustFlameClips.Length)];
						flameSource.Play();
					}
				}
				else
				{
					subEmission.enabled = false;
					if ((bool)flameLight)
					{
						flameLight.intensity = 0f;
					}
					if (flameSource.isPlaying)
					{
						flameSource.Stop();
					}
				}
			}
			else
			{
				if (emission.enabled)
				{
					emission.enabled = false;
				}
				subEmission.enabled = false;
				if ((bool)flameLight)
				{
					flameLight.intensity = 0f;
				}
				if (flameSource.isPlaying)
				{
					flameSource.Stop();
				}
			}
		}
	}
}