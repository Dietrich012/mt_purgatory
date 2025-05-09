using UnityEngine;
using UnityEngine.AI;

public enum EnemyState
{
    Idle,
    Patrol,
    Chase
}

[RequireComponent(typeof(NavMeshAgent))]
public class EnemyAI : MonoBehaviour
{
    private EnemyState startingState = EnemyState.Idle;
    //idle state
    [SerializeField] private float idleDuration = 5f;
    [SerializeField] private float idleChanceWhilePatrolling = 0.5f;
    //patrol state
    [SerializeField] private float patrolRadius = 20f;
    [SerializeField] private Vector3 patrolCenter = Vector3.zero;
    //chas state
    [SerializeField] private float chaseRange = 10f;
    [SerializeField] private Transform player;

    private EnemyState currentState;
    private NavMeshAgent agent;
    private float idleTimer;
    private float idleCheckTimer;

    void Start()
    {
        agent = GetComponent<NavMeshAgent>();
        currentState = startingState;
        idleTimer = idleDuration;
        idleCheckTimer = Random.Range(1f, 3f);

        if (patrolCenter == Vector3.zero)
            patrolCenter = transform.position;
    }

    void Update()
    {
        if (player == null) return;

        float distanceToPlayer = Vector3.Distance(transform.position, player.position);

        
        if (distanceToPlayer <= chaseRange)
        {
            currentState = EnemyState.Chase;
        }
        else if (currentState == EnemyState.Chase && distanceToPlayer > chaseRange)
        {
            currentState = EnemyState.Patrol;
        }
        switch (currentState)
        {
            case EnemyState.Idle:
                Idle();
                break;
            case EnemyState.Patrol:
                PatrolMode();
                break;
            case EnemyState.Chase:
                HuntMode();
                break;
        }
    }

    void Idle()
    {
        idleTimer -= Time.deltaTime;
        if (idleTimer <= 0f)
        {
            idleTimer = idleDuration;
            currentState = EnemyState.Patrol;
            MoveToRandomPoint();
        }
    }

    void PatrolMode()
    {

        idleCheckTimer -= Time.deltaTime;
        if (idleCheckTimer <= 0f)
        {
            if (Random.value < idleChanceWhilePatrolling)
            {
                currentState = EnemyState.Idle;
                idleTimer = idleDuration;
            }
            else
            {

                if (!agent.pathPending && agent.remainingDistance < 0.5f)
                {
                    MoveToRandomPoint();
                }
            }

            idleCheckTimer = Random.Range(1f, 3f);
        }
    }

    void HuntMode()
    {
        if (player != null)
        {
            agent.destination = player.position;
        }
    }
    // random patrol point
    void MoveToRandomPoint()
    {
        Vector3 randomDirection = Random.insideUnitSphere * patrolRadius;
        randomDirection += patrolCenter;

        NavMeshHit hit;
        if (NavMesh.SamplePosition(randomDirection, out hit, patrolRadius, NavMesh.AllAreas))
        {
            agent.SetDestination(hit.position);
        }
    }

    void OnDrawGizmosSelected()
    {
        //papakita patrol radius ng kalaban
        Gizmos.color = Color.yellow;
        Gizmos.DrawWireSphere(patrolCenter == Vector3.zero ? transform.position : patrolCenter, patrolRadius);
    }
}
