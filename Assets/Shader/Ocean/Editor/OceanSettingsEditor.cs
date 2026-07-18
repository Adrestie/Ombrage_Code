using UnityEditor;
using UnityEngine;

namespace Ocean
{
    [CustomEditor(typeof(OceanSettings))]
    public class OceanSettingsEditor : Editor
    {
        private int _selectedBeaufort = -1;
        private int _activeTab;

        private static readonly string[] TabNames =
        {
            "General", "Surface", "Shading", "Wake",
			"Underwater","Wind", "Settings", "References"
        };

        private static readonly Color ActiveTabColor = new Color(0.3f, 0.7f, 1f, 1f);
        private static readonly Color HeaderColor    = new Color(0.2f, 0.6f, 0.9f, 1f);

        private void OnEnable()
        {
            _activeTab = EditorPrefs.GetInt("OceanSettings_ActiveTab", 0);
        }

        public override void OnInspectorGUI()
        {
            serializedObject.Update();

            DrawBeaufortSection();

            EditorGUILayout.Space(6);
            DrawSeparator();
            EditorGUILayout.Space(6);

            DrawTabBar();

            EditorGUILayout.Space(4);

            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            switch (_activeTab)
            {
                case 0: DrawGeneralTab();     break;
                case 1: DrawSurfaceTab();        break;
                case 2: DrawShadingTab();      break;
                case 3: DrawWakeTab();      break;
				case 4: DrawUnderwaterTab();   break;
                case 5: DrawWindTab();         break;
                case 6: DrawSettingsTab();    break;
                case 7: DrawReferenciesTab();    break;
			}
            EditorGUILayout.EndVertical();

            serializedObject.ApplyModifiedProperties();
        }

        // ── Tab bar ─────────────────────────────────────────────────────

        private void DrawTabBar()
        {
            EditorGUILayout.BeginHorizontal();
            for (int i = 0; i < 4; i++) DrawTabButton(i);
            EditorGUILayout.EndHorizontal();

            EditorGUILayout.BeginHorizontal();
            for (int i = 4; i < TabNames.Length; i++) DrawTabButton(i);
            EditorGUILayout.EndHorizontal();
        }

        private void DrawTabButton(int index)
        {
            bool isActive = _activeTab == index;

            var style = new GUIStyle(EditorStyles.miniButton)
            {
                fixedHeight = 26,
                fontStyle   = isActive ? FontStyle.Bold : FontStyle.Normal,
                fontSize    = 11
            };

            var prevBg = GUI.backgroundColor;
            if (isActive)
                GUI.backgroundColor = ActiveTabColor;

            if (GUILayout.Button(TabNames[index], style))
            {
                _activeTab = index;
                EditorPrefs.SetInt("OceanSettings_ActiveTab", index);
            }

            GUI.backgroundColor = prevBg;
        }

        // ── Tab content ─────────────────────────────────────────────────

        private void DrawGeneralTab()
        {
            EditorGUILayout.PropertyField(serializedObject.FindProperty("waterLevel"));
            EditorGUILayout.PropertyField(serializedObject.FindProperty("meshResolution"));
            EditorGUILayout.PropertyField(serializedObject.FindProperty("followMode"));
		}

