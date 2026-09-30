Shader "Clay/BeltPoint"
{
    // Minimal unlit point shader for asteroid/ice belts — the mesh is drawn with MeshTopology.Points, so each
    // vertex is one ~1px speck; hundreds of them form a dusty ring. Works under the URP 2D renderer (plain CG).
    Properties { _Color("Color", Color) = (0.6, 0.55, 0.5, 1) }
    SubShader
    {
        Tags { "RenderType" = "Opaque" "Queue" = "Geometry+10" }
        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"
            struct appdata { float4 vertex : POSITION; };
            struct v2f { float4 pos : SV_POSITION; };
            half4 _Color;
            v2f vert(appdata v) { v2f o; o.pos = UnityObjectToClipPos(v.vertex); return o; }
            half4 frag(v2f i) : SV_Target { return _Color; }
            ENDCG
        }
    }
    Fallback Off
}
