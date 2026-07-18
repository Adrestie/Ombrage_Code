using Ombrage.Systems.BatteryManager;
using UnityEngine;
using UnityEngine.UI;

public class BatteryUI : MonoBehaviour
{
    BatteryManager batteryManager;

    public Image batteryBar;


    // Start is called once before the first execution of Update after the MonoBehaviour is created
    void Start()
    {
        batteryManager = BatteryManager.Instance;

        batteryManager.OnBatteryUsed += batteryUsed;
        batteryManager.OnBatteryRestored += batteryRestored;
    }

    public void batteryUsed(float amount)
    {
        batteryBar.rectTransform.localScale = new Vector3(amount, 1, 1);
    }

    public void batteryRestored(float amount)
    {
		batteryBar.rectTransform.localScale = new Vector3(amount, 1, 1);
	}


	// Update is called once per frame
	void Update()
    {
        
    }
}
