using UnityEngine;

public class DayNightCycle2 : MonoBehaviour
{
    [Header("Time Settings")]
    [Tooltip("Time in minutes for a full day-night cycle")]
    public float dayLengthInMinutes = 1f;

    [Header("Skybox & Lighting Settings")]
    [Tooltip("Assign your Procedural Skybox Material here")]
    public Material skyboxMaterial;

    [Tooltip("Color gradient for the skybox tint over a full rotation")]
    public Gradient skyColorGradient;

    [Tooltip("Color gradient for the directional light (sun)")]
    public Gradient lightColorGradient;

    private Light directionalLight;

    void Start()
    {
        // Grab the Light component attached to this GameObject
        directionalLight = GetComponent<Light>();

        // Ensure we have a skybox material assigned, otherwise fetch the current active one
        if (skyboxMaterial == null && RenderSettings.skybox != null)
        {
            skyboxMaterial = RenderSettings.skybox;
        }
    }

    void Update()
    {
        // 1. Calculate and apply rotation
        float rotationSpeed = 360f / (dayLengthInMinutes * 60f);
        transform.Rotate(Vector3.right * rotationSpeed * Time.deltaTime);

        // 2. Normalize the current rotation to a 0.0 to 1.0 range
        // We use the X rotation because that's the axis we are rotating around
        float currentAngle = transform.eulerAngles.x;
        float normalizedTime = currentAngle / 360f;

        // 3. Update Skybox and Light properties based on the time of day
        UpdateEnvironment(normalizedTime);
    }

    void UpdateEnvironment(float time)
    {
        if (skyboxMaterial != null)
        {
            // Evaluate colors from our gradients based on the current time (0 to 1)
            Color currentSkyColor = skyColorGradient.Evaluate(time);

            // "_SkyTint" is the standard property name for Unity's Procedural Skybox
            if (skyboxMaterial.HasProperty("_SkyTint"))
            {
                skyboxMaterial.SetColor("_SkyTint", currentSkyColor);
            }
        }

        if (directionalLight != null && lightColorGradient != null)
        {
            // Dynamically change the sun color (e.g., orange at sunset, white at noon)
            directionalLight.color = lightColorGradient.Evaluate(time);
        }
    }
}