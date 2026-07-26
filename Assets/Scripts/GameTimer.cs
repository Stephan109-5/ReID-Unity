using UnityEngine;
using TMPro; // Include this if you are using TextMeshPro for the UI

public class GameTimer : MonoBehaviour
{
    public float timeRemaining = 60f; // Set your desired time in seconds
    public bool timerIsRunning = false;
    public TMP_Text timeText; // Reference to your UI Text

    private void Start()
    {
        // Starts the timer automatically
        timerIsRunning = true;
    }

    void Update()
    {
        if (timerIsRunning)
        {
            if (timeRemaining > 0)
            {
                timeRemaining -= Time.deltaTime;
                if (timeText != null) DisplayTime(timeRemaining);
            }
            else
            {
                Debug.Log("Time has run out!");
                timeRemaining = 0;
                timerIsRunning = false;
                GameOver();
            }
        }
    }

    void DisplayTime(float timeToDisplay)
    {
        // Formats the float into minutes and seconds
        float minutes = Mathf.FloorToInt(timeToDisplay / 60);
        float seconds = Mathf.FloorToInt(timeToDisplay % 60);
        timeText.text = string.Format("{0:00}:{1:00}", minutes, seconds);
    }

    void GameOver()
    {
        // Logic to stop the game
        Time.timeScale = 0; // Freezes the game world
        UnityEditor.EditorApplication.isPlaying = false;
    }
}