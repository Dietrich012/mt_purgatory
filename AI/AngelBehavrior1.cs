using UnityEngine;
using UnityEngine.AI;
using UnityEngine.SceneManagement;
using System.Collections;
using System.Collections.Generic;
public class AngelBehavrior : MonoBehaviour
{
    public NavMeshAgent ai;
    public Transform player;
    Vector3 target;
    public Camera PlayerVis;
    public Camera JumpscareCam;
    public float AISpeed, attackDistance, JSDuration;
    public string MenuScene;

    private void Update()
    {
        Plane[] planes = GeometryUtility.CalculateFrustumPlanes(PlayerVis); ;
        float distance = Vector3.Distance(transform.position, player.position);
        if (GeometryUtility.TestPlanesAABB(planes, this.gameObject.GetComponent<Renderer>().bounds))
        {
            ai.speed = 0;
            ai.SetDestination(transform.position);
        }
        if (!GeometryUtility.TestPlanesAABB(planes, this.gameObject.GetComponent<Renderer>().bounds))
        {
            ai.speed = AISpeed;
            target = player.position;
            ai.destination = target;

            if (distance <= attackDistance)
            {
                player.gameObject.SetActive(false);
                JumpscareCam.gameObject.SetActive(true);
                StartCoroutine(GameOver());
            }
        }
    }
    IEnumerator GameOver()
    {
        yield return new WaitForSeconds(JSDuration);
        SceneManager.LoadScene(MenuScene);
    }
}