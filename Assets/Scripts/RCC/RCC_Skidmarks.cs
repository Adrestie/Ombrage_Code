using UnityEngine;
namespace RCC3
{
	[AddComponentMenu("BoneCracker Games/Realistic Car Controller/Misc/Skidmarks")]
	public class RCC_Skidmarks : MonoBehaviour
	{
		private class markSection
		{
			public Vector3 pos = Vector3.zero;

			public Vector3 normal = Vector3.zero;

			public Vector4 tangent = Vector4.zero;

			public Vector3 posl = Vector3.zero;

			public Vector3 posr = Vector3.zero;

			public float intensity;

			public int lastIndex;
		}

		public int maxMarks = 1024;

		public float markWidth = 0.275f;

		public float groundOffset = 0.02f;

		public float minDistance = 0.1f;

		private int indexShift;

		private int numMarks;

		private markSection[] skidmarks;

		private bool updated;

		private void Start()
		{
			if (base.transform.position != Vector3.zero)
			{
				base.transform.position = Vector3.zero;
			}
		}

		private void Awake()
		{
			skidmarks = new markSection[maxMarks];
			for (int i = 0; i < maxMarks; i++)
			{
				skidmarks[i] = new markSection();
			}
			if (GetComponent<MeshFilter>().mesh == null)
			{
				GetComponent<MeshFilter>().mesh = new Mesh();
			}
		}

		public int AddSkidMark(Vector3 pos, Vector3 normal, float intensity, int lastIndex)
		{
			if (intensity > 1f)
			{
				intensity = 1f;
			}
			if (intensity < 0f)
			{
				return -1;
			}
			markSection markSection2 = skidmarks[numMarks % maxMarks];
			markSection2.pos = pos + normal * groundOffset;
			markSection2.normal = normal;
			markSection2.intensity = intensity;
			markSection2.lastIndex = lastIndex;
			if (lastIndex != -1)
			{
				markSection markSection3 = skidmarks[lastIndex % maxMarks];
				Vector3 lhs = markSection2.pos - markSection3.pos;
				Vector3 normalized = Vector3.Cross(lhs, normal).normalized;
				markSection2.posl = markSection2.pos + normalized * markWidth * 0.5f;
				markSection2.posr = markSection2.pos - normalized * markWidth * 0.5f;
				markSection2.tangent = new Vector4(normalized.x, normalized.y, normalized.z, 1f);
				if (markSection3.lastIndex == -1)
				{
					markSection3.tangent = markSection2.tangent;
					markSection3.posl = markSection2.pos + normalized * markWidth * 0.5f;
					markSection3.posr = markSection2.pos - normalized * markWidth * 0.5f;
				}
			}
			numMarks++;
			updated = true;
			return numMarks - 1;
		}

		private void LateUpdate()
		{
			if (!updated)
			{
				return;
			}
			updated = false;
			Mesh mesh = GetComponent<MeshFilter>().mesh;
			mesh.Clear();
			int num = 0;
			for (int i = 0; i < numMarks && i < maxMarks; i++)
			{
				if (skidmarks[i].lastIndex != -1 && skidmarks[i].lastIndex > numMarks - maxMarks)
				{
					num++;
				}
			}
			Vector3[] array = new Vector3[num * 4];
			Vector3[] array2 = new Vector3[num * 4];
			Vector4[] array3 = new Vector4[num * 4];
			Color[] array4 = new Color[num * 4];
			Vector2[] array5 = new Vector2[num * 4];
			int[] array6 = new int[num * 6];
			num = 0;
			for (int j = 0; j < numMarks && j < maxMarks; j++)
			{
				if (skidmarks[j].lastIndex != -1 && skidmarks[j].lastIndex > numMarks - maxMarks)
				{
					markSection markSection2 = skidmarks[j];
					markSection markSection3 = skidmarks[markSection2.lastIndex % maxMarks];
					array[num * 4] = markSection3.posl;
					array[num * 4 + 1] = markSection3.posr;
					array[num * 4 + 2] = markSection2.posl;
					array[num * 4 + 3] = markSection2.posr;
					array2[num * 4] = markSection3.normal;
					array2[num * 4 + 1] = markSection3.normal;
					array2[num * 4 + 2] = markSection2.normal;
					array2[num * 4 + 3] = markSection2.normal;
					array3[num * 4] = markSection3.tangent;
					array3[num * 4 + 1] = markSection3.tangent;
					array3[num * 4 + 2] = markSection2.tangent;
					array3[num * 4 + 3] = markSection2.tangent;
					array4[num * 4] = new Color(0f, 0f, 0f, markSection3.intensity);
					array4[num * 4 + 1] = new Color(0f, 0f, 0f, markSection3.intensity);
					array4[num * 4 + 2] = new Color(0f, 0f, 0f, markSection2.intensity);
					array4[num * 4 + 3] = new Color(0f, 0f, 0f, markSection2.intensity);
					array5[num * 4] = new Vector2(0f, 0f);
					array5[num * 4 + 1] = new Vector2(1f, 0f);
					array5[num * 4 + 2] = new Vector2(0f, 1f);
					array5[num * 4 + 3] = new Vector2(1f, 1f);
					array6[num * 6] = num * 4;
					array6[num * 6 + 2] = num * 4 + 1;
					array6[num * 6 + 1] = num * 4 + 2;
					array6[num * 6 + 3] = num * 4 + 2;
					array6[num * 6 + 5] = num * 4 + 1;
					array6[num * 6 + 4] = num * 4 + 3;
					num++;
				}
			}
			mesh.vertices = array;
			mesh.normals = array2;
			mesh.tangents = array3;
			mesh.triangles = array6;
			mesh.colors = array4;
			mesh.uv = array5;
		}
	}
}