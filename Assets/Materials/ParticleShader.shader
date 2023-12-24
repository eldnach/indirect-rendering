Shader "Unlit/ParticleShader"
{
    Properties
    {
        _Colormap("Texture2D display name", 2D) = "" {}
        _Color0("Example color", Color) = (.0, .0, .0, 1.0)
        _Color1("Example color", Color) = (1.0, 1.0, 1.0, 1.0)
    }
        SubShader
    {
        Tags { "RenderType" = "Transparents" "RenderPipeline" = "UniversalPipeline" }
        //Blend SrcAlpha OneMinusSrcAlpha
        //ZWrite Off
        LOD 100

        Cull Off

        Pass
        {
            HLSLPROGRAM

            #pragma vertex vert
            #pragma fragment frag
            #pragma target 4.5

            #pragma shader_feature_local WIND
            #pragma shader_feature_local HEIGHTMAP
            #pragma shader_feature_local REPULSE
            #pragma shader_feature_local CUSTOMMESH
            #pragma shader_feature_local ALPHATEST

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            struct vertdata
            {
                float4 vertex : POSITION;
                float2 uv : TEXCOORD0;
                uint vid : SV_VertexID;
                uint iid : SV_InstanceID;
            };

            struct v2f
            {
                float2 uv : TEXCOORD0;
                float4 vertex : SV_POSITION;
                uint iid : TEXCOORD1;
                float height : TEXCOORD2;
            };

            struct fragdata
            {
                float4 col : SV_Target;
            };

            TEXTURE2D(_Colormap);
            SAMPLER(sampler_Colormap);

            CBUFFER_START(UnityPerMaterial)
                float4 _Colormap_ST;
                half4 _Color0;
                half4 _Color1;
            CBUFFER_END

            StructuredBuffer<float3> vertexBuffer;
            StructuredBuffer<float2> uvBuffer;
            StructuredBuffer<float4> culledPositionsBuffer;

            float modelHeight;
            float worldScale;
            float time;
       
            #if HEIGHTMAP
                sampler2D heightmap;
                float4 height;
            #endif

            #if WIND
                float4 wind;
            #endif

            #if REPULSE
                float4 repulsorOrigin;
                float4 repulsorScale;
            #endif

            float random2D(float2 xy, float2 dir)
            {
                float val = dot(xy, dir);
                return frac(159.15 * sin(val));
            }


            v2f vert(vertdata v)
            {
                v2f o;
                o.uv = uvBuffer[v.vid];
                o.iid = v.iid;

                float3 instanceOffset = culledPositionsBuffer[v.iid].xyz;
                float3 vPos = vertexBuffer[v.vid];
                float3 worldPos = vPos + instanceOffset;

                #if WIND || HEIGHTMAP
                    float x = worldPos.x / (worldScale / 2.0f);
                    x = x * 0.5f + 0.5f;
                    float y = worldPos.z / (worldScale / 2.0f);
                    y = y * 0.5f + 0.5f;
                #endif

                #if WIND
                    float3 dirWind = normalize(float3(wind.x, wind.y, wind.z));
                    time = time * 0.5;
                    float3 windOffset = dirWind * wind.w + dirWind * wind.w * 0.25 * (sin(time * 3.14) * 0.5 + 0.5);
                    windOffset += float3(dirWind.x, -.5, dirWind.z) * wind.w * 1.0 * smoothstep(0.0, 1.0, max(0.0, cos(-x * 2.0 - time * .5) * cos(-y * 2.0 - time * 1.2))) * 0.5;
                    worldPos += windOffset * (vPos.y / modelHeight);
                #endif

                #if REPULSE
                    float3 repulseDir = normalize(worldPos - repulsorOrigin);
                    float radius = repulsorScale.x;
                    float3 deformed = worldPos + (repulseDir * 5.0) * (vPos.y / modelHeight);
                    float test = smoothstep(0.0, radius, length(worldPos - repulsorOrigin));
                    deformed.y = 0.0;
                    worldPos = lerp(deformed, worldPos, test);
                #endif

                #if HEIGHTMAP
                    float4 hmap = tex2Dlod(heightmap, float4(x, y, 0, 0));
                    worldPos.y += hmap.r * height.x + hmap.g * height.y + hmap.b * height.z;
                #endif

                o.vertex = TransformWorldToHClip(worldPos);
                o.height = vPos.y / modelHeight;

                return o;
            }

            fragdata frag(v2f i)
            {
                fragdata fragout;
 
                float4 colmap = SAMPLE_TEXTURE2D(_Colormap, sampler_Colormap, i.uv);
                float a = _Color0.w * (1 - i.height) + _Color1.w * i.height;
                float3 color = colmap.xyz + lerp(_Color0.xyz * _Color0.w, _Color1.xyz * _Color1.w, i.height * a);
                fragout.col = float4(color.x, color.y, color.z, 1.0 );
                return fragout;
            }
            ENDHLSL
        }
    }
}