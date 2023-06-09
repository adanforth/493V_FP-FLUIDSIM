using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using Unity.Mathematics;
using Random = UnityEngine.Random;
using UnityEngine.XR;
using UnityEngine.SceneManagement;

public class Simulator_VR : MonoBehaviour
{

    // FOR RENDERING
    [SerializeField] private Mesh _instanceMesh;
    [SerializeField] private Material _instanceMaterial;
    public Camera cam;
    [SerializeField] private GameObject solidCellPrefab;
    [SerializeField] private GameObject fluidCellPrefab;
    [SerializeField] private GameObject bottomPlane;
    private List<GameObject> gridCells = new List<GameObject>();
    [SerializeField] bool showGrid;
    [SerializeField] int _solveIterations = 1;
    // How large to render - not scaled radius
    [SerializeField] float _partcileRadius = 1;
    [SerializeField] float _timeScale = 1;
    [SerializeField] float _restDensityMult = 1;

    [SerializeField] Color _baseParticleColor;
    [SerializeField] Color _fastParticleColor;





    // Mesh Data for rendering particles uniqely - add colors etc later
    private struct Mesh_Data
    {
        public Matrix4x4 mat;
        public Color color;

        public Mesh_Data(Matrix4x4 _mat, Color _color)
        {
            mat = _mat;
            color = _color;
        }
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
    public float _simDepth = 20;

    private float _h;
    private float _fInvSpacing;

    [Range(0, 1)]
    public float _flipRatio = 0.9f;
    [Range(0, .9f)]
    public float relativeWaterHeight = 0.8f;
    [Range(0, .9f)]
    public float relativeWaterWidth = 0.4f;
    //    [Range(0, .9f)]
    //public float relativeWaterDepth = 0.4f;

    //// Dimensions of the grid coords - aka there are _gridResX = (fNumX -1) cells in the x direction, and _gridResY cells in the y directrion (in 2D at least)
    //private float _gridResX;
    //private float _gridResY;
    private int _fNumX;
    private int _fNumY;
    private int _fNumZ;
    // CELL TYPES
    private static int FLUID_CELL = 0;
    private static int AIR_CELL = 1;
    private static int SOLID_CELL = 2;
    // amount of grid cells in the MAC grid
    private int _fNumCells;


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
    private int2[] _initCellDensityAndNumFluid;
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
    private int _copy_prev_velocities;
    private int _particle_to_grid;
    private int _avg_cell_velocities;
    private int _restore_solid_cells;
    private int _reset_particle_densities;
    private int _update_particle_densities;
    private int _reset_projection_updates;
    private int _solve_Incompressibility;
    private int _add_projection_to_velocities;
    private int _grid_to_particle;
    private int _convert_velocity_and_weight_to_float;
    private int _convert_velocity_to_int;
    private int _convert_velocity_to_float;
    private int _calc_particle_rest_density_pt1;
    private int _calc_particle_rest_density_pt2;


    // VR STUFF
    private List<InputDevice> devices = new List<InputDevice>();
    private InputDevice _rightHand;
    [SerializeField] GameObject _rightHandGameObject;
    private Vector3 _curRightHandPos;
    private Vector3 _prevRightHandPos;
    private Vector3 _rightHandVelocity;



    // Particle Buffers
    private ComputeBuffer _particlePositionsBuffer; // particle positions
    private ComputeBuffer _particleVelocitiesBuffer; // particle velocities
    private ComputeBuffer _meshPropertiesBuffer; // particle properties (matrix, color, etc)
    private ComputeBuffer _cellTypeBuffer; // cell type, Fluid = 0, Air = 1, Solid = 2;
    private ComputeBuffer _cellIsSolidBuffer; // Fixed buffer - sets boundary cells to solid
    private ComputeBuffer _cellVelocityBuffer;
    private ComputeBuffer _cellVelocityBufferInt;
    private ComputeBuffer _cellProjectionUpdatesBuffer;
    private ComputeBuffer _cellWeightBuffer;
    private ComputeBuffer _cellWeightBufferInt;
    private ComputeBuffer _prevCellVelocityBuffer;
    private ComputeBuffer _particleDensityBuffer;
    private ComputeBuffer _densitySumAndNumFluidBuffer;



