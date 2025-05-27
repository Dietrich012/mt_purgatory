using UnityEngine;
using UnityEngine.AI;
using UnityEngine.SceneManagement;
using System.Collections.Generic;
using System.Linq;
using System.Collections;

public class EnemyAI : MonoBehaviour
{
    // --- Banshee State Management ---
    public enum BansheeState
    {
        Idle,
        Patrol,    // Moving to a random point within radius
        Walking,   // Sub-state of Patrol/Investigate when actively moving
        Investigate, // Banshee goes to the alert location and idles
        Roaring,     // Plays roar audio/animation, stops moving
        Chase,       // Chases the player
        Jumpscare    // Added Jumpscare state
    }
    public BansheeState currentState;

    [Header("Banshee Movement Settings")]
    [Tooltip("The speed of the Banshee when patrolling or investigating.")]
    public float patrolSpeed = 2f;
    [Tooltip("The speed of the Banshee when chasing the player.")]
    public float chaseSpeed = 5f;
    [Tooltip("The radius within which the Banshee will pick a random patrol point.")]
    public float patrolPointRadius = 10f;
    [Tooltip("Minimum time (in seconds) the Banshee stays idle at a patrol point.")]
    public float minIdleTimeAtPatrol = 2f;
    [Tooltip("Maximum time (in seconds) the Banshee stays idle at a patrol point.")]
    public float maxIdleTimeAtPatrol = 5f;
    [Tooltip("Distance threshold for the Banshee to consider it has reached its destination (patrol or investigation point).")]
    public float arrivalThreshold = 1.0f;

    [Header("Investigate Behavior")]
    [Tooltip("Time (in seconds) the Banshee stays idle at an investigation point before returning to patrol.")]
    public float idleTimeAtInvestigatePoint = 2.0f;
    private float currentInvestigateIdleTimer;

    [Header("Player Detection Settings")]
    [Tooltip("The range at which the Banshee can detect the player.")]
    public float detectionRange = 15f;
    [Tooltip("The range at which the Banshee will stop chasing and return to patrol.")]
    public float losePlayerRange = 20f;
    [Tooltip("The layer(s) the player belongs to.")]
    public LayerMask playerLayer;

    [Header("Roar & Chase Behavior")]
    [Tooltip("Time (in seconds) the Banshee stops moving to roar after spotting the player.")]
    public float roarStopDuration = 1.0f;
    [Tooltip("AudioClip to play when the Banshee roars.")]
    public AudioClip roarAudioClip;

    [Header("Jumpscare Behavior")]
    [Tooltip("Distance at which the Banshee triggers a jumpscare when chasing the player.")]
    public float jumpscareDistance = 2f;
    [Tooltip("Duration (in seconds) of the jumpscare before loading the menu scene.")]
    public float jumpscareDuration = 2f;
    [Tooltip("Camera to activate during the jumpscare effect.")]
    public GameObject jumpscareCam;
    [Tooltip("Scene to load after the jumpscare ends (e.g., Main Menu).")]
    public string menuScene = "MainMenu";

    [Header("References")]
    [Tooltip("Reference to the player's Transform. This will be updated when the player is detected.")]
    public Transform playerTransform;
    [Tooltip("Reference to the NavMeshAgent component.")]
    public NavMeshAgent agent;
    [Tooltip("Reference to the Animator component.")]
    public Animator animator;
    [Tooltip("Reference to the AudioSource component.")]
    public AudioSource audioSource;

    private Vector3 currentPatrolDestination;
    private float currentIdleTimeAtPatrol;
    private float roarTimer;
    private Vector3 lastAlertPosition;
    private bool isJumpscaring = false; // Flag to prevent updates during jumpscare

    void Start()
    {
        if (agent == null) agent = GetComponent<NavMeshAgent>();
        if (animator == null) animator = GetComponent<Animator>();
        if (audioSource == null) audioSource = GetComponent<AudioSource>();

        if (playerTransform == null)
        {
            GameObject playerObj = GameObject.FindGameObjectWithTag("Player");
            if (playerObj != null)
            {
                playerTransform = playerObj.transform;
            }
            else
            {
                Debug.LogWarning("Player GameObject with tag 'Player' not found at Start. BansheeAI will rely on external triggers or later detection to find the player.", this);
            }
        }

        currentState = BansheeState.Patrol;
        SetNewPatrolDestination();
        SetAnimation("Idle");
    }

