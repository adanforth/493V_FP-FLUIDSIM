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
    public Camera cam;

    // Mesh Data for rendering particles uniqely - add colors etc later
    private struct Mesh_Data
    {
        public Matrix4x4 mat;
    };

    // GLOBAL PARAMETERS
    private static float _dt = 1.0f / 160.0f;
    private static float _gravity = -9.81f;
    private static float density = 1000f; // For divergence calcs
    private static int _dim = 2;

    // GRID PARAMS
    public float _gridCellDensity = 2f;
    // Dimensions of the grid in world coords - so it is _simWidth units long and _simHeight units tall
    public float _simWidth = 40;
    public float _simHeight = 20;
    // Dimensions of the grid coords - aka there are _gridResX = (fNumX -1) cells in the x direction, and _gridResY cells in the y directrion (in 2D at least)
    private float _gridResX;
    private float _gridResY;
    // amount of grid cells in the MAC grid
    private int _numCells;

    /* 
        Credit - David Li
        we use a staggered MAC grid
        this means the velocity grid width = grid width + 1 and velocity grid height = grid height + 1 and velocity grid depth = grid depth + 1
        a scalar for cell [i, j, k] is positionally located at [i + 0.5, j + 0.5, k + 0.5]
        x velocity for cell [i, j, k] is positionally located at [i, j + 0.5, k + 0.5]
        y velocity for cell [i, j, k] is positionally located at [i + 0.5, j, k + 0.5]
        z velocity for cell [i, j, k] is positionally located at [i + 0.5, j + 0.5, k]
    */

    // fluid/particle params
    private float _r;
    private Vector3 _particleScale;
    private static int _numSubSteps = 1;

    // PARTICLE INFORMATION
    private int _numParticles;
    // Instantiation arrays
    private float3[] _initParticlePositions;
    private float3[] _initParticleVelocities;
    private Mesh_Data[] _initParticleMatricies;
    private float[] _initCellTypes;
    private float[] _initSolidCells;



    // compute stuff
    [SerializeField] private ComputeShader _compute;
    private readonly uint[] _args = { 0, 0, 0, 0, 0 };
    private ComputeBuffer _argsBuffer;

    // Compute Shaders
    private int _render_particles;
    private int _integrate_particles;
    private int _enforce_boundaries;
    // Particle Buffers
    private ComputeBuffer _particlePositionsBuffer; // particle positions
    private ComputeBuffer _particleVelocitiesBuffer; // particle velocities
    private ComputeBuffer _meshPropertiesBuffer; // particle properties (matrix, color, etc)
    private ComputeBuffer _cellTypeBuffer; // cell type, Fluid = 0, Air = 1, Solid = 2;
    private ComputeBuffer _cellIsSolidBuffer; // Fixed buffer - sets boundary cells to solid

    // Grid Buffers
        // cell velocities
        // cell prev velocities
        // cell weights
        // cell density
        // cell pressure
        // cell cell markers

    // Start is called before the first frame update
    void Start()
    {
        // Move camera
        cam.transform.position = new Vector3(_simWidth / 2, _simHeight / 2, - _simWidth / 2);
           

        // Grid stuff
        // Note, this is the number of actual cells, we will be appending 1 to each dimension for our staggered MAC grid
        float numGridCells = _simWidth * _simHeight * _gridCellDensity;
        _gridResY = math.ceil(math.pow(numGridCells / 2.0f, 1.0f/_dim));
        _gridResX = _gridResY * 2.0f;

        // Particle stuff
        _r = 7.0f / _gridResX;
        _particleScale = new Vector3(2 * _r, 2 * _r, 2 * _r);


        // Setting up for "Dam Break" init
        float relativeWaterHeight = 0.8f;
        float relativeWaterWidth = 0.6f;
        float dx = 2.0f * _r;
        float dy = math.sqrt(3.0f) / 2.0f * dx;

        // these are the dimenion versions of h in the 10 min phys vid.
        float _x = _gridResX / _simWidth;
        float _y = _gridResY / _simHeight;

        int numX = (int) math.floor((relativeWaterWidth * _simWidth - 2.0f * _x - 2.0f * _r) / dx);
        int numY = (int) math.floor((relativeWaterHeight * _simHeight - 2.0f * _y - 2.0f * _r) / dy);
        _numParticles = numX * numY;

        _numCells = ((int)_gridResX + 1) * ((int)_gridResY + 1);


        // instantiate init arrays
        _initParticlePositions = new float3[_numParticles];
        _initParticleVelocities = new float3[_numParticles];
        _initParticleMatricies = new Mesh_Data[_numParticles];
        _initSolidCells = new float[_numCells];
        _initCellTypes = new float[_numCells];


        int pInd = 0;
        for (int i = 0; i < numX; i++)
        {
            for (int j = 0; j < numY; j++)
            {
                _initParticlePositions[pInd].x = _x + _r + dx * i + (j % 2 == 0 ? 0.0f : _r);
                _initParticlePositions[pInd].y = _y + _r + dy * j;
                pInd++;
            }
        }

        for (var i = 0; i < _numParticles; i++)
        {
            var pos = _initParticlePositions[i];
            var rot = Quaternion.identity;

            _initParticleMatricies[i] = new Mesh_Data { mat = Matrix4x4.TRS(pos, rot, _particleScale) };
        }

        setSolidCells();


        // Grab the compute shaders
        _integrate_particles = _compute.FindKernel("integrate_particles");
        _render_particles = _compute.FindKernel("render_particles");
        _enforce_boundaries = _compute.FindKernel("enforce_boundaries");

        // Create Buffers
        UpdateBuffers();
    }


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

        _cellTypeBuffer?.Release();
        _cellTypeBuffer = null;

        _cellIsSolidBuffer?.Release();
        _cellIsSolidBuffer = null;
}   

    private void setSolidCells()
    {
        for (int i = 0; i < _gridResX + 1; i++)
        {
            for (int j = 0; j < _gridResY + 1; j++)
            {
                float s = 1.0f; // fluid
                if (i == 0 || i == _gridResX || j == 0 )
                {
                    s = 0.0f;
                }
                _initSolidCells[i * (int)_gridResY + j] = s;
            }
        }
    }

    private void UpdateBuffers()
    {
        _argsBuffer = new ComputeBuffer(1, _args.Length * sizeof(uint), ComputeBufferType.IndirectArguments);

        _particlePositionsBuffer = new ComputeBuffer(_numParticles, 12);
        _particlePositionsBuffer.SetData(_initParticlePositions);
        _particleVelocitiesBuffer = new ComputeBuffer(_numParticles, 12);
        _meshPropertiesBuffer = new ComputeBuffer(_numParticles, 64);
        _meshPropertiesBuffer.SetData(_initParticleMatricies);

        _cellIsSolidBuffer = new ComputeBuffer (_numCells, 4);
        _cellIsSolidBuffer.SetData(_initSolidCells);

        _cellTypeBuffer = new ComputeBuffer(_numCells, 4);

        // Set buffer for mesh properties to be shared by compute shader and instance renderer.
        _compute.SetBuffer(_render_particles, "meshProperties", _meshPropertiesBuffer) ;
        _compute.SetBuffer(_render_particles, "particlePositions", _particlePositionsBuffer);
        _instanceMaterial.SetBuffer("data", _meshPropertiesBuffer);
        
        // Set particle pos and vel for integrate
        _compute.SetBuffer(_integrate_particles, "particlePositions", _particlePositionsBuffer);
        _compute.SetBuffer(_integrate_particles, "particleVelocities", _particleVelocitiesBuffer);

        // Set for enforce_boundaries
        _compute.SetBuffer(_enforce_boundaries, "particlePositions", _particlePositionsBuffer);
        _compute.SetBuffer(_enforce_boundaries, "particleVelocities", _particleVelocitiesBuffer);

        // Set floats
        _compute.SetFloat("_gravity", _gravity);
        _compute.SetFloat("_timeStep", _dt);
        _compute.SetFloat("_numSubSteps", _numSubSteps);
        _compute.SetFloat("_minX", _gridResX / _simWidth + _r);
        _compute.SetFloat("_maxX", _gridResX * (_gridResX / _simWidth) - _r);
        _compute.SetFloat("_minY", _gridResY / _simHeight + _r);
        _compute.SetFloat("_maxY", _gridResY * (_gridResY / _simHeight) - _r);


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
        Debug.DrawLine(Vector3.zero, new Vector3(0, _simHeight, 0), Color.gray);
        Debug.DrawLine(Vector3.zero, new Vector3(_simWidth, 0, 0), Color.gray);
        Debug.DrawLine(new Vector3(0, _simHeight, 0), new Vector3(_simWidth, _simHeight, 0), Color.gray);
        Debug.DrawLine(new Vector3(_simWidth, 0, 0), new Vector3(_simWidth, _simHeight, 0), Color.gray);
        // Update rendering stuff
        _compute.Dispatch(_render_particles, Mathf.CeilToInt(_numParticles / 64f), 1, 1);
        // Integrate particles
        _compute.Dispatch(_integrate_particles, Mathf.CeilToInt(_numParticles / 64f), 1, 1);
        _compute.Dispatch(_enforce_boundaries, Mathf.CeilToInt(_numParticles / 64f), 1, 1);
        Graphics.DrawMeshInstancedIndirect(_instanceMesh, 0, _instanceMaterial, new Bounds(Vector3.zero, Vector3.one * 1000), _argsBuffer);

    }
}