        private void DrawSurfaceTab()
        {
            EditorGUILayout.PropertyField(serializedObject.FindProperty("amplitude"));
            EditorGUILayout.PropertyField(serializedObject.FindProperty("choppiness"));
            EditorGUILayout.PropertyField(serializedObject.FindProperty("smallWaveCutoff"));
            EditorGUILayout.PropertyField(serializedObject.FindProperty("timeScale"));
			EditorGUILayout.PropertyField(serializedObject.FindProperty("cascadeCount"));
			EditorGUILayout.PropertyField(serializedObject.FindProperty("cascadeScale"));
			EditorGUILayout.PropertyField(serializedObject.FindProperty("foamThreshold"));
			EditorGUILayout.PropertyField(serializedObject.FindProperty("foamDecay"));
			EditorGUILayout.PropertyField(serializedObject.FindProperty("foamStrength"));
			EditorGUILayout.PropertyField(serializedObject.FindProperty("foamBlurRadius"));
			EditorGUILayout.PropertyField(serializedObject.FindProperty("shoreFoamDistance"));
			EditorGUILayout.PropertyField(serializedObject.FindProperty("shoreFoamStrength"));
			EditorGUILayout.PropertyField(serializedObject.FindProperty("shoreFoamFalloff"));
			EditorGUILayout.PropertyField(serializedObject.FindProperty("waveShoreAttenuationDist"));
			EditorGUILayout.PropertyField(serializedObject.FindProperty("waveShoreMinAmplitude"));
			EditorGUILayout.PropertyField(serializedObject.FindProperty("foamTexScale"));
			EditorGUILayout.PropertyField(serializedObject.FindProperty("foamTexBlend"));

			EditorGUILayout.Space(8);
			DrawSeparator();
			EditorGUILayout.LabelField("Shore Waves", EditorStyles.boldLabel);
			EditorGUILayout.PropertyField(serializedObject.FindProperty("enableShoreWaves"));
			EditorGUILayout.PropertyField(serializedObject.FindProperty("shoreWashHeight"));
			EditorGUILayout.PropertyField(serializedObject.FindProperty("shoreWashFoamWidth"));
			EditorGUILayout.PropertyField(serializedObject.FindProperty("shoreWetDarkening"));
			EditorGUILayout.PropertyField(serializedObject.FindProperty("shoreFoamNoiseScale"));
			EditorGUILayout.PropertyField(serializedObject.FindProperty("shoreWashPower"));

			EditorGUILayout.Space(8);
			DrawSeparator();
			EditorGUILayout.LabelField("Shore Intersection Map", EditorStyles.boldLabel);
			EditorGUILayout.PropertyField(serializedObject.FindProperty("enableShoreIntersectionMap"));
			EditorGUILayout.PropertyField(serializedObject.FindProperty("shoreMapResolution"));
			EditorGUILayout.PropertyField(serializedObject.FindProperty("shoreMapSize"));

			EditorGUILayout.Space(8);
			DrawSeparator();
			EditorGUILayout.PropertyField(serializedObject.FindProperty("reflectionIntensity"));
			EditorGUILayout.PropertyField(serializedObject.FindProperty("refractionStrength"));
			EditorGUILayout.PropertyField(serializedObject.FindProperty("refractionDepthFade"));
			EditorGUILayout.PropertyField(serializedObject.FindProperty("usePlanarReflection"));
			EditorGUILayout.PropertyField(serializedObject.FindProperty("planarReflectionResolution"));
			EditorGUILayout.PropertyField(serializedObject.FindProperty("planarReflectionBlend"));
			EditorGUILayout.PropertyField(serializedObject.FindProperty("reflectionLayers"));
			EditorGUILayout.PropertyField(serializedObject.FindProperty("reflectionClipOffset"));
			EditorGUILayout.PropertyField(serializedObject.FindProperty("reflectionUpdateInterval"));
		}

