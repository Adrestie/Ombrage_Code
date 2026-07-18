using System.Collections;
using System.Collections.Generic;
using UnityEngine;
namespace RCC3
{
	public class AudioManager : MonoBehaviour
	{
		public List<AudioSource> carAudioSource = new List<AudioSource>();

		public AudioSource[] musicSource;

		public int playID;

		public float volumeMax;

		private void Update()
		{
			playID = Mathf.Clamp(playID, -1, 8);
			for (int i = 0; i < playID + 1; i++)
			{
				if (i != 0)
				{
					musicSource[i].volume = Mathf.Lerp(musicSource[i].volume, volumeMax, Time.deltaTime / 2f);
				}
			}
		}

		public void EnableMusic()
		{
			playID++;
			playID = Mathf.Clamp(playID, 0, 9);
			StartCoroutine(LerpMusicVolume());
		}

		private IEnumerator LerpMusicVolume()
		{
			float t = 0f;
			while (t < 1f)
			{
				t += Time.deltaTime * 0.05f;
				volumeMax = Mathf.Lerp(0f, 0.35f, t);
				yield return new WaitForEndOfFrame();
			}
		}

		public void SetEngineSoundOn(float startVolume, float endVolume, float speedLerp)
		{
			StartCoroutine(SetEngineSoundOnVolume(startVolume, endVolume, speedLerp));
		}

		public void SetEngineSoundOff(float startVolume, float endVolume, float speedLerp)
		{
			StartCoroutine(SetEngineSoundOffVolume(startVolume, endVolume, speedLerp));
		}

		public void SetEngineSoundIdle(float startVolume, float endVolume, float speedLerp)
		{
			StartCoroutine(SetEngineSoundIdleVolume(startVolume, endVolume, speedLerp));
		}

		public void SetReversingSound(float startVolume, float endVolume, float speedLerp)
		{
			StartCoroutine(SetWindSoundVolume(startVolume, endVolume, speedLerp));
		}

		public void SetWindSound(float startVolume, float endVolume, float speedLerp)
		{
			StartCoroutine(SetbrakeSoundVolume(startVolume, endVolume, speedLerp));
		}

		public void SetbrakeSound(float startVolume, float endVolume, float speedLerp)
		{
		}

		private IEnumerator SetEngineSoundOnVolume(float startVolume, float endVolume, float speedLerp)
		{
			float t = 0f;
			while (t < 1f)
			{
				t += Time.deltaTime * speedLerp;
				carAudioSource[0].volume = Mathf.Lerp(startVolume, endVolume, t);
				yield return new WaitForEndOfFrame();
			}
		}

		private IEnumerator SetEngineSoundOffVolume(float startVolume, float endVolume, float speedLerp)
		{
			float t = 0f;
			while (t < 1f)
			{
				t += Time.deltaTime * speedLerp;
				carAudioSource[1].volume = Mathf.Lerp(startVolume, endVolume, t);
				yield return new WaitForEndOfFrame();
			}
		}

		private IEnumerator SetEngineSoundIdleVolume(float startVolume, float endVolume, float speedLerp)
		{
			float t = 0f;
			while (t < 1f)
			{
				t += Time.deltaTime * speedLerp;
				carAudioSource[1].volume = Mathf.Lerp(startVolume, endVolume, t);
				yield return new WaitForEndOfFrame();
			}
		}

		private IEnumerator SetReversingSoundVolume(float startVolume, float endVolume, float speedLerp)
		{
			float t = 0f;
			while (t < 1f)
			{
				t += Time.deltaTime * speedLerp;
				carAudioSource[1].volume = Mathf.Lerp(startVolume, endVolume, t);
				yield return new WaitForEndOfFrame();
			}
		}

		private IEnumerator SetWindSoundVolume(float startVolume, float endVolume, float speedLerp)
		{
			float t = 0f;
			while (t < 1f)
			{
				t += Time.deltaTime * speedLerp;
				carAudioSource[1].volume = Mathf.Lerp(startVolume, endVolume, t);
				yield return new WaitForEndOfFrame();
			}
		}

		private IEnumerator SetbrakeSoundVolume(float startVolume, float endVolume, float speedLerp)
		{
			float t = 0f;
			while (t < 1f)
			{
				t += Time.deltaTime * speedLerp;
				carAudioSource[1].volume = Mathf.Lerp(startVolume, endVolume, t);
				yield return new WaitForEndOfFrame();
			}
		}
	}
}