using UnityEngine;
using UnityEngine.AI;

public class npcwalk : MonoBehaviour
{
    public Transform[] waypoints; // Drag your 4 points here in the Inspector
    private int currentPointIndex = 0;
    private NavMeshAgent agent;
    public float walkSpeed= 1.0f;

    void Start()
    {
        agent = GetComponent<NavMeshAgent>();
        agent.speed = walkSpeed;

        if (waypoints.Length > 0)
        {
            GoToNextWaypoint();
        }
    }

    void Update()
    {
        // Check if we have reached the destination
        // remainingDistance: how far is the NPC from the target
        // stoppingDistance: a small buffer so the NPC doesn't get stuck trying to be pixel-perfect
        if (!agent.pathPending && agent.remainingDistance < 0.5f)
        {
            GoToNextWaypoint();
        }
    }

    void GoToNextWaypoint()
    {
        if (waypoints.Length == 0) return;

        // Set the destination to the current waypoint
        agent.destination = waypoints[currentPointIndex].position;

        // Move to the next index, and loop back to 0 if we hit the end
        currentPointIndex = (currentPointIndex + 1) % waypoints.Length;
    }
}
