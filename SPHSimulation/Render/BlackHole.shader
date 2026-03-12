Shader "Custom/BlackHole"

{

Properties

{

_HaloColor ("Halo Color", Color) = (0.1, 0.0, 0.2, 1)

// �������ĺڶ���ռ�ȣ�0.5����ռ�ݰ뾶��һ��

_EventHorizon ("Event Horizon (Black Core)", Range(0.1, 0.9)) = 0.5

}

SubShader

{

// ����Ϊ͸�����У�������ˮ�����Ϸ�

Tags { "RenderType"="Transparent" "Queue"="Transparent+100" }

Blend SrcAlpha OneMinusSrcAlpha

ZWrite Off

Cull Back



Pass

{

CGPROGRAM

#pragma vertex vert

#pragma fragment frag

#include "UnityCG.cginc"



struct appdata

{

float4 vertex : POSITION;

float3 normal : NORMAL;

};



struct v2f

{

float4 pos : SV_POSITION;

float3 viewDir : TEXCOORD0;

float3 normal : TEXCOORD1;

};



float4 _HaloColor;

float _EventHorizon;



v2f vert (appdata v)

{

v2f o;

o.pos = UnityObjectToClipPos(v.vertex);

o.normal = UnityObjectToWorldNormal(v.normal);

o.viewDir = WorldSpaceViewDir(v.vertex);

return o;

}



fixed4 frag (v2f i) : SV_Target

{

float3 n = normalize(i.normal);

float3 v = normalize(i.viewDir);


// ��ˣ����ĵ�Ϊ1����ԵΪ0

float NdotV = saturate(dot(n, v));


// --- �����߼� ---

// ��� NdotV �����ӽ���ֵ��˵������������ -> �������

// ��� С����ֵ��˵������Χ -> ��Ⱦ���β����Ե��ɢ


if (NdotV > (1.0 - _EventHorizon))

{

return fixed4(0, 0, 0, 1); // ���ԵĴ���

}

else

{

// ������Χ���ε�͸���Ƚ��� (Խ�����ڶ�Խ����Խ������ԵԽ͸��)

float edgeFade = NdotV / (1.0 - _EventHorizon);

float alpha = pow(edgeFade, 3.0); // ������˥�����ñ�Ե�����


return fixed4(_HaloColor.rgb, alpha * _HaloColor.a);

}

}

ENDCG

}

}

}