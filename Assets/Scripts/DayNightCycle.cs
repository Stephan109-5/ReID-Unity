using UnityEngine;

public class DayNightCycle : MonoBehaviour
{
    [Tooltip("Time in minutes for a full day-night cycle")]
    public float dayLengthInMinutes = 1f; 

    void Update()
    {
        // Calculate how many degrees to rotate per second
        // 360 degrees / (minutes * 60 seconds)
        float rotationSpeed = 360f / (dayLengthInMinutes * 60f);

        // Rotate the light around the X-axis
        transform.Rotate(Vector3.right * rotationSpeed * Time.deltaTime);
    }
}