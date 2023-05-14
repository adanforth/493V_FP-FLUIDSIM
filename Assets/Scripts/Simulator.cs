using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using Unity.Mathematics;
using Random = UnityEngine.Random;

public class Simulator : MonoBehaviour
{

    public float2[] particlePositions;
    public float2[] particleVelocities;

    private int _numParticles;

    // Start is called before the first frame update
    void Start()
    {
        
    }

    // Update is called once per frame
    void Update()
    {
        
    }

    public void initParticles(int numParticles)
    {
        _numParticles = numParticles;

        particlePositions = new float2[_numParticles];
        particleVelocities = new float2[_numParticles];

        for (int i = 0; i < _numParticles; i++)
        {
            particlePositions[i].x = 0;
        }

    }

    public float2[] simulate()
    {
        for (int i = 0; i < _numParticles; i++)
        {
            particlePositions[i].x += Random.Range(-1, 1);
            particlePositions[i].y += Random.Range(-1, 1);
        }

        return particlePositions;
    }
}
