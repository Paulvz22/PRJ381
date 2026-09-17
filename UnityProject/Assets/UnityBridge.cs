using UnityEngine;

public class UnityBridge : MonoBehaviour
{
    // This method sends a message back up to the .NET MAUI activity tracking loop
    public void NotifyMauiOfBuilding(string buildingId)
    {
#if UNITY_ANDROID && !UNITY_EDITOR
        using (AndroidJavaClass jc = new AndroidJavaClass("com.unity3d.player.UnityPlayer"))
        {
            // Locates the active native Android window shell created by your MAUI app
            AndroidJavaObject jo = jc.GetStatic<AndroidJavaObject>("currentActivity");
            
            // Calls a method named "OnBuildingDetected" that we will declare in MAUI
            jo.Call("OnBuildingDetected", buildingId); 
        }
#endif
    }
}