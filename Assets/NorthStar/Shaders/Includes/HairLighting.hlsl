// Copyright (c) Meta Platforms, Inc. and affiliates.

#ifndef HAIR_LIGHTING_INCLUDED
#define HAIR_LIGHTING_INCLUDED

#pragma target 5.0

#ifdef __INTELLISENSE__
    #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/RealtimeLights.hlsl"
    #define _ADDITIONAL_LIGHTS

    Texture2D<float> _MainLightShadowmapTexture;
    SamplerComparisonState sampler_LinearClampCompare;

	float Sq(float x) 
	{
		return x * x;
	}

	#define PI radians(180.0)
#endif

SamplerState _LinearClampSampler, _PointClampSampler;
float4 _MainLightShadowmapTexture_TexelSize;

float LongitudinalScatter(float alpha, float beta, float sinThetaI, float sinThetaR)
{
	return rcp(beta * sqrt(2.0 * PI)) * exp(-Sq(sinThetaI + sinThetaR - alpha) * rcp(2.0 * Sq(beta)));
}

float FSchlick(float f0, float LdotH) 
{
	return lerp(f0, 1.0, pow(1.0 - LdotH, 5.0));
}

float CosFromSin(float sinTheta)
{
	return SinFromCos(sinTheta);
}

// Projects vector a onto vector b
float3 Project(float3 a, float3 b)
{
	return b * dot(a, b);
}

float3 ProjectOntoPlane(float3 a, float3 b)
{
	return a - Project(a, b);
}

float3 CalculateLighting(float3 albedo, float roughness, float3 L, float3 V, float3 N, float3 T, bool isAmbient, float shift)
{
	if(isAmbient)
		roughness += 0.2;

	float sinThetaI = dot(T, L);
	float sinThetaR = dot(T, V);
	float cosThetaI = dot(N, L);
	float cosThetaR = dot(N, V);
	
    float cosThetaD = sqrt((1.0 + cosThetaI * cosThetaR + sinThetaR * sinThetaI) * 0.5);
	
	 // Projection onto the normal plane, and since phi is the relative angle, we take the cosine in this projection.
    half3 VProj = ProjectOntoPlane(V, T);
    half3 LProj = ProjectOntoPlane(L, T);
    float cosPhi = dot(LProj, VProj) * rsqrt(dot(LProj, LProj) * dot(VProj, VProj));
    float phi = FastACos(cosPhi);

    // Fixed for approximate human hair IOR
    float sinThetaT = sinThetaR / 1.55;
    float cosThetaT = SafeSqrt(1 - Sq(sinThetaT));
	
	// Can avoid normalizing by dividing by the length of each
	//float cosPhi = dot(Lp, Vp) * rsqrt(dot(Lp, Lp) * dot(Vp, Vp));
	float cosHalfPhi = sqrt(0.5 + 0.5 * cosPhi);

	float n = 1.55; // Hair is IOR 1.55
	float f0 = Sq(1 - n) / Sq(1 + n);
	float a = rcp(1.19 / cosThetaD + 0.36 * cosThetaD);
	
	float area = isAmbient ? 0.2 : 0.0;

	// R
	float longitudinalScatterR = LongitudinalScatter(-2.0 * shift, roughness + area, sinThetaI, sinThetaR);
	float fresnelR = FSchlick(f0, sqrt(0.5 + 0.5 * dot(L, V)));
	float distributionR = 0.25 * cosHalfPhi;
	float attenuationR = fresnelR;
	float azimuthalScatterR = distributionR * attenuationR;
	float3 S = longitudinalScatterR * azimuthalScatterR;
	
	// TT
	float longitudinalScatterTt = LongitudinalScatter(shift, 0.5 * roughness + area, sinThetaI, sinThetaR);
    float fresnelOffsetTt = (1 + a * (0.6 - 0.8 * cosPhi)) * cosHalfPhi;
    float fresnelTt = FSchlick(f0, cosThetaD * sqrt(1.0 - Sq(fresnelOffsetTt)));
    float3 transmittanceTt = pow(albedo, sqrt(1.0 - Sq(fresnelOffsetTt) * Sq(a)) / (2.0 * cosThetaD));
    float distributionTt = exp(-3.65 * cosPhi - 3.98);
    float3 attenuationTt = Sq(1.0 - fresnelTt) * fresnelR * transmittanceTt;
    float3 azimuthalScatterTt = distributionTt * attenuationTt;
	
    if(isAmbient)
        S *= saturate(dot(L, V) + 1.0);
    else
        S += longitudinalScatterTt * azimuthalScatterTt;
	
	// TRT
	float longitudinalScatterTrt = LongitudinalScatter(4.0 * shift, 2.0 * roughness + area, sinThetaI, sinThetaR);
	float fresnelOffsetTrt = sqrt(3.0) / 2.0;
	float fresnelTrt = FSchlick(f0, cosThetaD * sqrt(1.0 - Sq(fresnelOffsetTrt)));
	float3 transmittanceTrt = pow(albedo, 0.8 / cosThetaD);
	float distributionTrt = exp(17.0 * cosPhi - 16.78);
	float3 attenuationTrt = Sq(1.0 - fresnelTrt) * fresnelTt * transmittanceTrt;
	float3 azimuthalScatterTrt = distributionTrt * attenuationTrt;
	S += longitudinalScatterTrt * azimuthalScatterTrt;
	
	return max(0.0, S * PI) + albedo * saturate(dot(N, L));
}

void HairLighting_float(float3 position, float3 N, float3 T, float3 V, float3 albedo, float smoothness, float shift, float occlusion, float microShadow, out float3 color)
{
	#ifdef SHADERGRAPH_PREVIEW
		color = 1.0;
	#else
		T = Orthonormalize(T, N);
    
#ifdef _MAIN_LIGHT_SHADOWS_CASCADE
		half cascadeIndex = ComputeCascadeIndex(position);
#else
		half cascadeIndex = 0;
#endif
	    float3 shadowCoord = mul(_MainLightWorldToShadow[cascadeIndex], float4(position, 1.0)).xyz;
		Light mainLight = GetMainLight(float4(shadowCoord, 1), position, 0.0);
    
		float perceptualRoughness = 1.0 - smoothness;
		float roughness = perceptualRoughness * perceptualRoughness;

		float4 positionCS = TransformWorldToHClip(position);
		float2 positionNDC = positionCS.xy / max(positionCS.w, 0.0001); // NDC: [-1, 1] - Prevent division by zero
		float2 positionSS = saturate(positionNDC * 0.5 + 0.5);          // Screen space: [0, 1] - Clamp to valid range
	
		// Indirect Light
		float3 fakeN = normalize(V - T * dot(V, T));
		color = SHADERGRAPH_BAKED_GI(position, fakeN, positionSS, 0.0, 0.0, false) * CalculateLighting(albedo, roughness, fakeN, V, N, T, true, shift) * occlusion;
	
		// Direct light
		color += CalculateLighting(albedo, roughness, mainLight.direction, V, N, T, false, shift) * mainLight.color * mainLight.shadowAttenuation * ComputeMicroShadowing(occlusion, dot(N, mainLight.direction), microShadow);
    
		#ifdef _ADDITIONAL_LIGHTS
			// Shade additional lights if enabled
			uint numAdditionalLights = GetAdditionalLightsCount();
			for (uint i = 0; i < numAdditionalLights; i++) 
			{
				Light light = GetAdditionalLight(i, position, 1.0);
				color += CalculateLighting(albedo, roughness, light.direction, V, N, T, false, shift) * light.color * light.distanceAttenuation;
			}
		#endif
	#endif
}
#endif