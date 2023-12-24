using System;
using System.Collections.Generic;
using Unity.Collections;
using Unity.Mathematics;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using static QuadstripUtil;

public class ProceduralRenderingFeature : ScriptableRendererFeature
{
    [SerializeField] private ProceduralDrawSettings settings;
    [SerializeField] private Material material;
    private ProceduralPass proceduralRenderPass;

    public override void Create() {
        if (material == null)
        {
            return;
        }
        proceduralRenderPass = new ProceduralPass(material, settings);
        proceduralRenderPass.renderPassEvent = RenderPassEvent.AfterRenderingSkybox;
    }

    public override void AddRenderPasses(ScriptableRenderer renderer, ref RenderingData renderData) {
        if ((renderData.cameraData.cameraType == CameraType.Game) || (renderData.cameraData.cameraType == CameraType.SceneView))
        {
            renderer.EnqueuePass(proceduralRenderPass);
        }
    }

    protected override void Dispose(bool disposing)
    {
        proceduralRenderPass.DisposeResources();
        #if UNITY_EDITOR
            if (EditorApplication.isPlaying)
            {
                //Destroy(material);
            }
            else
            {
                DestroyImmediate(material);
            }
        #else
                Destroy(material);
        #endif
    }
}

[Serializable]
public class ProceduralDrawSettings
{
    [SerializeField] public Vector3 VolumeBounds;  
    [SerializeField] public Mesh mesh;
    [SerializeField] public uint2 meshesPerInstance;
    [SerializeField] public uint2 instanceCount;
    [SerializeField] public Vector4 seed;
    [SerializeField] public bool enableHeightmap;
    [SerializeField] public Texture2D heightmap;
    [SerializeField] public Vector4 heightmapIntensity;
    [SerializeField] public bool enableRepulsion;
    [SerializeField] public float repulsionRadius;
    [SerializeField] public bool enableWind;
    [SerializeField] public Vector4 windDirection;
}

class ProceduralPass : ScriptableRenderPass
{
    private ProceduralDrawSettings settings;
    private Material material;
    private Vector4 instanceScale;
    private Vector3 repulsorOrigin;
    private Vector3 repulsorScale;
    private float minimumModelHeight;
    private Matrix4x4 viewProjMatrix;

    private int modelSizeUID;
    private int modelIndicesSizeUID;
    private int meshCountXUID;
    private int meshCountYUID;
    private int meshCountZUID;
    private int2 spawnerScaleUID;
    private int timeUID;
    private int windUID;
    private int seedUID;
    private int repulsorOriginUID;
    private int repulsorScaleUID;
    private int heightUID;
    private int2 instanceCountUID;
    private int instanceScaleUID;

    private ComputeBuffer modelPosBuffer;
    private int modelPosBufferUID;
    private ComputeBuffer modelUVBuffer;
    private int modelUVBufferUID;
    private ComputeBuffer modelIndexBuffer;
    private int modelIndexBufferUID;
    private ComputeBuffer emitterBuffer;
    private int emitterBufferUID;

    private ComputeBuffer curveBuffer;
    private int curveBufferUID;
    private ComputeBuffer vertexBuffer;
    private int vertexBufferUID;
    private ComputeBuffer uvBuffer;
    private int uvBufferUID;
    private GraphicsBuffer indexBuffer;
    private int indexBufferUID;
    private ComputeBuffer indirectBuffer;
    private int indirectBufferUID;
    private ComputeBuffer positionsBuffer;
    private int positionsBufferUID;
    private ComputeBuffer culledPositionsBuffer;
    private int culledPositionsBufferUID;
    private int heightmapUID;
    private int viewProjMatrixUID;

    private ComputeShader computeShader;
    private int[] kernelIDs;

    private QuadstripUtil.Quadstrip quadstrip;
    private QuadstripUtil.shape2D shape2d;

    private int workgroupCountX;
    private int workgroupCountY;
    private int workgroupCountZ;

    private int workgroupCountX_1;
    private int workgroupCountY_1;
    private int workgroupCountZ_1;

