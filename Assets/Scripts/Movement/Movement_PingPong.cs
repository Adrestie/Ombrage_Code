using System.Collections.Generic;

using NUnit.Framework.Constraints;

using UnityEngine;

using static Ombrage.Movement_PingPong;

namespace Ombrage
{
    public class Movement_PingPong : MonoBehaviour
    {
        public enum PositionType { Vector, Transform }
        public PositionType positionType;

        public List<Vector3> targetPositions = new List<Vector3>();
        public List<Transform> targetTransforms = new List<Transform>();

        public float speed = 1.0f;
        private float progress = 0f;
        public float delay = 0.0f;
        private float delayProgression = 0.0f;
        public int currentIndex = 0;
        // Start is called once before the first execution of Update after the MonoBehaviour is created
        void Start()
        {
			transform.position = positionType == PositionType.Vector ? targetPositions[currentIndex] : targetTransforms[currentIndex].position;
		}

		// Update is called once per frame
		void Update()
		{
			int count = positionType == PositionType.Vector ? targetPositions.Count : targetTransforms.Count;

			Vector3 pointA = positionType == PositionType.Vector
				? targetPositions[(currentIndex - 1 + count) % count]
				: targetTransforms[(currentIndex - 1 + count) % count].position;

			Vector3 pointB = positionType == PositionType.Vector
				? targetPositions[currentIndex]
				: targetTransforms[currentIndex].position;

			if (progress < 1f)
			{
				progress += Time.deltaTime / speed;
				transform.position = Vector3.Lerp(pointA, pointB, progress);
			}
			else
			{
				if (delay == 0f)
				{
					progress = 0f;
					currentIndex = (currentIndex + 1) % count;
				}
				else
				{
					delayProgression += Time.deltaTime;
					if (delayProgression >= delay)
					{
						delayProgression = 0f;
						progress = 0f;
						currentIndex = (currentIndex + 1) % count;
					}
				}
			}
		}
	}
}
