using UnityEngine;
using UnityEngine.AI;
using UnityEngine.SceneManagement;
using System.Collections.Generic;
using System.Linq;
using System.Collections;

public enum EnemyState
{
    Idle,
    Patrol,
    Chase,
    Alert,
    Jumpscare
}

[RequireComponent(typeof(NavMeshAgent))]
public class EnemyAI : MonoBehaviour
{
    private Animator animator;
    private NavMeshAgent agent;
    private EnemyState currentState;
    private AnimationClip walkClip;

    public float patrolRadius = 20f; // Radius for circular patrol
    public Transform[] patrolPoints; // Array of specific patrol points (capsules)
    public bool useCircularPatrol = true; // Toggle between circular or specific points
    public float detectionRadius = 10f; // Radius for player detection
    public float chaseSpeed = 5f; // Speed when chasing or in alert
    public float jumpscareDistance = 2f; // Distance to trigger jumpscare during Chase
    public float JSDuration = 2f; // Duration of jumpscare before loading main menu
    public Transform player; // Player to chase and disable during jumpscare
    public GameObject JumpscareCam; // Camera for jumpscare effect
    public string MenuScene = "MainMenu"; // Scene to load after jumpscare
    public float walkDistancePerCycle = 1.2f; // Distance per walk cycle for animation sync
    public float animationSpeedMultiplier = 0.5f; // Multiplier to slow down animation (0.5 = half speed)

    private float idleTimer;
    private bool isJumpscaring = false; // Flag to prevent updates during jumpscare
    private Vector3 patrolCenter;
    private List<int> patrolOrder; // For randomizing specific points
    private int currentPatrolIndex;
    private List<Vector3> alertPositions = new List<Vector3>(); // Store multiple alert positions

    void Start()
    {
        animator = GetComponent<Animator>();
        agent = GetComponent<NavMeshAgent>();
        agent.speed = 3.5f; // Default patrol speed
        agent.angularSpeed = 360f;
        agent.acceleration = 20f;
        agent.stoppingDistance = 0.5f;

        currentState = EnemyState.Idle;
        idleTimer = Random.Range(1f, 3f); // Random idle time (1-3 seconds)
        patrolCenter = transform.position; // Center for circular patrol
        patrolOrder = new List<int>();

        // Find the walk animation clip
        foreach (AnimationClip clip in animator.runtimeAnimatorController.animationClips)
        {
            if (clip.name == "walk" || clip.name.Contains("zombie walk"))
            {
                walkClip = clip;
                break;
            }
        }

        // Randomize patrol points order if using specific points
        if (!useCircularPatrol && patrolPoints.Length > 0)
        {
            for (int i = 0; i < patrolPoints.Length; i++) patrolOrder.Add(i);
            ShufflePatrolOrder();
            currentPatrolIndex = 0;
        }

        if (player == null)
        {
            player = GameObject.FindGameObjectWithTag("Player")?.transform;
            if (player == null) Debug.LogError("Player not found! Assign the player Transform or tag a GameObject as 'Player'.");
        }
    }