    struct vert
    {
        public Vector3 position;
        public Vector2 uv;
    }
    private vert[] modelData;
    private uint modelSize;
    private int[] modelIndicesData;
    private uint modelIndicesSize;

    private Bounds bounds;
    private Vector3[] emitterData;

    private Matrix4x4 trnsfrm;

    private LocalKeyword WIND;
    private LocalKeyword HEIGHT;
    private LocalKeyword REPULSE;
    private LocalKeyword CUSTOMMESH;
    private LocalKeyword ALPHATEST;

    private bool updateVertexBuffer;

    public ProceduralPass(Material material, ProceduralDrawSettings settings)
    {
        this.material = material;
        this.settings = settings;
        Initialize();
    }

    public void Initialize()
    {
        computeShader = (ComputeShader)Resources.Load("ComputeParticleDeformation");

        settings.meshesPerInstance[0] = Math.Max(1, settings.meshesPerInstance[0]);
        settings.meshesPerInstance[1] = Math.Max(1, settings.meshesPerInstance[1]);
        uint meshCount = (uint)(settings.meshesPerInstance[0] * settings.meshesPerInstance[1]);

        instanceScale = new Vector4(settings.VolumeBounds.x / (float)settings.instanceCount[0], settings.VolumeBounds.y, settings.VolumeBounds.z / (float)settings.instanceCount[1], 1.0f);

        Vector3 origin = new Vector3(0, 0, 0);
        Vector3 scale = new Vector3(instanceScale.x, instanceScale.y, instanceScale.z);
        Vector3 margin = new Vector3(scale.x / settings.meshesPerInstance[0], 1.0f, scale.z / settings.meshesPerInstance[1]);

        emitterData = new Vector3[3]{
            origin,
            scale,
            margin
        };

        bounds = new Bounds(new Vector3(0.0f, 0.0f, 0.0f), new Vector3(1000.0f, 1000.0f, 1000.0f));

        Vector3[] quadBezier = new[] { new Vector3(0.0f, 0.0f, 0.0f), new Vector3(0.0f, 4.0f * .5f, 0.0f), new Vector3(0.0f, 4.0f, 0.0f) };
        shape2d = QuadstripUtil.defineShape2D(0.0f, new Vector3(0.0f, 0.0f, -1.0f));
        QuadstripUtil.curvePoint[] curvePoints = QuadstripUtil.sampleBezier(quadBezier, 10);

        if (!settings.mesh)
        {
            quadstrip = QuadstripUtil.loft(shape2d, curvePoints);
            modelSize = (uint)quadstrip.positions.Length;
            modelData = new vert[modelSize];
            for (int i = 0; i < modelSize; i++)
            {
                modelData[i].position = quadstrip.positions[i];
                modelData[i].uv = quadstrip.uvs[i];
            }

            modelIndicesSize = (uint)quadstrip.indices.Length;
            modelIndicesData = new int[modelIndicesSize];
            for (int i = 0; i < modelIndicesSize; i++)
            {
                modelIndicesData[i] = quadstrip.indices[i];
            }
        }
        else
        {
            using (var dataArray = Mesh.AcquireReadOnlyMeshData(settings.mesh))
            {
                var data = dataArray[0];
                var positions = new NativeArray<Vector3>(settings.mesh.vertexCount, Allocator.TempJob);
                var uvs = new NativeArray<Vector2>(settings.mesh.vertexCount, Allocator.TempJob);
                var indices = new NativeArray<int>((int)settings.mesh.GetIndexCount(0), Allocator.TempJob);
                data.GetVertices(positions);
                data.GetUVs(0, uvs);
                data.GetIndices(indices, 0);

                modelSize = (uint)settings.mesh.vertexCount;
                modelData = new vert[modelSize];
                for (int i = 0; i < modelSize; i++)
                {
                    modelData[i].position = positions[i];
                    modelData[i].uv = uvs[i];
                }

                modelIndicesSize = (uint)indices.Length;
                modelIndicesData = new int[modelIndicesSize];
                for (int i = 0; i < modelIndicesSize; i++)
                {
                    modelIndicesData[i] = indices[i];
                }
                positions.Dispose();
                uvs.Dispose();
                indices.Dispose();
            }
        }

        Vector3[] modelVertData = new Vector3[modelSize];
        for (int i = 0; i < modelSize; i++)
        {
            minimumModelHeight = Mathf.Max(modelData[i].position.y, settings.VolumeBounds.y);
            modelVertData[i] = modelData[i].position;
        };
        Vector2[] modelUVData = new Vector2[modelSize];
        for (int i = 0; i < modelSize; i++)
        {
            modelUVData[i] = modelData[i].uv;
        };
        int[] modelIndexData = new int[modelIndicesSize];
        for (int i = 0; i < modelIndicesSize; i++)
        {
            modelIndexData[i] = modelIndicesData[i];
        };

        List<Vector3> vertexData = new List<Vector3>();
        List<Vector2> uvData = new List<Vector2>();
        List<int> indexData = new List<int>();
        List<Vector3> curveData = new List<Vector3>();
        for (int i = 0; i < meshCount; i++)
        {
            for (int j = 0; j < modelSize; j++)
            {
                vertexData.Add(modelData[j].position);
                uvData.Add(modelData[j].uv);
            }

            for (int j = 0; j < modelIndicesSize; j++)
            {
                indexData.Add(i * (int)modelSize + modelIndicesData[j]);
            }

            curveData.Add(new Vector3(0.0f, 0.0f, 0.0f));
            curveData.Add(new Vector3(0.0f, 0.5f, 0.0f));
            curveData.Add(new Vector3(0.0f, 1.0f, 0.0f));
        }

        uint vertexCount = modelSize * meshCount;
        uint indexCount = modelIndicesSize * meshCount;
        uint cpCount = 3 * meshCount;

        // Create GPU buffers
        emitterBuffer = new ComputeBuffer(3, sizeof(float) * 3, ComputeBufferType.Default);
        emitterBuffer.name = "particle emitter buffer";
        modelPosBuffer = new ComputeBuffer((int)modelSize, sizeof(float) * 3, ComputeBufferType.Default);
        modelPosBuffer.name = "particle model buffer";
        modelUVBuffer = new ComputeBuffer((int)modelSize, sizeof(float) * 2, ComputeBufferType.Default);
        modelUVBuffer.name = "particle uv buffer";
        modelIndexBuffer = new ComputeBuffer((int)modelIndicesSize, sizeof(int), ComputeBufferType.Default);
        modelIndexBuffer.name = "particle index buffer";
        curveBuffer = new ComputeBuffer((int)cpCount, sizeof(float) * 3, ComputeBufferType.Default);
        curveBuffer.name = "particle curve buffer";
        vertexBuffer = new ComputeBuffer((int)vertexCount, sizeof(float) * 3, ComputeBufferType.Default);
        vertexBuffer.name = "vertex buffer";
        uvBuffer = new ComputeBuffer((int)vertexCount, sizeof(float) * 2, ComputeBufferType.Default);
        uvBuffer.name = "uv buffer";
        indexBuffer = new GraphicsBuffer(GraphicsBuffer.Target.Raw | GraphicsBuffer.Target.Index, (int)indexCount, sizeof(int));
        indexBuffer.name = "indices buffer";
        indirectBuffer = new ComputeBuffer(5, sizeof(uint), ComputeBufferType.IndirectArguments | ComputeBufferType.Structured);
        indirectBuffer.name = "indirect buffer";
        positionsBuffer = new ComputeBuffer((int)(settings.instanceCount[0] * settings.instanceCount[1]), sizeof(float) * 4, ComputeBufferType.Default);
        positionsBuffer.name = "particle positions buffer";
        culledPositionsBuffer = new ComputeBuffer((int)(settings.instanceCount[0] * settings.instanceCount[1]), sizeof(float) * 4, ComputeBufferType.Append | ComputeBufferType.Structured);
        culledPositionsBuffer.name = "culled particle positions buffer";

        Vector4[] instancePositions = new Vector4[settings.instanceCount[0] * settings.instanceCount[1]];
        Vector4[] culledPositions = new Vector4[settings.instanceCount[0] * settings.instanceCount[1]];
        float w = settings.instanceCount[0] * instanceScale.x;
        float startX = -w * 0.5f + instanceScale.x * 0.5f;
        float h = settings.instanceCount[1] * instanceScale.z;
        float startY = -h * 0.5f + instanceScale.z * 0.5f;

        for (int i = 0; i < settings.instanceCount[0]; i++)
        {
            for (int j = 0; j < settings.instanceCount[1]; j++)
            {
                instancePositions[i * settings.instanceCount[1] + j] = new Vector4(startX + instanceScale.x * i, 0.0f, startY + instanceScale.z * j, 1.0f);
                culledPositions[i * settings.instanceCount[1] + j] = new Vector4(0.0f, 0.0f, 0.0f, 0.0f);
            }
        }
        positionsBuffer.SetData(instancePositions);
        culledPositionsBuffer.SetData(culledPositions);

        emitterBuffer.SetData(emitterData);
        modelPosBuffer.SetData(modelVertData);
        modelUVBuffer.SetData(modelUVData);
        modelIndexBuffer.SetData(modelIndexData);

        curveBuffer.SetData(curveData);
        vertexBuffer.SetData(vertexData);
        uvBuffer.SetData(uvData);
        indexBuffer.SetData(indexData);
        uint[] args = { modelIndicesSize * meshCount, (uint)(settings.instanceCount[0] * settings.instanceCount[1]), 0, 0, 0 };
        indirectBuffer.SetData(args);

        kernelIDs = new int[3];
        kernelIDs[0] = computeShader.FindKernel("ComputeCurves");
        kernelIDs[1] = computeShader.FindKernel("ComputeGeometry");
        kernelIDs[2] = computeShader.FindKernel("ComputeCulling");
        uint workgroupSizeX;
        uint workgroupSizeY;
        uint workgroupSizeZ;
        computeShader.GetKernelThreadGroupSizes(kernelIDs[0], out workgroupSizeX, out workgroupSizeY, out workgroupSizeZ);
        workgroupCountX = (int)(Mathf.Ceil(settings.meshesPerInstance[0] / workgroupSizeX));
        workgroupCountY = (int)(Mathf.Ceil(settings.meshesPerInstance[1] / workgroupSizeY));
        workgroupCountZ = (int)(Mathf.Ceil(1 / workgroupSizeZ));

        uint workgroupSizeX_1;
        uint workgroupSizeY_1;
        uint workgroupSizeZ_1;
        computeShader.GetKernelThreadGroupSizes(kernelIDs[2], out workgroupSizeX_1, out workgroupSizeY_1, out workgroupSizeZ_1);
        workgroupCountX_1 = (int)(Mathf.Ceil(settings.instanceCount[0] / workgroupSizeX_1));
        workgroupCountY_1 = (int)(Mathf.Ceil(settings.instanceCount[1] / workgroupSizeY_1));
        workgroupCountZ_1 = (int)(Mathf.Ceil(1 / workgroupSizeZ_1));


        // retrieve shader resource UIDs for buffers
        emitterBufferUID = Shader.PropertyToID("emitterBuffer");
        modelPosBufferUID = Shader.PropertyToID("modelPosBuffer");
        modelUVBufferUID = Shader.PropertyToID("modelUVBuffer");
        modelIndexBufferUID = Shader.PropertyToID("modelIndexBuffer");

        curveBufferUID = Shader.PropertyToID("curveBuffer");
        vertexBufferUID = Shader.PropertyToID("vertexBuffer");
        uvBufferUID = Shader.PropertyToID("uvBuffer");
        indexBufferUID = Shader.PropertyToID("indexBuffer");
        positionsBufferUID = Shader.PropertyToID("positionsBuffer");
        culledPositionsBufferUID = Shader.PropertyToID("culledPositionsBuffer");
        heightmapUID = Shader.PropertyToID("heightmap");

        // restrieve shader resource UIDs for constants
        indirectBufferUID = Shader.PropertyToID("indirectBuffer");
        meshCountXUID = Shader.PropertyToID("particleCountX");
        meshCountYUID = Shader.PropertyToID("particleCountY");
        meshCountZUID = Shader.PropertyToID("particleCountZ");
        modelSizeUID = Shader.PropertyToID("modelSize");
        modelIndicesSizeUID = Shader.PropertyToID("modelIndicesSize");

        timeUID = Shader.PropertyToID("time");
        windUID = Shader.PropertyToID("wind");
        seedUID = Shader.PropertyToID("seed");
        repulsorOriginUID = Shader.PropertyToID("repulsorOrigin");
        repulsorScaleUID = Shader.PropertyToID("repulsorScale");
        heightUID = Shader.PropertyToID("height");
        spawnerScaleUID = new int2();
        spawnerScaleUID[0] = Shader.PropertyToID("modelHeight");
        spawnerScaleUID[1] = Shader.PropertyToID("worldScale");
        viewProjMatrixUID = Shader.PropertyToID("viewProjMat");

        instanceCountUID = new int2();
        instanceCountUID[0] = Shader.PropertyToID("spawnersCountX");
        instanceCountUID[1] = Shader.PropertyToID("spawnersCountY");
        instanceScaleUID = Shader.PropertyToID("spawnerScale");

        trnsfrm = new Matrix4x4();

        WIND = new LocalKeyword(material.shader, "WIND");
        HEIGHT = new LocalKeyword(material.shader, "HEIGHTMAP");
        REPULSE = new LocalKeyword(material.shader, "REPULSE");
        CUSTOMMESH = new LocalKeyword(material.shader, "CUSTOMMESH");
        ALPHATEST = new LocalKeyword(material.shader, "ALPHATEST");
        
        UpdateVertexBuffer();
    }

