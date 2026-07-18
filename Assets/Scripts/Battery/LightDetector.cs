using Ombrage.Systems.BatteryManager;
using UnityEngine;
using System.Collections.Generic;

public class LightDetector : MonoBehaviour
{
    private BatteryManager batteryManager;
    public Transform targetSun = null;
    public List<Transform> lightDetectors = new List<Transform>();
    public List<float> amountPerDetector = new List<float>();
    public LayerMask blockingLayers = new LayerMask();

    // Start is called once before the first execution of Update after the MonoBehaviour is created
    void Start()
    {
        batteryManager = BatteryManager.Instance;
        targetSun = GameObject.FindGameObjectWithTag("Sun").GetComponent<Transform>();
    }

    // Update is called once per frame
    void Update()
    {
        int lighted = -1;
        for (int i = 0; i < lightDetectors.Count; i++)
        {
            if (lightDetectors[i] == null)
                Debug.Log("[LightDetector] Detector n°" + (i+1) + " not referenced !");
            else
            {
                Ray ray = new Ray(lightDetectors[i].position, -targetSun.forward);
				if (!Physics.Raycast(ray, Mathf.Infinity, blockingLayers))
				{
					lighted++;
                    Debug.DrawRay(ray.origin, ray.direction * 1000, Color.green);
				}
				else
                {
                    Debug.DrawRay(ray.origin, ray.direction * 1000, Color.red);
                }
            }
        }

       

        if (lighted > -1 && batteryManager.CanRestore(amountPerDetector[lighted]))
        {
            batteryManager.Restore(amountPerDetector[lighted] * Time.deltaTime);
        }

    }
}