    void Update()
    {
        if (player == null || isJumpscaring)
        {
            Debug.Log("Player null or jumpscaring, skipping Update");
            return;
        }

        // Sync animation speed with agent movement, slowed by multiplier
        float normalizedSpeed = Mathf.Clamp(agent.velocity.magnitude / agent.speed, 0f, 1f);
        animator.SetFloat("Speed", normalizedSpeed);
        if (walkClip != null && agent.velocity.magnitude > 0)
        {
            float cyclesPerSecond = agent.velocity.magnitude / walkDistancePerCycle;
            animator.speed = cyclesPerSecond * walkClip.length * animationSpeedMultiplier;
        }
        else
        {
            animator.speed = 1f * animationSpeedMultiplier; // Slowed idle animation
        }

        Debug.Log($"State: {currentState}, Speed: {normalizedSpeed}, Velocity: {agent.velocity.magnitude}, Animator Speed: {animator.speed}");

        // Detect player within detection radius
        float distanceToPlayer = Vector3.Distance(transform.position, player.position);
        if (distanceToPlayer <= detectionRadius && currentState != EnemyState.Chase && currentState != EnemyState.Jumpscare)
        {
            currentState = EnemyState.Chase;
            Debug.Log("Switching to Chase");
        }
        else if (distanceToPlayer > detectionRadius && currentState == EnemyState.Chase)
        {
            currentState = EnemyState.Patrol;
            Debug.Log("Switching to Patrol");
        }

        // State machine
        switch (currentState)
        {
            case EnemyState.Idle:
                Idle();
                break;
            case EnemyState.Patrol:
                Patrol();
                break;
            case EnemyState.Chase:
                Chase();
                break;
            case EnemyState.Alert:
                Alert();
                break;
            case EnemyState.Jumpscare:
                // Handled by TriggerJumpscare and GameOver coroutine
                break;
        }
    }

    void Idle()
    {
        agent.isStopped = true; // Stop movement
        agent.velocity = Vector3.zero;
        animator.speed = 1f * animationSpeedMultiplier; // Slowed idle animation
        idleTimer -= Time.deltaTime;

        if (idleTimer <= 0f)
        {
            idleTimer = Random.Range(1f, 3f); // Random idle time (1-3 seconds)
            currentState = EnemyState.Patrol;
            MoveToNextPatrolPoint();
            Debug.Log("Idle -> Patrol: Starting movement");
        }
    }

    void Patrol()
    {
        agent.isStopped = false; // Allow movement
        agent.speed = 3.5f; // Normal patrol speed

        if (!agent.pathPending && agent.remainingDistance <= agent.stoppingDistance)
        {
            currentState = EnemyState.Idle; // Return to Idle
            MoveToNextPatrolPoint();
            Debug.Log("Patrol: Moving to new point");
        }
    }

    void Chase()
    {
        agent.isStopped = false; // Allow movement
        agent.speed = chaseSpeed; // Chase speed

        agent.destination = player.position;

        float distanceToPlayer = Vector3.Distance(transform.position, player.position);
        if (distanceToPlayer <= jumpscareDistance)
        {
            TriggerJumpscare();
        }
    }

    void Alert()
    {
        agent.isStopped = false; // Allow movement
        agent.speed = chaseSpeed; // Use chase speed for alert

        // Move to the closest alert position
        if (alertPositions.Count > 0)
        {
            Vector3 closestAlert = alertPositions.OrderBy(pos => Vector3.Distance(transform.position, pos)).First();
            agent.SetDestination(closestAlert);
            Debug.Log($"Alert: Moving to {closestAlert}, Distance: {Vector3.Distance(transform.position, closestAlert)}");

            // Check if reached the closest alert destination
            if (!agent.pathPending && agent.remainingDistance <= agent.stoppingDistance)
            {
                alertPositions.Remove(closestAlert); // Remove reached position
                Debug.Log("Alert: Reached position, removing from list");
                if (alertPositions.Count > 0)
                {
                    agent.SetDestination(alertPositions.OrderBy(pos => Vector3.Distance(transform.position, pos)).First());
                }
                else
                {
                    // Check if player is in detection radius after clearing alerts
                    float distanceToPlayer = Vector3.Distance(transform.position, player.position);
                    if (distanceToPlayer <= detectionRadius)
                    {
                        currentState = EnemyState.Chase; // Chase if player is detected
                        Debug.Log("Alert: Player detected after clearing alerts, switching to Chase");
                    }
                    else
                    {
                        currentState = EnemyState.Idle; // Return to Idle if no player detected
                        Debug.Log("Alert: No player detected after clearing alerts, switching to Idle");
                    }
                }
            }
        }
        else
        {
            currentState = EnemyState.Idle; // Return to Idle if no alerts
            Debug.Log("Alert: No alert positions, switching to Idle");
        }
    }

