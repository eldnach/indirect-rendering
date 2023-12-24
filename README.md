# Indirect Rendering and GPU Culling 

This sample implementes a procedural (indirect) Scriptable Render Pass, which is instantiated and enequeued within the Univesral Render Pipeline:

<p align="center">
  <img width="100%" src="https://github.com/eldnach/indirect-rendering/blob/main/.github/images/draw-indirect.gif?raw=true" alt="DrawIndirect">
</p>

Indirect Drawing allows to procedurally generate geometry and drawing parameter data using compute shaders. This technique can be used in order to massively-parallelize the transformation, culling, and rendering of complex geometries.

In this example, a compute shader is used in order to procedurally generate and transform a large amount of vertices and mesh instances. A final instance-based culling dispatch is executed once per frame, in order to procedurally render any visible instances:

<p align="center">
  <img width="100%" src="https://github.com/eldnach/indirect-rendering/blob/main/.github/images/gpu-culling.gif?raw=true" alt="GPUCullling">
</p>

## Renderer Feature

You can enable and configure the procedural rendering pass by adding a "Procedural Rendering Feature" to the active UniversalRenderer.asset:
<p align="center">
  <img width="100%" src="https://github.com/eldnach/indirect-rendering/blob/main/.github/images/renderer-feature.png?raw=true" alt="RendererFeature">
</p>

The renderer feature's settings can be used to load an input mesh, configure the per-instance mesh count, and set the total instance count. Optional heightmap, wind deformation and camera-based repulsion can be enabled.