        private void DrawShadingTab()
        {
            EditorGUILayout.PropertyField(serializedObject.FindProperty("shallowColor"));
            EditorGUILayout.PropertyField(serializedObject.FindProperty("deepColor"));
            EditorGUILayout.PropertyField(serializedObject.FindProperty("sssColor"));
            EditorGUILayout.PropertyField(serializedObject.FindProperty("foamColor"));
            EditorGUILayout.PropertyField(serializedObject.FindProperty("fresnelPower"));
			EditorGUILayout.PropertyField(serializedObject.FindProperty("sssIntensity"));
            EditorGUILayout.PropertyField(serializedObject.FindProperty("sssPower"));
            EditorGUILayout.PropertyField(serializedObject.FindProperty("sssSpread"));
            EditorGUILayout.PropertyField(serializedObject.FindProperty("specularPower"));
            EditorGUILayout.PropertyField(serializedObject.FindProperty("oceanRoughness"));
            EditorGUILayout.PropertyField(serializedObject.FindProperty("heightScale"));
            EditorGUILayout.PropertyField(serializedObject.FindProperty("ambientStrength"));
            EditorGUILayout.PropertyField(serializedObject.FindProperty("wrapDiffuse"));
			EditorGUILayout.PropertyField(serializedObject.FindProperty("depthAbsorption"));
			EditorGUILayout.PropertyField(serializedObject.FindProperty("depthMaxDistance"));
			EditorGUILayout.PropertyField(serializedObject.FindProperty("absorptionColor"));
			EditorGUILayout.PropertyField(serializedObject.FindProperty("wrapDiffuse"));
			EditorGUILayout.PropertyField(serializedObject.FindProperty("horizonColor"));
			EditorGUILayout.PropertyField(serializedObject.FindProperty("zenithColor"));
			EditorGUILayout.PropertyField(serializedObject.FindProperty("underwaterFogColor"));
			EditorGUILayout.PropertyField(serializedObject.FindProperty("godRayColor"));
		}

		private void DrawWakeTab()
		{
			EditorGUILayout.PropertyField(serializedObject.FindProperty("wakeIntensity"));
			EditorGUILayout.PropertyField(serializedObject.FindProperty("wakeSplashWidth"));
			EditorGUILayout.PropertyField(serializedObject.FindProperty("wakeStampRadius"));
			EditorGUILayout.PropertyField(serializedObject.FindProperty("wakeMinSpeed"));
			EditorGUILayout.PropertyField(serializedObject.FindProperty("wakeFullSpeed"));
			EditorGUILayout.PropertyField(serializedObject.FindProperty("wakeBlurStrength"));
			EditorGUILayout.PropertyField(serializedObject.FindProperty("wakeBlurPasses"));
			EditorGUILayout.PropertyField(serializedObject.FindProperty("wakeFadeRate"));
			EditorGUILayout.PropertyField(serializedObject.FindProperty("wakeDisplacementScale"));
			EditorGUILayout.PropertyField(serializedObject.FindProperty("wakeCoverageSize"));
			EditorGUILayout.PropertyField(serializedObject.FindProperty("wakeResolution"));
			EditorGUILayout.PropertyField(serializedObject.FindProperty("wakeFadeDepth"));
		}

