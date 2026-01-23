// Copyright (c) Meta Platforms, Inc. and affiliates.

Shader "Unlit/UIMotionProxy"
{
    Properties
    {
    }
    SubShader
    {
        Tags
	    {
            "RenderType" = "Opaque"
            "RenderPipeline" = "UniversalPipeline"
            "UniversalMaterialType" = "Lit"
            "IgnoreProjector" = "True"
	    }

        LOD 100

        Blend One Zero
        ZTest Less
        ZWrite On
        Cull Off

        Pass
        {
            Name "MotionVectors"
            Tags{ "LightMode" = "MotionVectors"}
            Tags { "RenderType" = "Opaque" }

            HLSLPROGRAM
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/ObjectMotionVectors.hlsl"

            #pragma vertex vert
            #pragma fragment frag

            ENDHLSL
        }
    }
}