    private void UpdateVertexBuffer()
    {

        // Bind and update shader constants
        computeShader.SetInt(meshCountXUID, (int)settings.meshesPerInstance[0]);
        computeShader.SetInt(meshCountYUID, (int)settings.meshesPerInstance[1]);
        computeShader.SetInt(meshCountZUID, 1);
        computeShader.SetInt(modelSizeUID, (int)modelSize);
        computeShader.SetInt(modelIndicesSizeUID, (int)modelIndicesSize);
        computeShader.SetVector(seedUID, settings.seed);
        computeShader.SetFloat(spawnerScaleUID[0], minimumModelHeight);

        // ---------------- read -----------------------------------
        computeShader.SetBuffer(kernelIDs[0], emitterBufferUID, emitterBuffer);
        // ---------------- write -----------------------------------
        computeShader.SetBuffer(kernelIDs[0], curveBufferUID, curveBuffer);
        // Dispatch (compute curves)
        computeShader.Dispatch(kernelIDs[0], workgroupCountX, workgroupCountY, workgroupCountZ);


        // ---------------- read -----------------------------------
        computeShader.SetBuffer(kernelIDs[1], curveBufferUID, curveBuffer);
        computeShader.SetBuffer(kernelIDs[1], modelPosBufferUID, modelPosBuffer);
        computeShader.SetBuffer(kernelIDs[1], modelUVBufferUID, modelUVBuffer);
        computeShader.SetBuffer(kernelIDs[1], modelIndexBufferUID, modelIndexBuffer);
        // ---------------- write -----------------------------------
        computeShader.SetBuffer(kernelIDs[1], vertexBufferUID, vertexBuffer);
        computeShader.SetBuffer(kernelIDs[1], uvBufferUID, uvBuffer);
        computeShader.SetBuffer(kernelIDs[1], indexBufferUID, indexBuffer);
        // Dispatch (compute geometry)
        computeShader.Dispatch(kernelIDs[1], workgroupCountX, workgroupCountY, workgroupCountZ);

    }