		private void DrawUnderwaterTab()
        {
            EditorGUILayout.PropertyField(serializedObject.FindProperty("enableUnderwater"));
            EditorGUILayout.PropertyField(serializedObject.FindProperty("underwaterResolutionScale"));
            EditorGUILayout.PropertyField(serializedObject.FindProperty("underwaterFogDensity"));
			EditorGUILayout.PropertyField(serializedObject.FindProperty("underwaterFogStartDistance"));
            EditorGUILayout.PropertyField(serializedObject.FindProperty("underwaterDepthAbsorption"));
            EditorGUILayout.PropertyField(serializedObject.FindProperty("underwaterDepthDarkeningMin"));
            EditorGUILayout.PropertyField(serializedObject.FindProperty("underwaterDistortionStrength"));
            EditorGUILayout.PropertyField(serializedObject.FindProperty("underwaterDistortionSpeed"));
            EditorGUILayout.PropertyField(serializedObject.FindProperty("underwaterDistortionScale"));
            EditorGUILayout.PropertyField(serializedObject.FindProperty("godRayIntensity"));
            EditorGUILayout.PropertyField(serializedObject.FindProperty("godRaySteps"));
			EditorGUILayout.PropertyField(serializedObject.FindProperty("godRayFadeInDepth"));
			EditorGUILayout.PropertyField(serializedObject.FindProperty("godRaySharpness"));
            EditorGUILayout.PropertyField(serializedObject.FindProperty("godRayBeamScale"));
            EditorGUILayout.PropertyField(serializedObject.FindProperty("godRaySunFollow"));
            EditorGUILayout.PropertyField(serializedObject.FindProperty("godRayDepthFade"));
            EditorGUILayout.PropertyField(serializedObject.FindProperty("godRayExtinction"));
            EditorGUILayout.PropertyField(serializedObject.FindProperty("godRayMaxDist"));
            EditorGUILayout.PropertyField(serializedObject.FindProperty("godRaySpeed"));
			EditorGUILayout.PropertyField(serializedObject.FindProperty("causticsScale"));
			EditorGUILayout.PropertyField(serializedObject.FindProperty("causticsSpeed"));
			EditorGUILayout.PropertyField(serializedObject.FindProperty("causticsIntensity"));
			EditorGUILayout.PropertyField(serializedObject.FindProperty("causticsMaxDepth"));
			EditorGUILayout.PropertyField(serializedObject.FindProperty("causticsChromaSpread"));
			EditorGUILayout.PropertyField(serializedObject.FindProperty("causticsChromaOffsetR"));
			EditorGUILayout.PropertyField(serializedObject.FindProperty("causticsChromaOffsetB"));
			EditorGUILayout.PropertyField(serializedObject.FindProperty("surfaceFromBelowDistortion"));
			EditorGUILayout.PropertyField(serializedObject.FindProperty("snellWindowDepth"));

			EditorGUILayout.Space(8);
			DrawSeparator();
			EditorGUILayout.LabelField("Underwater Lighting", EditorStyles.boldLabel);
			EditorGUILayout.PropertyField(serializedObject.FindProperty("enableUnderwaterLighting"));
			EditorGUILayout.PropertyField(serializedObject.FindProperty("transitionDepth"));
			EditorGUILayout.PropertyField(serializedObject.FindProperty("absorptionCoefficients"));
			EditorGUILayout.PropertyField(serializedObject.FindProperty("attenuationDepthScale"));
			EditorGUILayout.PropertyField(serializedObject.FindProperty("lightIntensityDecay"));
			EditorGUILayout.PropertyField(serializedObject.FindProperty("lightIntensityFloor"));
		}

        private void DrawWindTab()
        {
			EditorGUILayout.PropertyField(serializedObject.FindProperty("windAngleOffset"));
			EditorGUILayout.PropertyField(serializedObject.FindProperty("windSpeedFactor"));
			EditorGUILayout.PropertyField(serializedObject.FindProperty("windTurbulenceFactor"));
			EditorGUILayout.PropertyField(serializedObject.FindProperty("windPulseMagnitudeFactor"));
			EditorGUILayout.PropertyField(serializedObject.FindProperty("windPulseFrequencyFactor"));
		}

		private void DrawSettingsTab()
        {
			EditorGUILayout.PropertyField(serializedObject.FindProperty("resolution"));
			EditorGUILayout.PropertyField(serializedObject.FindProperty("spectrumType"));
			EditorGUILayout.PropertyField(serializedObject.FindProperty("patchSize"));
			EditorGUILayout.PropertyField(serializedObject.FindProperty("seed"));
			EditorGUILayout.PropertyField(serializedObject.FindProperty("tessMaxFactor"));
			EditorGUILayout.PropertyField(serializedObject.FindProperty("tessMaxDistance"));
			EditorGUILayout.PropertyField(serializedObject.FindProperty("fetchLength"));
			EditorGUILayout.PropertyField(serializedObject.FindProperty("jonswapGamma"));
			EditorGUILayout.PropertyField(serializedObject.FindProperty("verboseLogs"));
            EditorGUILayout.PropertyField(serializedObject.FindProperty("underwaterDebugMode"));
			EditorGUILayout.Space(6);
			EditorGUILayout.PropertyField(serializedObject.FindProperty("debugView"));
			EditorGUILayout.PropertyField(serializedObject.FindProperty("debugCascade"));
			EditorGUILayout.PropertyField(serializedObject.FindProperty("fftDebugShader"));
		}

