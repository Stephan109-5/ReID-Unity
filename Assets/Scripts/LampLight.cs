using UnityEngine;

public class LampLight : MonoBehaviour
{
    [Tooltip("Reference to the Directional Light causing the day/night cycle")]
    public Transform directionalLight;

    [Tooltip("The Point Light component to turn on and off")]
    public Light pointLight;

    [Tooltip("The threshold angle below the horizon where lights turn on. (0 = exact sunset)")]
    public float turnOnAngle = 10f;

    void Start()
    {
        // Automatically try to grab the Light component if not assigned
        if (pointLight == null)
        {
            pointLight = GetComponent<Light>();
        }
    }

    void Update()
    {
        if (directionalLight == null) return;

        // Get the current X rotation of the directional light (clamped between 0 and 360)
        float currentXRotation = directionalLight.eulerAngles.x;

        // Unity eulerAngles can sometimes be tricky, so we check if the light is 
        // shining "upwards" into the sky, representing night.
        // A standard rotation puts night roughly between 180 and 360 degrees.
        bool isNight = currentXRotation > (180f - turnOnAngle) && currentXRotation < (360f + turnOnAngle);

        // Toggle the light component based on the result
        if (pointLight != null)
        {
            pointLight.enabled = isNight;
        }
    }
}