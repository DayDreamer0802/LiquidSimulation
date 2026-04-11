Shader "Custom/Tornado"

{

Properties

{

[HDR] _Color ("Wind Color", Color) = (0.0, 0.8, 0.6, 0.3)

_BottomRadius ("Eye Radius (Bottom)", Float) = 0.1

_TopRadius ("Anvil Radius (Top)", Float) = 3.0


_FunnelCurve ("Funnel Curve (Fatness)", Range(0.1, 3.0)) = 0.8


_SpinForce ("Spin Force", Float) = 10.0

_LiftForce ("Lift Force", Float) = 5.0


_SwayStrength ("Sway Strength (Bending)", Float) = 2.0


// ==============================================================

// ���ؼ��޸� 1��������ƽ�̱���ʵ�֡�����ܶࡱ���ο�

// �������� X (ˮƽ�ܶ�) �� Y (��ֱ�ܶ�) ��Ĭ��ֵ

// ==============================================================

_SpiralTiling ("Spiral Density (X:Spin, Y:Lift)", Vector) = (80.0, 60.0, 0, 0)

_SpiralThickness ("Spring Thickness", Range(0.1, 0.9)) = 0.6

}

SubShader

{

Tags { "RenderType"="Transparent" "Queue"="Transparent+50" }

Blend SrcAlpha OneMinusSrcAlpha

ZWrite Off

Cull Off



Pass

{

CGPROGRAM

#pragma vertex vert

#pragma fragment frag

#include "UnityCG.cginc"



struct appdata

{

float4 vertex : POSITION;

float2 uv : TEXCOORD0;

float3 normal : NORMAL;

};



struct v2f

{

float4 pos : SV_POSITION;

float2 uv : TEXCOORD0;

float3 normal : TEXCOORD1;

float3 viewDir : TEXCOORD2;

float heightRatio : TEXCOORD3;

};



float4 _Color;

float _BottomRadius;

float _TopRadius;

float _SpinForce;

float _LiftForce;

float _FunnelCurve;


float _SwayStrength;

float2 _SpiralTiling;

float _SpiralThickness;



v2f vert (appdata v)

{

v2f o;


float heightRatio = v.vertex.y + 0.5;

o.heightRatio = heightRatio;



float currentRadius = _BottomRadius + (_TopRadius - _BottomRadius) * pow(heightRatio, _FunnelCurve);

v.vertex.xz *= currentRadius;



float twistAngle = pow(heightRatio, 1.5) * _SpinForce * 0.5;

float s = sin(twistAngle);

float c = cos(twistAngle);

v.vertex.xz = float2(

v.vertex.x * c - v.vertex.z * s,

v.vertex.x * s + v.vertex.z * c

);


float swayX = sin(heightRatio * 3.14 + _Time.y * 2.0) * _SwayStrength * heightRatio;

float swayZ = cos(heightRatio * 2.5 + _Time.y * 1.5) * _SwayStrength * heightRatio;

v.vertex.x += swayX;

v.vertex.z += swayZ;



o.pos = UnityObjectToClipPos(v.vertex);


o.uv = v.uv;

o.uv.x += _Time.y * _SpinForce * 0.5;

o.uv.y -= _Time.y * _LiftForce * 0.5;


o.normal = UnityObjectToWorldNormal(v.normal);

o.viewDir = WorldSpaceViewDir(v.vertex);

return o;

}



fixed4 frag (v2f i) : SV_Target

{

float3 n = normalize(i.normal);

float3 v = normalize(i.viewDir);


float rim = 1.0 - saturate(dot(n, v));

float alpha = pow(rim, 1.5) * _Color.a;


float spiralWave = sin(i.uv.x * _SpiralTiling.x + i.uv.y * _SpiralTiling.y);


// Ϊ���ñ�Ե����ͣ�����ͨ������ smoothstep �ķ�Χ��΢��

float springCutout = smoothstep(-1.0 + _SpiralThickness * 2.0, -0.5 + _SpiralThickness * 2.0, spiralWave);


float windStreaks = sin(i.uv.x * 30.0 + i.uv.y * 15.0) * 0.25 + 0.75;


float topFade = smoothstep(1.0, 0.7, i.heightRatio);

float bottomFade = smoothstep(0.0, 0.1, i.heightRatio);



alpha *= springCutout * windStreaks * topFade * bottomFade;



// ==============================================================

// ���ؼ��޸� 2����ɾ������Ӳ������룡

// ���д����ǵ�����˸����Ӳ��Ե������

// ==============================================================

// clip(alpha - 0.01);



return fixed4(_Color.rgb, alpha);

}

ENDCG

}

}

} 