    void Update()
    {
        if (isJumpscaring) return; // Skip updates during jumpscare

        // --- GLOBAL PLAYER DETECTION (HIGH PRIORITY) ---
        if (playerTransform != null && currentState != BansheeState.Roaring && currentState != BansheeState.Chase && currentState != BansheeState.Jumpscare)
        {
            float distanceToPlayer = Vector3.Distance(transform.position, playerTransform.position);
            if (distanceToPlayer <= detectionRange)
            {
                TransitionToRoarState();
                return;
            }
        }

        // --- State Specific Logic ---
        switch (currentState)
        {
            case BansheeState.Idle:
                HandleIdleState();
                break;
            case BansheeState.Patrol:
                HandlePatrolState();
                break;
            case BansheeState.Walking:
                HandleWalkingState();
                break;
            case BansheeState.Investigate:
                HandleInvestigateState();
                break;
            case BansheeState.Roaring:
                HandleRoaringState();
                break;
            case BansheeState.Chase:
                HandleChaseState();
                break;
            case BansheeState.Jumpscare:
                // Handled by TriggerJumpscare and coroutine
                break;
        }

        // Handle losing player while chasing
        if (currentState == BansheeState.Chase && playerTransform != null)
        {
            float distanceToPlayer = Vector3.Distance(transform.position, playerTransform.position);
            if (distanceToPlayer > losePlayerRange)
            {
                TransitionToPatrolState();
            }
        }
    }

    void HandleIdleState()
    {
        agent.isStopped = true;
        SetAnimation("Idle");

        currentIdleTimeAtPatrol -= Time.deltaTime;
        if (currentIdleTimeAtPatrol <= 0)
        {
            TransitionToPatrolState();
        }
    }

    void HandlePatrolState()
    {
        agent.speed = patrolSpeed;
        agent.isStopped = false;
        SetAnimation("Walk");

        if (!agent.pathPending && agent.remainingDistance < arrivalThreshold && agent.hasPath)
        {
            currentState = BansheeState.Idle;
            currentIdleTimeAtPatrol = Random.Range(minIdleTimeAtPatrol, maxIdleTimeAtPatrol);
            SetAnimation("Idle");
        }
        else if (agent.isStopped || !agent.hasPath || agent.velocity.magnitude < 0.1f)
        {
            SetNewPatrolDestination();
            if (!agent.hasPath || agent.remainingDistance < arrivalThreshold)
            {
                currentState = BansheeState.Idle;
                currentIdleTimeAtPatrol = Random.Range(minIdleTimeAtPatrol, maxIdleTimeAtPatrol);
                SetAnimation("Idle");
            }
        }
    }

    void HandleWalkingState()
    {
        agent.speed = patrolSpeed;
        agent.isStopped = false;
        SetAnimation("Walk");
    }

    void HandleInvestigateState()
    {
        agent.speed = patrolSpeed;
        agent.isStopped = false;
        SetAnimation("Walk");

        if (!agent.pathPending && agent.remainingDistance < arrivalThreshold && agent.hasPath)
        {
            agent.isStopped = true;
            SetAnimation("Idle");
            currentInvestigateIdleTimer -= Time.deltaTime;

            if (currentInvestigateIdleTimer <= 0)
            {
                TransitionToPatrolState();
            }
        }
        else if (agent.isStopped)
        {
            agent.SetDestination(lastAlertPosition);
            if (agent.isStopped)
            {
                TransitionToPatrolState();
            }
        }
    }

    void HandleRoaringState()
    {
        agent.isStopped = true;
        SetAnimation("Roar");

        roarTimer -= Time.deltaTime;
        if (roarTimer <= 0)
        {
            TransitionToChaseState();
        }
    }

    void HandleChaseState()
    {
        if (playerTransform != null)
        {
            agent.speed = chaseSpeed;
            agent.isStopped = false;
            agent.SetDestination(playerTransform.position);
            SetAnimation("Run");

            // Check for jumpscare distance
            float distanceToPlayer = Vector3.Distance(transform.position, playerTransform.position);
            if (distanceToPlayer <= jumpscareDistance)
            {
                TriggerJumpscare();
            }
        }
        else
        {
            TransitionToPatrolState();
        }
    }

