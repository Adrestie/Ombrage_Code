//----------------------------------------------
//            Realistic Car Controller
//
// Copyright © 2015 BoneCracker Games
// http://www.bonecrackergames.com
//
//----------------------------------------------

using UnityEngine;
using UnityEditor;
using System.Collections;
namespace RCC3
{
	public class RCC_EditorWindows : Editor
	{

		[MenuItem("Tools/BoneCracker Games/Realistic Car Controller/Edit Asset Settings", false, -10)]
		public static void OpenAssetSettings()
		{
			Selection.activeObject = RCC_Settings.Instance;
		}

		[MenuItem("Tools/BoneCracker Games/Realistic Car Controller/Configure Ground Materials", false, -8)]
		public static void OpenGroundMaterialsSettings()
		{
			Selection.activeObject = RCC_GroundMaterials.Instance;
		}

		[MenuItem("Tools/BoneCracker Games/Realistic Car Controller/Misc/Add Exhaust To Vehicle", false, 10)]
		public static void CreateExhaust()
		{

			if (!Selection.activeGameObject.GetComponentInParent<RCC_CarControllerV3>())
			{

				EditorUtility.DisplayDialog("Select a vehicle controlled by Realistic Car Controller!", "Select a vehicle controlled by Realistic Car Controller!", "Ok");

			}
			else
			{

				GameObject exhaustsMain;

				if (!Selection.activeGameObject.GetComponentInParent<RCC_CarControllerV3>().transform.Find(Selection.activeGameObject.GetComponentInParent<RCC_CarControllerV3>().chassis.name + "/Exhausts"))
				{
					exhaustsMain = new GameObject("Exhausts");
					exhaustsMain.transform.SetParent(Selection.activeGameObject.GetComponentInParent<RCC_CarControllerV3>().chassis.transform, false);
				}
				else
				{
					exhaustsMain = Selection.activeGameObject.GetComponentInParent<RCC_CarControllerV3>().transform.Find(Selection.activeGameObject.GetComponentInParent<RCC_CarControllerV3>().chassis.name + "/Exhausts").gameObject;
				}

				GameObject exhaust = (GameObject)Instantiate(RCC_Settings.Instance.exhaustGas, Selection.activeGameObject.transform.position, Selection.activeGameObject.transform.rotation * Quaternion.Euler(0f, 180f, 0f));
				exhaust.transform.SetParent(exhaustsMain.transform);
				exhaust.transform.localPosition = new Vector3(1f, 0f, -2f);
				RCC_LabelEditor.SetIcon(exhaust, RCC_LabelEditor.Icon.DiamondGray);
				Selection.activeGameObject = exhaust;

			}

		}

		[MenuItem("Tools/BoneCracker Games/Realistic Car Controller/Misc/Add Exhaust To Vehicle", true)]
		public static bool CheckCreateExhaust()
		{
			if (Selection.gameObjects.Length > 1 || !Selection.activeTransform)
				return false;
			else
				return true;
		}
	}
}