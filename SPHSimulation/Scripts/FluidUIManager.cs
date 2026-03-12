using System;
using System.Reflection;
using HighPerform.test.Scripts;
using UnityEngine;

namespace HighPerform.SPHSimulation.Scripts
{
    public class FluidUIManager : MonoBehaviour
    {
        [Header("拖入你的流体脚本 (不拖会自动找)")] public LiquidTest fluidScript;

        private float _deltaTime;
        private bool _isPanelOpen;

 
        private GUIStyle _fpsButtonStyle;
        private GUIStyle _panelStyle;
        private GUIStyle _headerStyle;
        private GUIStyle _skillTimerStyle;


        private FieldInfo _fIsTornado;
        private FieldInfo _fTimerTornado;
        private FieldInfo _fIsBlackHole;
        private FieldInfo _fTimerBlackHole;

        [Obsolete("Obsolete")]
        void Start()
        {
            if (fluidScript == null)
            {
                fluidScript = FindObjectOfType<LiquidTest>();
            }

            // 初始化反射目标
            if (fluidScript != null)
            {
                var flags = BindingFlags.NonPublic | BindingFlags.Instance;
                var type = fluidScript.GetType();
                _fIsTornado = type.GetField("isTornadoActive", flags);
                _fTimerTornado = type.GetField("tornadoTimer", flags);
                _fIsBlackHole = type.GetField("isBlackHoleActive", flags);
                _fTimerBlackHole = type.GetField("blackHoleTimer", flags);
            }
        }

        void Update()
        {
       
            _deltaTime += (Time.unscaledDeltaTime - _deltaTime) * 0.1f;
        }

        void OnGUI()
        {
            if (fluidScript == null) return;

            InitStyles();

            float fps = 1.0f / _deltaTime;
            string fpsText = $"FPS: {Mathf.Ceil(fps)}";
            Rect buttonRect = new Rect(Screen.width - 130, 10, 120, 40);

            if (GUI.Button(buttonRect, fpsText, _fpsButtonStyle))
            {
                _isPanelOpen = !_isPanelOpen;
            }
            
            DrawSkillTimers();
            
            if (_isPanelOpen)
            {
                Rect panelRect = new Rect(Screen.width - 360, 60, 350, 700); // 拉长了一点面板以容纳更多参数
                GUI.Box(panelRect, "", _panelStyle);
                GUILayout.BeginArea(new Rect(panelRect.x + 15, panelRect.y + 15, panelRect.width - 30,
                    panelRect.height - 30));

                // --- RunTime Settings ---
                GUILayout.Label("--- RunTime Settings (实时生效) ---", _headerStyle);
                GUILayout.Space(5);

                // 玩家与范围参数
                fluidScript.playerRadius = DrawSlider("玩家半径 (Radius)", fluidScript.playerRadius, 0.5f, 20f);
                fluidScript.playerMoveSpeed = DrawSlider("玩家移速 (Speed)", fluidScript.playerMoveSpeed, 0f, 100f);
                fluidScript.playerGravity = DrawSlider("玩家重力", fluidScript.playerGravity, 0f, 2000f);
                fluidScript.playerJumpForce = DrawSlider("玩家跳跃力", fluidScript.playerJumpForce, 0f, 1000f);


                GUILayout.Label("--- 粒子模拟属性  ---", _headerStyle);
                GUILayout.Space(5);
                fluidScript.gravity = DrawSlider("重力", fluidScript.gravity, -50f, 50f);
                fluidScript.collisionScale = DrawSlider("排斥缩放", fluidScript.collisionScale, 0.2f, 2.0f);
                fluidScript.particleMass = DrawSlider("粒子质量", fluidScript.particleMass, 0.1f, 10f);
                fluidScript.smoothingRadius = DrawSlider("平滑核半径", fluidScript.smoothingRadius, 0.2f, 2f);
                fluidScript.restDensity = DrawSlider("静态密度", fluidScript.restDensity, 0.1f, 100f);
                fluidScript.gasConstant = DrawSlider("压力刚度系数", fluidScript.gasConstant, 0.5f, 500f);
                fluidScript.viscosity = DrawSlider("粘滞阻力系数", fluidScript.viscosity, 0.5f, 200f);
                GUILayout.Space(15);

                // --- ReSet Settings ---
                GUILayout.Label("--- ReSet Settings (重启生效) ---", _headerStyle);
                GUILayout.Space(5);

           
                GUILayout.BeginHorizontal();
                GUILayout.Label("粒子数量 (GridCount):", GUILayout.Width(140));
                string countStr = GUILayout.TextField(fluidScript.gridCount.ToString());
                if (int.TryParse(countStr, out int newCount))
                {
                    newCount = Mathf.Clamp(newCount, 0, 1048575);
                    fluidScript.gridCount = newCount;
                }

                GUILayout.EndHorizontal();
                GUILayout.BeginHorizontal();
                fluidScript.maxSize = (int)DrawSlider("世界尺寸(半径)", fluidScript.maxSize, 8, 128);
                GUILayout.EndHorizontal();
                GUILayout.Space(25);

                GUI.backgroundColor = new Color(0.8f, 0.2f, 0.2f);
                if (GUILayout.Button("重新生成 (Restart)", GUILayout.Height(40)))
                {
                    RestartFluid();
                    _isPanelOpen = false; // 点完自动收起
                }

                GUI.backgroundColor = Color.white;
                GUILayout.EndArea();
            }
        }