    public override void Execute(ScriptableRenderContext context, ref RenderingData renderData)
    {
        CommandBuffer cmdbuffer = CommandBufferPool.Get("ProceduralDraw");

        culledPositionsBuffer.SetCounterValue(0);
        // Set uniforms
        computeShader.SetInt(instanceCountUID[0], (int)settings.instanceCount[0]);
        //viewProjMatrix = renderData.cameraData.camera.projectionMatrix * renderData.cameraData.camera.worldToCameraMatrix ;
        viewProjMatrix = Camera.main.projectionMatrix * Camera.main.worldToCameraMatrix;
        computeShader.SetMatrix(viewProjMatrixUID, viewProjMatrix);
        computeShader.SetVector(instanceScaleUID, instanceScale);
        computeShader.SetFloat(spawnerScaleUID[0],  minimumModelHeight);
        // Set buffers
        // ---------------- read -----------------------------------
        computeShader.SetBuffer(kernelIDs[2], positionsBufferUID, positionsBuffer);
        // ---------------- write -----------------------------------
        computeShader.SetBuffer(kernelIDs[2], culledPositionsBufferUID, culledPositionsBuffer);

        // Dispatch (compute instance cullig)
        cmdbuffer.DispatchCompute(computeShader, kernelIDs[2], workgroupCountX_1, workgroupCountY_1, workgroupCountZ_1);
        cmdbuffer.CopyCounterValue(culledPositionsBuffer, indirectBuffer, 4);


        int[] args = new int[5];
        indirectBuffer.GetData(args);
        Debug.Log(args[1]);
        

        bounds = new Bounds(new Vector3(0.0f, 0.0f, 0.0f), new Vector3(1000.0f, 1000.0f, 1000.0f));
        if (settings.enableRepulsion)
        {
            repulsorOrigin = Camera.main.transform.position;
            repulsorScale = new Vector3(settings.repulsionRadius, settings.repulsionRadius, settings.repulsionRadius);
        }

        // Set buffers 
        material.SetBuffer(vertexBufferUID, vertexBuffer);
        material.SetBuffer(uvBufferUID, uvBuffer);
        material.SetBuffer(culledPositionsBufferUID, culledPositionsBuffer);

        // Set uniforms
        material.SetFloat(spawnerScaleUID[0], minimumModelHeight);
        material.SetFloat(spawnerScaleUID[1], settings.VolumeBounds.x);
        material.SetFloat(timeUID, Time.time);
        material.SetVector(windUID, settings.windDirection);
        material.SetVector(repulsorOriginUID, repulsorOrigin);
        material.SetVector(repulsorScaleUID, repulsorScale);
        material.SetVector(heightUID, settings.heightmapIntensity);
        material.SetTexture(heightmapUID, settings.heightmap);

        // Set keywords
        if (settings.enableWind) { material.EnableKeyword(WIND); } else { material.DisableKeyword(WIND); };
        if (settings.enableHeightmap) { material.EnableKeyword(HEIGHT); } else { material.DisableKeyword(HEIGHT); };
        if (settings.enableRepulsion ) { material.EnableKeyword(REPULSE); } else { material.DisableKeyword(REPULSE); };
        if (settings.mesh) { material.EnableKeyword(CUSTOMMESH); } else { material.DisableKeyword(CUSTOMMESH); };    

        cmdbuffer.DrawProceduralIndirect(indexBuffer, trnsfrm, material, 0 , MeshTopology.Triangles, indirectBuffer);
        context.ExecuteCommandBuffer(cmdbuffer);
        context.Submit();
    }
    public void DisposeResources()
    {
        modelPosBuffer.Dispose();
        modelUVBuffer.Dispose();
        modelIndexBuffer.Dispose();
        emitterBuffer.Dispose();
        curveBuffer.Dispose();
        vertexBuffer.Dispose();
        uvBuffer.Dispose();
        indexBuffer.Dispose();
        indirectBuffer.Dispose();
        positionsBuffer.Dispose();
        culledPositionsBuffer.Dispose();
    }
}