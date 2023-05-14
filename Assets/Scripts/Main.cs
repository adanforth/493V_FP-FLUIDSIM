using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using Unity.Mathematics;
using Random = UnityEngine.Random;

public class Main : MonoBehaviour
{

    // FOR RENDERING
    [SerializeField] private Mesh _instanceMesh;
    [SerializeField] private Material _instanceMaterial;

    private struct Mesh_Data
    {
        public Matrix4x4 mat;
    };

    private RenderParams _rp;
    private Matrix4x4[] _matrices; // Particle matricies

    // compute stuff
    [SerializeField] private ComputeShader _compute;
    private readonly uint[] _args = { 0, 0, 0, 0, 0 };
    private ComputeBuffer _argsBuffer;

    // Compute Shaders
    private int _render_particles;
    private int _integrate_particles;
    // Data Buffers
    private ComputeBuffer _particlePositionsBuffer;
    private ComputeBuffer _particleVelocitiesBuffer;
    private ComputeBuffer _meshPropertiesBuffer;


    // SIM PARAMETERS
    public static float _simWidth = 80;
    public static float _simHeight = 60;
    public static float _dt = 1.0f / 160.0f;
    public static float _h = 1.0f;
    public static float _r = 0.3f * _h;
    public static float _gravity = -9.81f;
    public static Vector3 ParticleScale = new(_r, _r, _r);
    public static int _numSubSteps = 1;

    // PARTICLE INFORMATION
    public int _numParticles;
    // Instantiation for particles
    private float3[] _initParticlePositions;
    private float3[] _initParticleVelocities;
    private Mesh_Data[] _initParticleMatricies;

    // Start is called before the first frame update
    void Start()
    {
        _integrate_particles = _compute.FindKernel("integrate_particles");
        _render_particles = _compute.FindKernel("render_particles");

        _initParticlePositions = new float3[_numParticles];
        _initParticleVelocities = new float3[_numParticles];
        _initParticleMatricies = new Mesh_Data[_numParticles];

        for (int i = 0; i < _numParticles; i++)
        {
            _initParticlePositions[i].x = Random.Range(0, _simWidth);
            _initParticlePositions[i].y = Random.Range(0, _simHeight);
        }

        _argsBuffer = new ComputeBuffer(1, _args.Length * sizeof(uint), ComputeBufferType.IndirectArguments);
        UpdateBuffers();
    }

    //private void integrateParticles(float dt, float gravity)
    //{
    //    for (var i = 0; i < _numParticles; i++)
    //    {
    //        _particleVelocities[i].y += dt * gravity;
    //        _particlePositions[i].x += _particleVelocities[i].x * dt;
    //        _particlePositions[i].y += _particleVelocities[i].y * dt;
    //    }
    //}

    private void OnDisable()
    {
        _argsBuffer?.Release();
        _argsBuffer = null;

        _particlePositionsBuffer?.Release();
        _particlePositionsBuffer = null;

        _particleVelocitiesBuffer?.Release();
        _particleVelocitiesBuffer = null;

        _meshPropertiesBuffer?.Release();
        _meshPropertiesBuffer = null;
}   

    private void UpdateBuffers()
    {
        for (var i = 0; i < _numParticles; i++)
        {
            var pos = _initParticlePositions[i];
            var rot = Quaternion.identity;

            _initParticleMatricies[i] = new Mesh_Data { mat = Matrix4x4.TRS(pos, rot, ParticleScale)};
        }

        _particlePositionsBuffer = new ComputeBuffer(_numParticles, 12);
        _particlePositionsBuffer.SetData(_initParticlePositions);
        _particleVelocitiesBuffer = new ComputeBuffer(_numParticles, 12);
        _meshPropertiesBuffer = new ComputeBuffer(_numParticles, 64);
        _meshPropertiesBuffer.SetData(_initParticleMatricies);

        // Set buffer for mesh properties to be shared by compute shader and instance renderer.
        _compute.SetBuffer(_render_particles, "meshProperties", _meshPropertiesBuffer) ;
        _compute.SetBuffer(_render_particles, "particlePositions", _particlePositionsBuffer);
        _instanceMaterial.SetBuffer("data", _meshPropertiesBuffer);
        
        // Set particle pos and vel for integrate
        _compute.SetBuffer(_integrate_particles, "particlePositions", _particlePositionsBuffer);
        _compute.SetBuffer(_integrate_particles, "particleVelocities", _particleVelocitiesBuffer);


        // Set floats
        _compute.SetFloat("_gravity", _gravity);
        _compute.SetFloat("_timeStep", _dt);
        _compute.SetFloat("_numSubSteps", _numSubSteps);


        // Verts
        _args[0] = _instanceMesh.GetIndexCount(0);
        _args[1] = (uint)_numParticles;
        _args[2] = _instanceMesh.GetIndexStart(0);
        _args[3] = _instanceMesh.GetBaseVertex(0);

        _argsBuffer.SetData(_args);
    }


    // Update is called once per frame
    void Update()
    {
        // Update rendering stuff
        _compute.Dispatch(_render_particles, Mathf.CeilToInt(_numParticles / 64f), 1, 1);
        // Integrate particles
        _compute.Dispatch(_integrate_particles, Mathf.CeilToInt(_numParticles / 64f), 1, 1);
        Graphics.DrawMeshInstancedIndirect(_instanceMesh, 0, _instanceMaterial, new Bounds(Vector3.zero, Vector3.one * 1000), _argsBuffer);

        //Graphics.RenderMeshInstanced(_rp, _mesh, 0, _matrices);
    }
}