        private void DrawSkillTimers()
        {
            if (_fIsTornado == null) return;

            bool isTornado = (bool)_fIsTornado.GetValue(fluidScript);
            bool isBlackHole = (bool)_fIsBlackHole.GetValue(fluidScript);

            float yOffset = 20f;
            float centerX = Screen.width / 2f;

            if (isTornado)
            {
                float timer = (float)_fTimerTornado.GetValue(fluidScript);
                Rect rect = new Rect(centerX - 100, yOffset, 200, 35);
                GUI.Label(rect, $"🌪️ 龙卷风: {timer:F1}s", _skillTimerStyle);
                yOffset += 40f;
            }

            if (isBlackHole)
            {
                float timer = (float)_fTimerBlackHole.GetValue(fluidScript);
                Rect rect = new Rect(centerX - 100, yOffset, 200, 35);
                GUI.Label(rect, $"🕳️ 黑洞: {timer:F1}s", _skillTimerStyle);
            }
        }

        private float DrawSlider(string label, float val, float min, float max)
        {
            GUILayout.BeginHorizontal();
            GUILayout.Label(label, GUILayout.Width(130));
            float newVal = GUILayout.HorizontalSlider(val, min, max);
            GUILayout.Label(newVal.ToString("F2"), GUILayout.Width(45));
            GUILayout.EndHorizontal();
            return newVal;
        }

        private void RestartFluid()
        {
            if (fluidScript != null)
            {
                MethodInfo createMethod = fluidScript.GetType()
                    .GetMethod("Create", BindingFlags.NonPublic | BindingFlags.Instance);
                if (createMethod != null)
                {
                    createMethod.Invoke(fluidScript, null);
                }
            }
        }

        private void InitStyles()
        {
            if (_fpsButtonStyle == null)
            {
                _fpsButtonStyle = new GUIStyle(GUI.skin.button)
                {
                    fontSize = 16,
                    fontStyle = FontStyle.Bold
                };

                _panelStyle = new GUIStyle(GUI.skin.box);
                Texture2D tex = new Texture2D(1, 1);
                tex.SetPixel(0, 0, new Color(0.1f, 0.1f, 0.1f, 0.9f));
                tex.Apply();
                _panelStyle.normal.background = tex;

                _headerStyle = new GUIStyle(GUI.skin.label)
                {
                    fontSize = 14,
                    fontStyle = FontStyle.Bold,
                    alignment = TextAnchor.MiddleCenter
                };
                _headerStyle.normal.textColor = Color.yellow;

                _skillTimerStyle = new GUIStyle(GUI.skin.label)
                {
                    fontSize = 24,
                    fontStyle = FontStyle.Bold,
                    alignment = TextAnchor.MiddleCenter
                };
                _skillTimerStyle.normal.textColor = Color.cyan;
            }
        }
    }
}