    // Start is called before the first frame update
    void Start()
    {
        // Move camera
        cam = Camera.main;
        cam.transform.position = new Vector3(_simWidth * 1.15f, 1.3f * _simHeight, -_simDepth * .65f);
        cam.transform.LookAt(gameObject.transform.position + new Vector3(_simWidth/2, _simHeight/5, _simDepth / 2));


        _curRightHandPos = Vector3.zero;
        _prevRightHandPos = Vector3.zero;
        _rightHandVelocity = Vector3.zero;
        // Grid stuff
        //_gridResY = math.floor(_simHeight * _gridCellDensity);
        //_gridResX = math.floor(_simWidth * _gridCellDensity);

        int res = (int)math.floor(_simHeight * _gridCellDensity);

        float h = _simHeight / res;


        // Particle stuff
        _r = 0.3f * h;
        _particleScale = new Vector3(_partcileRadius * _r, _partcileRadius * _r, _partcileRadius * _r);


        // Setting up for "Dam Break" init
        float dx = 2.0f * _r;
        float dy = 2* _r;
        float dz = math.sqrt(3) * _r;


        int numX = (int)math.floor((relativeWaterWidth * _simWidth - 2.0f * h - 2.0f * _r) / dx);
        int numY = (int)math.floor((relativeWaterHeight * _simHeight - 2.0f * h - 2.0f * _r) / dy);
        int numZ = (int)math.floor((_simDepth - 2 * h) / dz);
        _numParticles = numX * numY * numZ;
        Debug.Log(_numParticles);

        //Debug.Log(_numParticles);
        _fNumX = (int)math.floor(_simWidth / h) + 1;
        _fNumY = (int)math.floor(_simHeight / h) + 1;
        _fNumZ = (int)math.floor(_simDepth / h) + 1;


        _fNumCells = _fNumX * _fNumY * _fNumZ;

        _h = math.max(math.max(_simWidth / _fNumX, _simHeight / _fNumY), _simDepth / _fNumZ);
        _fInvSpacing = 1.0f / _h;

        // Init bottom plane
        bottomPlane.transform.position = new Vector3((_simWidth + _h) / 2, _r, (_simDepth + _h)/2);
        bottomPlane.transform.localScale = new Vector3(_simWidth / 10, 0, _simDepth / 10);


        // instantiate init arrays
        _initParticlePositions = new float3[_numParticles];
        _initParticleMatricies = new Mesh_Data[_numParticles];
        _initSolidCells = new float[_fNumCells];
        _initCellTypes = new float[_fNumCells];


        int pInd = 0;
        for (int i = 0; i < numX; i++)
        {
            for (int j = 0; j < numY; j++)
            {
                for (int k = 0; k < numZ; k++)
                {
                    _initParticlePositions[pInd].x = _h + _r + dx * i + (j % 2 == 0 ? 0.0f : _r) + (k % 2 == 0 ? 0.0f : _r);
                    _initParticlePositions[pInd].y = (_h + _r + dy * j);
                    _initParticlePositions[pInd].z = _h + _r + dz * k;
                    pInd++;
                }
            }
        }

        for (var i = 0; i < _numParticles; i++)
        {
            var pos = _initParticlePositions[i];
            var rot = Quaternion.identity;

            _initParticleMatricies[i] = new Mesh_Data(Matrix4x4.TRS(pos, rot, _particleScale), _baseParticleColor);
        }

        setSolidCells();


        // Grab the compute shaders
        _integrate_particles = _compute.FindKernel("integrate_particles");
        _render_particles = _compute.FindKernel("render_particles");
        _enforce_boundaries = _compute.FindKernel("enforce_boundaries");
        _reset_cell_types = _compute.FindKernel("reset_cell_types");
        _reset_cell_velocities_and_weights = _compute.FindKernel("reset_cell_velocities_and_weights");
        _mark_fluid_cells = _compute.FindKernel("mark_fluid_cells");
        _copy_prev_velocities = _compute.FindKernel("copy_prev_velocities");
        _particle_to_grid = _compute.FindKernel("particle_to_grid");
        _convert_velocity_and_weight_to_float = _compute.FindKernel("convert_velocity_and_weight_to_float");
        _avg_cell_velocities = _compute.FindKernel("avg_cell_velocities");
        _restore_solid_cells = _compute.FindKernel("restore_solid_cells");
        _reset_particle_densities = _compute.FindKernel("reset_particle_densities");
        _update_particle_densities = _compute.FindKernel("update_particle_densities");
        _calc_particle_rest_density_pt1 = _compute.FindKernel("calc_particle_rest_density_pt1");
        _calc_particle_rest_density_pt2 = _compute.FindKernel("calc_particle_rest_density_pt2");
        _reset_projection_updates = _compute.FindKernel("reset_projection_updates");
        _convert_velocity_to_int = _compute.FindKernel("convert_velocity_to_int");
        _solve_Incompressibility = _compute.FindKernel("solve_Incompressibility");
        _convert_velocity_to_float = _compute.FindKernel("convert_velocity_to_float");
        _add_projection_to_velocities = _compute.FindKernel("add_projection_to_velocities");
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

        _cellWeightBufferInt?.Release();
        _cellWeightBufferInt = null;

        _cellVelocityBufferInt?.Release();
        _cellVelocityBufferInt = null;

        _cellProjectionUpdatesBuffer?.Release();
        _cellProjectionUpdatesBuffer = null;

        _densitySumAndNumFluidBuffer?.Release();
        _densitySumAndNumFluidBuffer = null;
    }