    void MoveToNextPatrolPoint()
    {
        if (useCircularPatrol)
        {
            // Circular patrol: Random point within radius
            Vector3 randomDirection = Random.insideUnitSphere * patrolRadius + patrolCenter;
            NavMeshHit hit;
            if (NavMesh.SamplePosition(randomDirection, out hit, patrolRadius, NavMesh.AllAreas))
            {
                agent.SetDestination(hit.position);
                Debug.Log($"MoveToRandomPoint: Set destination to {hit.position}");
            }
            else
            {
                Vector3 fallbackPoint = transform.position + Random.insideUnitSphere * 5f;
                if (NavMesh.SamplePosition(fallbackPoint, out hit, 5f, NavMesh.AllAreas))
                {
                    agent.SetDestination(hit.position);
                    Debug.Log($"MoveToRandomPoint: Fallback destination {hit.position}");
                }
                else
                {
                    Debug.LogWarning("MoveToRandomPoint: No valid NavMesh position found");
                }
            }
        }
        else if (patrolPoints.Length > 0)
        {
            // Specific points: Move to next point in randomized order
            agent.SetDestination(patrolPoints[patrolOrder[currentPatrolIndex]].position);
            currentPatrolIndex = (currentPatrolIndex + 1) % patrolOrder.Count;
            if (currentPatrolIndex == 0) ShufflePatrolOrder(); // Reshuffle after full cycle
            Debug.Log($"Patrol: Moving to point {patrolOrder[currentPatrolIndex]}");
        }
    }

    void ShufflePatrolOrder()
    {
        // Fisher-Yates shuffle for patrol points order
        for (int i = 0; i < patrolOrder.Count - 1; i++)
        {
            int j = Random.Range(i, patrolOrder.Count);
            int temp = patrolOrder[i];
            patrolOrder[i] = patrolOrder[j];
            patrolOrder[j] = temp;
        }
    }

    // Add or update an alert position
    public void TriggerAlert(Vector3 alertPosition)
    {
        if (!alertPositions.Contains(alertPosition))
        {
            alertPositions.Add(alertPosition); // Add new alert position
            Debug.Log($"TriggerAlert: Added position {alertPosition}, Total alerts: {alertPositions.Count}");
        }
        if (currentState != EnemyState.Jumpscare && currentState != EnemyState.Chase) // Avoid interrupting jumpscare or chase
        {
            currentState = EnemyState.Alert;
            agent.SetDestination(alertPositions.OrderBy(pos => Vector3.Distance(transform.position, pos)).First());
            Debug.Log($"TriggerAlert: Switching to Alert, moving to {alertPosition}");
        }
        else
        {
            Debug.Log($"TriggerAlert: Ignored, current state is {currentState}");
        }
    }

    void TriggerJumpscare()
    {
        currentState = EnemyState.Jumpscare;
        isJumpscaring = true;
        agent.isStopped = true; // Stop movement
        animator.SetTrigger("Jumpscare"); // Play jumpscare animation
        Debug.Log("Triggering Jumpscare");

        if (player != null) player.gameObject.SetActive(false); // Disable player
        if (JumpscareCam != null) JumpscareCam.gameObject.SetActive(true); // Enable jumpscare camera
        StartCoroutine(GameOver()); // Start coroutine to load main menu
    }

    IEnumerator GameOver()
    {
        yield return new WaitForSeconds(JSDuration); // Wait for jumpscare duration
        SceneManager.LoadScene(MenuScene); // Load main menu scene
    }

    void OnDrawGizmosSelected()
    {
        // Visualize patrol radius (yellow)
        Gizmos.color = Color.yellow;
        Gizmos.DrawWireSphere(patrolCenter, patrolRadius);

        // Visualize detection radius (red)
        Gizmos.color = Color.red;
        Gizmos.DrawWireSphere(patrolCenter, detectionRadius);
    }
}