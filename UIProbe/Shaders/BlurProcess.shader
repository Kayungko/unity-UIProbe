Shader "Hidden/UIProbe/BlurProcess"
{
    Properties
    {
        _MainTex ("Texture", 2D) = "white" {}
        _BlurSize ("Blur Size", Float) = 1.0
    }
    SubShader
    {
        Cull Off ZWrite Off ZTest Always

        Pass
        {
            Name "HorizontalBlur"
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"

            struct appdata {
                float4 vertex : POSITION;
                float2 uv : TEXCOORD0;
            };

            struct v2f {
                float2 uv : TEXCOORD0;
                float4 vertex : SV_POSITION;
            };

            sampler2D _MainTex;
            float4 _MainTex_ST;
            float4 _MainTex_TexelSize;
            float _BlurSize;

            v2f vert (appdata v) {
                v2f o;
                o.vertex = UnityObjectToClipPos(v.vertex);
                o.uv = v.uv;
                return o;
            }

            fixed4 frag (v2f i) : SV_Target {
                float weight[3] = {0.4026, 0.2442, 0.0545};
                float2 uv = i.uv;
                fixed3 color = tex2D(_MainTex, uv).rgb * weight[0];
                
                for (int j = 1; j < 3; j++) {
                    color += tex2D(_MainTex, uv + float2(_MainTex_TexelSize.x * j * _BlurSize, 0)).rgb * weight[j];
                    color += tex2D(_MainTex, uv - float2(_MainTex_TexelSize.x * j * _BlurSize, 0)).rgb * weight[j];
                }
                return fixed4(color, 1.0);
            }
            ENDCG
        }

        Pass
        {
            Name "VerticalBlur"
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"

            struct appdata {
                float4 vertex : POSITION;
                float2 uv : TEXCOORD0;
            };

            struct v2f {
                float2 uv : TEXCOORD0;
                float4 vertex : SV_POSITION;
            };

            sampler2D _MainTex;
            float4 _MainTex_ST;
            float4 _MainTex_TexelSize;
            float _BlurSize;

            v2f vert (appdata v) {
                v2f o;
                o.vertex = UnityObjectToClipPos(v.vertex);
                o.uv = v.uv;
                return o;
            }

            fixed4 frag (v2f i) : SV_Target {
                float weight[3] = {0.4026, 0.2442, 0.0545};
                float2 uv = i.uv;
                fixed3 color = tex2D(_MainTex, uv).rgb * weight[0];
                
                for (int j = 1; j < 3; j++) {
                    color += tex2D(_MainTex, uv + float2(0, _MainTex_TexelSize.y * j * _BlurSize)).rgb * weight[j];
                    color += tex2D(_MainTex, uv - float2(0, _MainTex_TexelSize.y * j * _BlurSize)).rgb * weight[j];
                }
                return fixed4(color, 1.0);
            }
            ENDCG
        }
    }
}