    private void setSolidCells()
    {
        int n = _fNumY;


        for (int i = 0; i < _fNumX; i++)
        {
            for (int j = 0; j < _fNumY; j++)
            {
                for (int k = 0; k < _fNumZ; k++)
                {
                    float s = 1.0f; // fluid
                    if (i == 0 || i == _fNumX - 0 || j == 0 || j == _fNumY - 0 || k == 0 || k == _fNumZ - 0)
                    {
                        s = 0.0f;
                    }
                    _initSolidCells[i * (_fNumY * _fNumZ) + j * _fNumZ + k] = s;
                }
            }
        }
    }

    private void UpdateBuffers()
    {
        _argsBuffer = new ComputeBuffer(1, _args.Length * sizeof(uint), ComputeBufferType.IndirectArguments);

        _particlePositionsBuffer = new ComputeBuffer(_numParticles, 12);
        _particlePositionsBuffer.SetData(_initParticlePositions);

        _particleVelocitiesBuffer = new ComputeBuffer(_numParticles, 12);

        _meshPropertiesBuffer = new ComputeBuffer(_numParticles, 64 + 16);
        _meshPropertiesBuffer.SetData(_initParticleMatricies);

        _cellIsSolidBuffer = new ComputeBuffer(_fNumCells, 4);
        _cellIsSolidBuffer.SetData(_initSolidCells);

        _cellTypeBuffer = new ComputeBuffer(_fNumCells, 4);

        _cellWeightBuffer = new ComputeBuffer(_fNumCells, 12);

        _cellVelocityBuffer = new ComputeBuffer(_fNumCells, 12);

        _prevCellVelocityBuffer = new ComputeBuffer(_fNumCells, 12);

        _cellWeightBufferInt = new ComputeBuffer(_fNumCells, 12);

        _cellVelocityBufferInt = new ComputeBuffer(_fNumCells, 12);

        _particleDensityBuffer = new ComputeBuffer(_fNumCells, 4);

        _cellProjectionUpdatesBuffer = new ComputeBuffer(_fNumCells, 12);

        _densitySumAndNumFluidBuffer = new ComputeBuffer(1, 8);
        _densitySumAndNumFluidBuffer.SetData(new uint2[1]);

        // Set buffer for mesh properties to be shared by compute shader and instance renderer.
        _compute.SetBuffer(_render_particles, "meshProperties", _meshPropertiesBuffer);
        _compute.SetBuffer(_render_particles, "particlePositions", _particlePositionsBuffer);
        _compute.SetBuffer(_render_particles, "particleDensity", _particleDensityBuffer);
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
        _compute.SetBuffer(_reset_cell_velocities_and_weights, "cellVelocitiesInt", _cellVelocityBufferInt);
        _compute.SetBuffer(_reset_cell_velocities_and_weights, "cellWeightsInt", _cellWeightBufferInt);

        // Set for mark_fluid_cells
        _compute.SetBuffer(_mark_fluid_cells, "cellTypes", _cellTypeBuffer);
        _compute.SetBuffer(_mark_fluid_cells, "particlePositions", _particlePositionsBuffer);

        // Set for copy_prev_velocities
        _compute.SetBuffer(_copy_prev_velocities, "cellVelocities", _cellVelocityBuffer);
        _compute.SetBuffer(_copy_prev_velocities, "prevCellVelocities", _prevCellVelocityBuffer);

        // Set for particle_to_grid
        _compute.SetBuffer(_particle_to_grid, "particlePositions", _particlePositionsBuffer);
        _compute.SetBuffer(_particle_to_grid, "particleVelocities", _particleVelocitiesBuffer);
        _compute.SetBuffer(_particle_to_grid, "cellVelocitiesInt", _cellVelocityBufferInt);
        _compute.SetBuffer(_particle_to_grid, "cellWeightsInt", _cellWeightBufferInt);

        // Set for convert_velocity_and_weight_to_float
        _compute.SetBuffer(_convert_velocity_and_weight_to_float, "cellVelocities", _cellVelocityBuffer);
        _compute.SetBuffer(_convert_velocity_and_weight_to_float, "cellWeights", _cellWeightBuffer);
        _compute.SetBuffer(_convert_velocity_and_weight_to_float, "cellVelocitiesInt", _cellVelocityBufferInt);
        _compute.SetBuffer(_convert_velocity_and_weight_to_float, "cellWeightsInt", _cellWeightBufferInt);


        // Set for avg_cell_velocities
        _compute.SetBuffer(_avg_cell_velocities, "cellWeights", _cellWeightBuffer);
        _compute.SetBuffer(_avg_cell_velocities, "cellVelocities", _cellVelocityBuffer);

        // Set for restore_solid_cells
        _compute.SetBuffer(_restore_solid_cells, "prevCellVelocities", _prevCellVelocityBuffer);
        _compute.SetBuffer(_restore_solid_cells, "cellVelocities", _cellVelocityBuffer);
        _compute.SetBuffer(_restore_solid_cells, "cellTypes", _cellTypeBuffer);

        // Set for reset_particle_densities
        _compute.SetBuffer(_reset_particle_densities, "particleDensity", _particleDensityBuffer);

        // Set for update_particle_densities
        _compute.SetBuffer(_update_particle_densities, "particleDensity", _particleDensityBuffer);
        _compute.SetBuffer(_update_particle_densities, "particlePositions", _particlePositionsBuffer);

        // Set for calc_particle_rest_density_pt1 and 2
        _compute.SetBuffer(_calc_particle_rest_density_pt1, "particleDensity", _particleDensityBuffer);
        _compute.SetBuffer(_calc_particle_rest_density_pt1, "densitySumAndNumFluid", _densitySumAndNumFluidBuffer);
        _compute.SetBuffer(_calc_particle_rest_density_pt1, "cellTypes", _cellTypeBuffer);

        _compute.SetBuffer(_calc_particle_rest_density_pt2, "densitySumAndNumFluid", _densitySumAndNumFluidBuffer);

        // Set for reset_projection_updates
        _compute.SetBuffer(_reset_projection_updates, "projectionUpdates", _cellProjectionUpdatesBuffer);

        // Set for add_projection_to_velocities
        _compute.SetBuffer(_add_projection_to_velocities, "projectionUpdates", _cellProjectionUpdatesBuffer);
        _compute.SetBuffer(_add_projection_to_velocities, "cellVelocities", _cellVelocityBuffer);

        // Set for convert_velocity_to_int
        _compute.SetBuffer(_convert_velocity_to_int, "cellVelocities", _cellVelocityBuffer);
        _compute.SetBuffer(_convert_velocity_to_int, "cellVelocitiesInt", _cellVelocityBufferInt);

        // Set for convert_velocity_to_float
        _compute.SetBuffer(_convert_velocity_to_float, "cellVelocities", _cellVelocityBuffer);
        _compute.SetBuffer(_convert_velocity_to_float, "cellVelocitiesInt", _cellVelocityBufferInt);

        // Set for solve_Incompressibility
        _compute.SetBuffer(_solve_Incompressibility, "cellTypes", _cellTypeBuffer);
        _compute.SetBuffer(_solve_Incompressibility, "cellIsSolid", _cellIsSolidBuffer);
        _compute.SetBuffer(_solve_Incompressibility, "cellVelocities", _cellVelocityBuffer);
        _compute.SetBuffer(_solve_Incompressibility, "cellVelocitiesInt", _cellVelocityBufferInt);
        _compute.SetBuffer(_solve_Incompressibility, "particleDensity", _particleDensityBuffer);
        _compute.SetBuffer(_solve_Incompressibility, "projectionUpdates", _cellProjectionUpdatesBuffer);

        // Set for grid_to_particle
        _compute.SetBuffer(_grid_to_particle, "particlePositions", _particlePositionsBuffer);
        _compute.SetBuffer(_grid_to_particle, "particleVelocities", _particleVelocitiesBuffer);
        _compute.SetBuffer(_grid_to_particle, "cellTypes", _cellTypeBuffer);
        _compute.SetBuffer(_grid_to_particle, "cellVelocities", _cellVelocityBuffer);
        _compute.SetBuffer(_grid_to_particle, "prevCellVelocities", _prevCellVelocityBuffer);

        // Set floats
        _compute.SetFloat("_gravity", _gravity);
        //_compute.SetFloat("_timeStep", _dt);
        _compute.SetFloat("_r", _r);
        _compute.SetFloat("_numSubSteps", _numSubSteps);
        _compute.SetFloat("_width", _simWidth);
        _compute.SetFloat("_height", _simHeight);
        _compute.SetFloat("_h", _h);
        _compute.SetFloat("_fInvSpacing", _fInvSpacing);
        _compute.SetInt("_fNumX", _fNumX);
        _compute.SetInt("_fNumY", _fNumY);
        _compute.SetInt("_fNumZ", _fNumZ);
        _compute.SetFloat("_numIterations", _solveIterations);
        //_compute.SetFloat("_resX", _gridResX);
        //_compute.SetFloat("_resY", _gridResY);
        _compute.SetFloat("_minX", _h + _r);
        _compute.SetFloat("_maxX", (_fNumX) * _h - _r);
        _compute.SetFloat("_minY", _h + _r);
        _compute.SetFloat("_maxY", (_fNumY) * _h - _r);
        _compute.SetFloat("_minZ", _h + _r);
        _compute.SetFloat("_maxZ", (_fNumZ) * _h - _r);
        _compute.SetInt("FLUID_CELL", FLUID_CELL);
        _compute.SetInt("AIR_CELL", AIR_CELL);
        _compute.SetInt("SOLID_CELL", SOLID_CELL);
        _compute.SetFloat("_overrelaxation", 1.95f);
        //_compute.SetFloat("_timeStep", 1.0f / 120.0f);
        _compute.SetFloat("_flipRatio", _flipRatio);
        _compute.SetFloat("_particleRestDensity", 0);



        // Verts
        _args[0] = _instanceMesh.GetIndexCount(0);
        _args[1] = 2 * (uint)_numParticles;
        _args[2] = _instanceMesh.GetIndexStart(0);
        _args[3] = _instanceMesh.GetBaseVertex(0);

        _argsBuffer.SetData(_args);
    }