		private void DrawReferenciesTab()
		{
			EditorGUILayout.PropertyField(serializedObject.FindProperty("oceanMaterial"));
			EditorGUILayout.Space(6);
			EditorGUILayout.PropertyField(serializedObject.FindProperty("initSpectrumShader"));
			EditorGUILayout.PropertyField(serializedObject.FindProperty("timeDependentSpectrumShader"));
			EditorGUILayout.PropertyField(serializedObject.FindProperty("fftShader"));
			EditorGUILayout.PropertyField(serializedObject.FindProperty("postProcessShader"));
			EditorGUILayout.PropertyField(serializedObject.FindProperty("foamBlurShader"));
			EditorGUILayout.PropertyField(serializedObject.FindProperty("wakeTrailShader"));
			EditorGUILayout.PropertyField(serializedObject.FindProperty("shoreIntersectionShader"));
		}

		// ── Beaufort presets ────────────────────────────────────────────

		private void DrawBeaufortSection()
        {
            var prevColor = GUI.backgroundColor;
            GUI.backgroundColor = HeaderColor;
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            GUI.backgroundColor = prevColor;

            var headerStyle = new GUIStyle(EditorStyles.boldLabel)
            {
                fontSize  = 13,
                alignment = TextAnchor.MiddleCenter
            };
            EditorGUILayout.LabelField("Beaufort Scale Presets", headerStyle);
            EditorGUILayout.Space(4);

            EditorGUILayout.BeginHorizontal();
            for (int i = 0; i <= 3; i++)
                DrawBeaufortButton(i);
            EditorGUILayout.EndHorizontal();

            EditorGUILayout.BeginHorizontal();
            for (int i = 4; i <= 6; i++)
                DrawBeaufortButton(i);
            GUILayout.FlexibleSpace();
            EditorGUILayout.EndHorizontal();

            if (_selectedBeaufort >= 0 && _selectedBeaufort < BeaufortPresets.Descriptions.Length)
            {
                EditorGUILayout.Space(4);
                EditorGUILayout.HelpBox(
                    BeaufortPresets.Descriptions[_selectedBeaufort],
                    MessageType.Info);
            }

            EditorGUILayout.Space(4);

            EditorGUI.BeginDisabledGroup(_selectedBeaufort < 0);
            if (GUILayout.Button("Apply Selected Preset", GUILayout.Height(26)))
            {
                var settings = (OceanSettings)target;
                Undo.RecordObject(settings, $"Apply Beaufort Preset: {BeaufortPresets.Labels[_selectedBeaufort]}");
                var data = BeaufortPresets.GetPreset((BeaufortLevel)_selectedBeaufort);
                BeaufortPresets.ApplyToSettings(settings, data);
                EditorUtility.SetDirty(settings);
            }
            EditorGUI.EndDisabledGroup();

            EditorGUILayout.EndVertical();
        }

        private void DrawBeaufortButton(int index)
        {
            bool isSelected = _selectedBeaufort == index;

            var style = new GUIStyle(isSelected ? EditorStyles.miniButtonMid : EditorStyles.miniButton)
            {
                fixedHeight = 24,
                fontStyle   = isSelected ? FontStyle.Bold : FontStyle.Normal
            };

            var prevBg = GUI.backgroundColor;
            if (isSelected)
                GUI.backgroundColor = new Color(0.3f, 0.7f, 1f, 1f);

            if (GUILayout.Button(BeaufortPresets.Labels[index], style))
                _selectedBeaufort = isSelected ? -1 : index;

            GUI.backgroundColor = prevBg;
        }

        // ── Helpers ─────────────────────────────────────────────────────

        private static void DrawSeparator()
        {
            var rect = EditorGUILayout.GetControlRect(false, 1);
            rect.height = 1;
            EditorGUI.DrawRect(rect, new Color(0.5f, 0.5f, 0.5f, 0.5f));
        }
    }
}
