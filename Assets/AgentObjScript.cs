using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;

public class AgentObjScript : MonoBehaviour
{
    public NavMeshAgent Agent;
    public Transform Goal;

    public void Start()
    {
        Agent.destination = Goal.position;
    }

#if DEBUG
    public void Update()
    {
        var corners = Agent.path.corners;
        for (int i = 0; i < corners.Length - 1; i++)
        {
            Debug.DrawLine(corners[i], corners[i + 1], Color.red);
        }
    }
#endif
}