    private static int xd = 0;
    private GameObject xdd;

    // Update is called once per frame
    void Update()
    {

        InputDeviceCharacteristics rightControllerChars = InputDeviceCharacteristics.Right | InputDeviceCharacteristics.Controller;

        InputDevices.GetDevicesWithCharacteristics(rightControllerChars, devices);

        //foreach (var item in devices)
        //{
        //    Debug.Log(item.name + item.characteristics);

        //}

        if (devices.Count > 0)
        {
            _rightHand = devices[0];
            Debug.Log("XD");
        }

        _rightHand.TryGetFeatureValue(CommonUsages.primaryButton, out bool primaryButton);
        if (primaryButton)
        {
            if (_curRightHandPos == Vector3.zero)
            {
                _curRightHandPos = _rightHandGameObject.transform.position;
                _prevRightHandPos = _curRightHandPos;
                _rightHandVelocity = _curRightHandPos - _prevRightHandPos;
            } else
            {
                _prevRightHandPos = _curRightHandPos;
                _curRightHandPos = _rightHandGameObject.transform.position;
                _rightHandVelocity = _curRightHandPos - _prevRightHandPos;
            }

            float h2 = 0.5f * _h;

            // given some point...
            float x_p = _curRightHandPos.x;
            float y_p = _curRightHandPos.y;
            float z_p = _curRightHandPos.z;

            x_p = math.clamp(x_p, _h, (_fNumX - 1) * _h);
            y_p = math.clamp(y_p, _h, (_fNumY - 1) * _h);
            z_p = math.clamp(z_p, _h, (_fNumZ - 1) * _h);

            int x0 = (int)math.floor((x_p - h2) * _fInvSpacing);
            int y0 = (int)math.floor((y_p - h2) * _fInvSpacing);
            int z0 = (int)math.floor((z_p - h2) * _fInvSpacing);

            _compute.SetBool("_rightHandPressed", true);
            _compute.SetInt("_rightHandCellX", x0);
            _compute.SetInt("_rightHandCellY", y0);
            _compute.SetInt("_rightHandCellZ", z0);
            _compute.SetFloat("_rightHandVelocityX", _rightHandVelocity.x);
            _compute.SetFloat("_rightHandVelocityY", _rightHandVelocity.y);
            _compute.SetFloat("_rightHandVelocityZ", _rightHandVelocity.z);


        } else
        {
            _compute.SetBool("_rightHandPressed", false);

            _curRightHandPos = Vector3.zero;
            _prevRightHandPos = _curRightHandPos;
            _rightHandVelocity = Vector3.zero;
        }

        _rightHand.TryGetFeatureValue(CommonUsages.trigger, out float triggerVal);
        if (triggerVal > .01f)
        {
            _compute.SetFloat("_timeStep", _timeScale * Time.deltaTime);



            // Update rendering stuff
            _compute.Dispatch(_render_particles, Mathf.CeilToInt(_numParticles / 64f), 1, 1);
            // Integrate particles
            _compute.Dispatch(_integrate_particles, Mathf.CeilToInt(_numParticles / 64f), 1, 1);
            // Enforce Boundaries
            _compute.Dispatch(_enforce_boundaries, Mathf.CeilToInt(_numParticles / 64f), 1, 1);
            // Transfer to Grid
            _compute.Dispatch(_reset_cell_types, Mathf.CeilToInt(_fNumCells / 64f), 1, 1);
            _compute.Dispatch(_reset_cell_velocities_and_weights, Mathf.CeilToInt(_fNumCells / 64f), 1, 1);
            _compute.Dispatch(_copy_prev_velocities, Mathf.CeilToInt(_fNumCells / 64f), 1, 1);
            _compute.Dispatch(_mark_fluid_cells, Mathf.CeilToInt(_numParticles / 64f), 1, 1);
            _compute.Dispatch(_particle_to_grid, Mathf.CeilToInt(_numParticles / 64f), 1, 1);
            _compute.Dispatch(_convert_velocity_and_weight_to_float, Mathf.CeilToInt(_fNumCells / 64f), 1, 1);
            _compute.Dispatch(_avg_cell_velocities, Mathf.CeilToInt(_fNumCells / 64f), 1, 1);
            _compute.Dispatch(_restore_solid_cells, Mathf.CeilToInt(_fNumCells / 64f), 1, 1);
            // Update Density
            _compute.Dispatch(_reset_particle_densities, Mathf.CeilToInt(_fNumCells / 64f), 1, 1);
            _compute.Dispatch(_update_particle_densities, Mathf.CeilToInt(_numParticles / 64f), 1, 1);
            if (xd == 0)
            {
                _compute.Dispatch(_calc_particle_rest_density_pt1, Mathf.CeilToInt(_fNumCells / 64f), 1, 1);
                uint2[] densitySumAndNumFluid = new uint2[1];

                _densitySumAndNumFluidBuffer.GetData(densitySumAndNumFluid);
                _compute.SetFloat("_particleRestDensity", _restDensityMult * ((float)densitySumAndNumFluid[0].x / 100000.0f) / densitySumAndNumFluid[0].y);
                Debug.Log(_restDensityMult * ((float)densitySumAndNumFluid[0].x / 100000.0f) / densitySumAndNumFluid[0].y);
                xd++;
            }
            //// Solve for incompressibility
            _compute.Dispatch(_copy_prev_velocities, Mathf.CeilToInt(_fNumCells / 64f), 1, 1);
            for (int iter = 0; iter < _solveIterations; iter++)
            {
                _compute.Dispatch(_reset_projection_updates, Mathf.CeilToInt(_fNumCells / 64f), 1, 1);
                _compute.Dispatch(_solve_Incompressibility, Mathf.CeilToInt(_fNumCells / 64f), 1, 1);
                _compute.Dispatch(_add_projection_to_velocities, Mathf.CeilToInt(_fNumCells / 64f), 1, 1);
            }


            _compute.Dispatch(_grid_to_particle, Mathf.CeilToInt(_numParticles / 64f), 1, 1);

            //int[] den = new int[_fNumCells];

            //_particleDensityBuffer.GetData(den);
            //for (int i = _fNumX / 4; i < 3 * _fNumX / 4; i++)
            //{
            //    Debug.Log((float)den[i * _fNumY] / 100000.0f);
            //}

            //for (int i = 0; i < 1; i++)
            //{
            //    _compute.Dispatch(_solve_Incompressibility, Mathf.CeilToInt(_numParticles / 64f), 1, 1);
            //}

            //if (xd == 0)
            //{
            //    float[] cellTypes = new float[_fNumCells];

            //    _cellTypeBuffer.GetData(cellTypes);

            //    Debug.Log(cellTypes[128]);
            //    xd++;
            //}

            if (showGrid)
            {
                for (int i = 0; i < gridCells.Count; i++)
                {
                    DestroyImmediate(gridCells[i]);
                }
                gridCells.Clear();

                float[] cellTypes = new float[_fNumCells];

                _cellTypeBuffer.GetData(cellTypes);

                for (int i = 0; i < _fNumX; i++)
                {
                    for (int j = 0; j < _fNumY; j++)
                    {
                        for (int k = 0; k < _fNumZ; k++)
                        {
                            //if (cellTypes[i * _fNumY + j] == AIR_CELL)
                            //{
                            //    //gridCells.Add(Instantiate(airCellPrefab, new Vector3(_simWidth / (_gridResX + 1) * (i + .5f), _simHeight / (_gridResY + 1) * (j + .5f), 0), Quaternion.identity));
                            //}
                            if (cellTypes[i * _fNumY * _fNumZ + j * _fNumZ + k] == SOLID_CELL)
                            {
                                gridCells.Add(Instantiate(solidCellPrefab, new Vector3(_h * (i + .5f), _h * (j + .5f), _h * (k + .5f)), Quaternion.identity));
                            }
                            if (cellTypes[i * _fNumY * _fNumZ + j * _fNumZ + k] == FLUID_CELL)
                            {
                                gridCells.Add(Instantiate(fluidCellPrefab, new Vector3(_h * (i + .5f), _h * (j + .5f), _h * (k + .5f)), Quaternion.identity));
                            }
                        }

                    }
                }
            }

        }
        Graphics.DrawMeshInstancedIndirect(_instanceMesh, 0, _instanceMaterial, new Bounds(Vector3.zero, Vector3.one * 1000), _argsBuffer);
    }
}