    public void TriggerAlert(Vector3 alertPosition)
    {
        if (currentState != BansheeState.Roaring && currentState != BansheeState.Chase && currentState != BansheeState.Jumpscare)
        {
            lastAlertPosition = alertPosition;
            agent.SetDestination(lastAlertPosition);
            agent.speed = patrolSpeed;
            agent.isStopped = false;
            SetAnimation("Walk");
            currentState = BansheeState.Investigate;
            currentInvestigateIdleTimer = idleTimeAtInvestigatePoint;

            Debug.Log(gameObject.name + " is investigating alert at: " + alertPosition);
        }
    }

    void TransitionToRoarState()
    {
        Debug.Log(gameObject.name + " is roaring and preparing to chase!");
        currentState = BansheeState.Roaring;
        agent.isStopped = true;
        roarTimer = roarStopDuration;
        SetAnimation("Roar");

        if (audioSource != null && roarAudioClip != null)
        {
            audioSource.PlayOneShot(roarAudioClip);
        }
    }

    void TransitionToChaseState()
    {
        Debug.Log(gameObject.name + " is now chasing the player!");
        currentState = BansheeState.Chase;
        agent.speed = chaseSpeed;
        agent.isStopped = false;
        if (playerTransform != null)
        {
            agent.SetDestination(playerTransform.position);
        }
        SetAnimation("Run");
    }

    void TransitionToPatrolState()
    {
        Debug.Log(gameObject.name + " is returning to patrol.");
        currentState = BansheeState.Patrol;
        agent.speed = patrolSpeed;
        agent.isStopped = false;
        SetNewPatrolDestination();
        SetAnimation("Walk");
    }

    void SetNewPatrolDestination()
    {
        Vector3 randomDirection = Random.insideUnitSphere * patrolPointRadius;
        randomDirection += transform.position;

        NavMeshHit hit;
        if (NavMesh.SamplePosition(randomDirection, out hit, patrolPointRadius, NavMesh.AllAreas))
        {
            currentPatrolDestination = hit.position;
            agent.SetDestination(currentPatrolDestination);
        }
        else
        {
            currentPatrolDestination = transform.position;
            agent.SetDestination(currentPatrolDestination);
            Debug.LogWarning("Could not find valid patrol point within radius, staying at current position.", this);
        }
    }

    void SetAnimation(string animName)
    {
        if (animator != null)
        {
            animator.SetBool("Idle", false);
            animator.SetBool("Walk", false);
            animator.SetBool("Run", false);
            animator.SetBool("Roar", false);
            animator.SetBool("Jumpscare", animName == "Jumpscare"); // Added Jumpscare animation parameter
            animator.SetBool(animName, true);
        }
    }

    void TriggerJumpscare()
    {
        Debug.Log(gameObject.name + " is triggering jumpscare!");
        currentState = BansheeState.Jumpscare;
        isJumpscaring = true;
        agent.isStopped = true;
        SetAnimation("Jumpscare"); // Assumes "Jumpscare" animation parameter exists

        if (playerTransform != null) playerTransform.gameObject.SetActive(false); // Disable player
        if (jumpscareCam != null) jumpscareCam.SetActive(true); // Enable jumpscare camera
        StartCoroutine(GameOver());
    }

    IEnumerator GameOver()
    {
        yield return new WaitForSeconds(jumpscareDuration);
        SceneManager.LoadScene(menuScene);
    }

    void OnDrawGizmosSelected()
    {
        Gizmos.color = Color.yellow;
        Gizmos.DrawWireSphere(transform.position, detectionRange);

        Gizmos.color = Color.red;
        Gizmos.DrawWireSphere(transform.position, losePlayerRange);

        Gizmos.color = Color.blue;
        Gizmos.DrawSphere(currentPatrolDestination, 0.5f);
        Gizmos.DrawWireSphere(currentPatrolDestination, arrivalThreshold);

        Gizmos.color = Color.magenta;
        Gizmos.DrawSphere(lastAlertPosition, 0.75f);
        Gizmos.DrawWireSphere(lastAlertPosition, arrivalThreshold);

        Gizmos.color = Color.green;
        Gizmos.DrawWireSphere(transform.position, patrolPointRadius);

        // Draw jumpscare distance
        Gizmos.color = Color.black;
        Gizmos.DrawWireSphere(transform.position, jumpscareDistance);
    }
}