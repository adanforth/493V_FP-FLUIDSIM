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
    [SerializeField] private GameObject airCellPrefab;
    [SerializeField] private GameObject solidCellPrefab;
    [SerializeField] private GameObject fluidCellPrefab;
    private List<GameObject> gridCells = new List<GameObject>();
    [SerializeField] bool showGrid;




    // Mesh Data for rendering particles uniqely - add colors etc later
    private struct Mesh_Data
    {
        public Matrix4x4 mat;
    };

    // GLOBAL PARAMETERS
    //private static float _dt = 1.0f / 160.0f;
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
    // CELL TYPES
    private static float FLUID_CELL = 0;
    private static float AIR_CELL = 1;
    private static float SOLID_CELL = 2;
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
    private int _reset_cell_types;
    private int _reset_cell_velocities_and_weights;
    private int _mark_fluid_cells;
    private int _particle_to_grid;
    private int _avg_cell_velocities;
    private int _reset_particle_densities;
    private int _update_particle_densities;
    private int _solve_Incompressibility;
    private int _grid_to_particle;



    // Particle Buffers
    private ComputeBuffer _particlePositionsBuffer; // particle positions
    private ComputeBuffer _particleVelocitiesBuffer; // particle velocities
    private ComputeBuffer _meshPropertiesBuffer; // particle properties (matrix, color, etc)
    private ComputeBuffer _cellTypeBuffer; // cell type, Fluid = 0, Air = 1, Solid = 2;
    private ComputeBuffer _cellIsSolidBuffer; // Fixed buffer - sets boundary cells to solid
    private ComputeBuffer _cellVelocityBuffer;
    private ComputeBuffer _prevCellVelocityBuffer;
    private ComputeBuffer _cellWeightBuffer;
    private ComputeBuffer _particleDensityBuffer;



    // Start is called before the first frame update
    void Start()
    {
        // Move camera
        cam.transform.position = new Vector3(_simWidth / 2, _simHeight / 2, - _simWidth / 2);
           

        // Grid stuff
        // Note, this is the number of actual cells, we will be appending 1 to each dimension for our staggered MAC grid
        float numGridCells = _simWidth * _simHeight * _gridCellDensity;
        //_gridResY = math.ceil(math.pow(numGridCells / 1.0f, 1.0f/_dim));
        //_gridResX = _gridResY * 2.0f;
        _gridResY = math.floor(_simHeight * _gridCellDensity);
        _gridResX = math.floor(_simWidth * _gridCellDensity);

        // Particle stuff
        _r = 0.3f * (_simWidth / _gridResX);
        _particleScale = new Vector3(2 * _r, 2 * _r, 2 * _r);


        // Setting up for "Dam Break" init
        float relativeWaterHeight = 0.8f;
        float relativeWaterWidth = 0.6f;
        float dx = 2.0f * _r;
        float dy = math.sqrt(3.0f) / 2.0f * dx;

        // these are the dimenion versions of h in the 10 min phys vid.
        float _x = _simWidth / _gridResX;
        float _y = _simHeight / _gridResY;

        Debug.Log(_gridResY);
        Debug.Log(_gridResX);
        Debug.Log(_x);
        Debug.Log(_y);

        int numX = (int) math.floor((relativeWaterWidth * _simWidth - 2.0f * _x - 2.0f * _r) / dx);
        int numY = (int) math.floor((relativeWaterHeight * _simHeight - 2.0f * _y - 2.0f * _r) / dy);
        _numParticles = numX * numY;

        //Debug.Log(_numParticles);

        _numCells = ((int)_gridResX + 1) * ((int)_gridResY + 1);


        // instantiate init arrays
        _initParticlePositions = new float3[_numParticles];
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
        _reset_cell_types = _compute.FindKernel("reset_cell_types");
        _reset_cell_velocities_and_weights = _compute.FindKernel("reset_cell_velocities_and_weights");
        _mark_fluid_cells = _compute.FindKernel("mark_fluid_cells");
        _particle_to_grid = _compute.FindKernel("particle_to_grid");
        _avg_cell_velocities = _compute.FindKernel("avg_cell_velocities");
        _reset_particle_densities = _compute.FindKernel("reset_particle_densities");
        _update_particle_densities = _compute.FindKernel("update_particle_densities");
        _solve_Incompressibility = _compute.FindKernel("solve_Incompressibility");
        _grid_to_particle = _compute.FindKernel("grid_to_particle");

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

        _cellVelocityBuffer?.Release();
        _cellVelocityBuffer = null;

        _cellWeightBuffer?.Release();
        _cellWeightBuffer = null;

        _prevCellVelocityBuffer?.Release();
        _prevCellVelocityBuffer = null;

        _particleDensityBuffer?.Release();
        _particleDensityBuffer = null;
}   

    private void setSolidCells()
    {
        for (int i = 0; i < _gridResX + 1; i++)
        {
            for (int j = 0; j < _gridResY + 1; j++)
            {
                float s = 1.0f; // fluid
                if (i == 0 || i == _gridResX || j == 0 || j == _gridResY)
                {
                    s = 0.0f;
                }
                _initSolidCells[i * ((int)_gridResY + 1) + j] = s;
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

        _cellWeightBuffer = new ComputeBuffer(_numCells, 12);

        _cellVelocityBuffer = new ComputeBuffer(_numCells, 12);

        _particleDensityBuffer = new ComputeBuffer(_numCells, 4);

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

        // Set for reset_cell_types
        _compute.SetBuffer(_reset_cell_types, "cellTypes", _cellTypeBuffer);
        _compute.SetBuffer(_reset_cell_types, "cellIsSolid", _cellIsSolidBuffer);

        // Set for reset_cell_velocities_and_weights
        _compute.SetBuffer(_reset_cell_velocities_and_weights, "cellVelocities", _cellVelocityBuffer);
        _compute.SetBuffer(_reset_cell_velocities_and_weights, "cellWeights", _cellWeightBuffer);

        // Set for mark_fluid_cells
        _compute.SetBuffer(_mark_fluid_cells, "cellTypes", _cellTypeBuffer);
        _compute.SetBuffer(_mark_fluid_cells, "particlePositions", _particlePositionsBuffer);

        // Set for particle_to_grid
        _compute.SetBuffer(_particle_to_grid, "particlePositions", _particlePositionsBuffer);
        _compute.SetBuffer(_particle_to_grid, "particleVelocities", _particleVelocitiesBuffer);
        _compute.SetBuffer(_particle_to_grid, "cellVelocities", _cellVelocityBuffer);
        _compute.SetBuffer(_particle_to_grid, "cellWeights", _cellWeightBuffer);

        // Set for avg_cell_velocities
        _compute.SetBuffer(_avg_cell_velocities, "cellWeights", _cellWeightBuffer);
        _compute.SetBuffer(_avg_cell_velocities, "cellVelocities", _cellVelocityBuffer);

        // Set for reset_particle_densities
        _compute.SetBuffer(_reset_particle_densities, "particleDensity", _particleDensityBuffer);

        // Set for update_particle_densities
        _compute.SetBuffer(_update_particle_densities, "particleDensity", _particleDensityBuffer);
        _compute.SetBuffer(_update_particle_densities, "particlePositions", _particlePositionsBuffer);

        // Set for solve_Incompressibility
        _compute.SetBuffer(_solve_Incompressibility, "particlePositions", _particlePositionsBuffer);
        _compute.SetBuffer(_solve_Incompressibility, "cellIsSolid", _cellIsSolidBuffer);
        _compute.SetBuffer(_solve_Incompressibility, "particleVelocities", _particleVelocitiesBuffer);
        _compute.SetBuffer(_solve_Incompressibility, "particleDensity", _particleDensityBuffer);
        _compute.SetBuffer(_solve_Incompressibility, "cellVelocities", _cellVelocityBuffer);
        _compute.SetBuffer(_solve_Incompressibility, "cellTypes", _cellTypeBuffer);

        // Set for grid_to_particle
        _compute.SetBuffer(_grid_to_particle, "particlePositions", _particlePositionsBuffer);
        _compute.SetBuffer(_grid_to_particle, "particleVelocities", _particleVelocitiesBuffer);
        _compute.SetBuffer(_grid_to_particle, "cellTypes", _cellTypeBuffer);
        _compute.SetBuffer(_grid_to_particle, "cellVelocities", _cellVelocityBuffer);

        // Set floats
        _compute.SetFloat("_gravity", _gravity);
        //_compute.SetFloat("_timeStep", _dt);
        _compute.SetFloat("_r", _r);
        _compute.SetFloat("_numSubSteps", _numSubSteps);
        _compute.SetFloat("_width", _simWidth);
        _compute.SetFloat("_height", _simHeight);
        _compute.SetFloat("_resX", _gridResX);
        _compute.SetFloat("_resY", _gridResY);
        _compute.SetFloat("_minX", _simWidth / _gridResX + _r);
        _compute.SetFloat("_maxX", _gridResX * (_simWidth / _gridResX) - 2 *_r);
        _compute.SetFloat("_minY", _simHeight / _gridResY + _r);
        _compute.SetFloat("_maxY", _gridResY * (_simHeight / _gridResY) - 2*_r);
        _compute.SetFloat("FLUID_CELL", FLUID_CELL);
        _compute.SetFloat("AIR_CELL", AIR_CELL);
        _compute.SetFloat("SOLID_CELL", SOLID_CELL);
        _compute.SetFloat("_overrelaxation", 1.9f);
        _compute.SetFloat("_timeStep", 1.0f / 10.0f);



        // Verts
        _args[0] = _instanceMesh.GetIndexCount(0);
        _args[1] = (uint)_numParticles;
        _args[2] = _instanceMesh.GetIndexStart(0);
        _args[3] = _instanceMesh.GetBaseVertex(0);

        _argsBuffer.SetData(_args);
    }

    private static int xd = 0;

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
        _compute.Dispatch(_reset_cell_types, Mathf.CeilToInt(_numCells / 64f), 1, 1);
        _compute.Dispatch(_reset_cell_velocities_and_weights, Mathf.CeilToInt(_numCells / 64f), 1, 1);
        _compute.Dispatch(_mark_fluid_cells, Mathf.CeilToInt(_numParticles / 64f), 1, 1);
        _compute.Dispatch(_particle_to_grid, Mathf.CeilToInt(_numParticles / 64f), 1, 1);
        _compute.Dispatch(_avg_cell_velocities, Mathf.CeilToInt(_numCells / 64f), 1, 1);
        //_compute.Dispatch(_reset_particle_densities, Mathf.CeilToInt(_numCells / 64f), 1, 1);
        //_compute.Dispatch(_update_particle_densities, Mathf.CeilToInt(_numParticles / 64f), 1, 1);
        for (int i = 0; i < 1; i++)
        {
            _compute.Dispatch(_solve_Incompressibility, Mathf.CeilToInt(_numParticles / 64f), 1, 1);
        }
        _compute.Dispatch(_grid_to_particle, Mathf.CeilToInt(_numParticles / 64f), 1, 1);

        //if (xd == 0)
        //{
        //    int3[] vels = new int3[_numCells];

        //    _cellVelocityBuffer.GetData(vels);

        //    for (int i = 0; i < _numCells; i++)
        //    {
        //        if (vels[i].x != 0 || vels[i].y != 0 || vels[i].z != 0)
        //        {
        //            Debug.Log(i);
        //            Debug.Log((float)vels[i].x);
        //            Debug.Log((float)vels[i].y);
        //            Debug.Log((float)vels[i].z);
        //        }
        //    }
        //}

        if (showGrid)
        {
            for (int i = 0; i < gridCells.Count; i++)
            {
                DestroyImmediate(gridCells[i]);
            }
            gridCells.Clear();

            float[] cellTypes = new float[_numCells];
            
            _cellTypeBuffer.GetData(cellTypes);

            for (int i = 0; i <= _gridResX; i++)
            {
                for (int j = 0; j <= _gridResY; j++)
                {

                    if (cellTypes[i * ((int)_gridResY + 1) + j] == AIR_CELL)
                    {
                        //gridCells.Add(Instantiate(airCellPrefab, new Vector3(_simWidth / (_gridResX + 1) * (i + .5f), _simHeight / (_gridResY + 1) * (j + .5f), 0), Quaternion.identity));
                    }
                    else if (cellTypes[i * ((int)_gridResY + 1) + j] == SOLID_CELL)
                    {
                        gridCells.Add(Instantiate(solidCellPrefab, new Vector3(_simWidth / (_gridResX + 1) * (i + .5f), _simHeight / (_gridResY + 1) * (j + .5f), 0), Quaternion.identity));
                    }
                    if (cellTypes[i * ((int)_gridResY + 1) + j] == FLUID_CELL)
                    {
                        gridCells.Add(Instantiate(fluidCellPrefab, new Vector3(_simWidth / (_gridResX + 1) * (i + .5f), _simHeight / (_gridResY + 1) * (j + .5f), 0), Quaternion.identity));
                    }

                }
            }
        }

        Graphics.DrawMeshInstancedIndirect(_instanceMesh, 0, _instanceMaterial, new Bounds(Vector3.zero, Vector3.one * 1000), _argsBuffer);

    }
}
