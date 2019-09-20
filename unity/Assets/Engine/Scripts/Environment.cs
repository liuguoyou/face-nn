﻿using CFUtilPoolLib;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

namespace XEngine
{

    public enum DrawType
    {
        Both,
        Draw,
        Cull,
    }

    [DisallowMultipleComponent, ExecuteInEditMode]
    [RequireComponent(typeof(Camera))]
    public class Environment : MonoBehaviour
    {
        //IBL
        public string EnveriomentCubePath;
        public float hdrScale = 4.6f;
        public float hdrPow = 0.1f;
        public float hdrAlpha = 0.5f;
        [XEngine.RangeAttribute(0, 1)]
        public float lightmapShadowMask = 0.25f;
        [XEngine.RangeAttribute(0, 1)]
        public float shadowIntensity = 0.1f;
        //Lighting
        public LightingModify lighting = null;
        public string SkyboxMatPath;
        public Vector3 sunDir = new Vector3(0, -1, 0);
        //Shadow
        public float shadowDepthBias = -0.03f;
        public float shadowNormalBias = 2.5f;
        public float shadowSmoothMin = 4f;
        public float shadowSmoothMax = 1f;
        public float shadowSampleSize = 1.2f;
        public float shadowPower = 2f;
        public FogModify fog = null;
        public bool fogEnable = true;
        [NoSerialized]
        public SceneData sceneData = null;

        private ResHandle EnveriomentCube;
        private ResHandle SkyBoxMat;
        private Cubemap SkyBox;
        //shadow
        [System.NonSerialized]
        public Vector3 cameraForward;
        [System.NonSerialized]
        public Vector3 lightProjectRight;
        [System.NonSerialized]
        public Vector3 lightProjectUp;
        [System.NonSerialized]
        public Vector3 lightProjectForward;
        [System.NonSerialized]
        public Vector3 translatePos;

#if UNITY_EDITOR
        [System.NonSerialized]
        public float shadowOrthoSize;

        public bool lightingFolder = true;
        public Light roleLight0;
        [System.NonSerialized]
        public TransformRotationGUIWrapper roleLight0Rot;
        public Light roleLight1;
        [System.NonSerialized]
        public TransformRotationGUIWrapper roleLight1Rot;


        public bool shadowFolder = true;
        public float shadowMapLevel = 0.25f;
        public bool shadowBound = false;
        public GameObject shadowCasterProxy = null;
        public Transform lookTarget;
        public bool drawShadowLighing = false;

        [System.NonSerialized]
        public RenderTexture shadowMap = null;
        private List<Renderer> shadowCasters = new List<Renderer>();
        private CommandBuffer shadowMapCb = null;
        private List<RenderBatch> shadowRenderBatchs = new List<RenderBatch>();
        private Material shadowMat = null;


        public bool debugFolder = true;
        public int quadIndex = -1;
        public DrawType drawType = DrawType.Both;
        public bool showObjects = false;

        public ShaderDebugContext debugContext = new ShaderDebugContext();
        public static int[] debugShaderIDS = new int[]
        {
            Shader.PropertyToID ("_GlobalDebugMode"),
            Shader.PropertyToID ("_DebugDisplayType"),
            Shader.PropertyToID ("_SplitAngle"),
            Shader.PropertyToID ("_SplitPos"),
        };

#endif

        void Awake()
        {
            SceneData.GlobalSceneData = sceneData;
            Shader.SetGlobalFloat("_GlobalDebugMode", 0);
        }

        public void Update()
        {
            if (lighting != null && fog != null)
            {
                UpdateEnv();
                bool hasFog = fogEnable;
                if (SceneView.lastActiveSceneView != null)
                {
                    var sceneViewState = SceneView.lastActiveSceneView.sceneViewState;
                    hasFog &= sceneViewState.showFog;
                }
                Shader.SetGlobalFloat(ShaderIDs.Env_FogDisable, !hasFog ? 1.0f : 0.0f);
            }
            if (sceneData.CameraRef == null)
            {
                InitRender(this.GetComponent<Camera>());
            }
            if (sceneData.CameraRef != null)
            {
                GeometryUtility.CalculateFrustumPlanes(sceneData.CameraRef, SceneData.frustumPlanes);
                Shader.SetGlobalVector(ShaderIDs.Env_GameViewCameraPos, sceneData.cameraPos);
            }
            SyncLightInfo();
            UpdateShadowCaster();
            BuildShadowMap();
            debugContext.Refresh();
        }

        void OnDestroy()
        {
            if (shadowMapCb != null)
            {
                shadowMapCb.Release();
                shadowMapCb = null;
            }
            if (shadowMap != null)
            {
                RuntimeUtilities.Destroy(shadowMap);
                shadowMap = null;
            }
        }

        

        private void ProcessResCb(ref ResHandle resHandle, ref Vector4Int param)
        {
            if (resHandle.obj != null)
            {
                if (resHandle.obj is Cubemap)
                {
                    EnveriomentCube.Set(ref resHandle);
                    return;
                }
                else if (resHandle.obj is Material)
                {
                    SkyBoxMat.Set(ref resHandle);
                    return;
                }
                LoadMgr.singleton.Destroy(ref resHandle);
            }
        }

        public void LoadRes(bool loadEnvCube = true, bool loadSkyBox = true)
        {
            ProcessLoadCb processResCb = ProcessResCb;
            if (loadEnvCube && !string.IsNullOrEmpty(EnveriomentCubePath))
            {
                string suffix = EnveriomentCubePath.EndsWith("HDR") ? ".exr" : ".tga";
                string path = string.Format("{0}/{1}{2}", AssetsConfig.GlobalAssetsConfig.ResourcePath, EnveriomentCubePath, suffix);
                EnveriomentCube.obj = AssetDatabase.LoadAssetAtPath<Cubemap>(path);
            }
            if (loadSkyBox && !string.IsNullOrEmpty(SkyboxMatPath))
            {
                string path = string.Format("{0}/{1}.mat", AssetsConfig.GlobalAssetsConfig.ResourcePath, SkyboxMatPath);
                SkyBoxMat.obj = AssetDatabase.LoadAssetAtPath<Material>(path);
            }
        }

        public void InitRender(UnityEngine.Camera camera)
        {
            sceneData.CameraRef = camera;
            sceneData.CameraTransCache = camera.transform;
            if (lighting == null) lighting = new LightingModify();
            if (fog == null) fog = new FogModify();

            RuntimeUtilities.EnableKeyword(ShaderIDs.Weather_ThunderKeyWord, false);
            RuntimeUtilities.EnableKeyword(ShaderIDs.Weather_RainbowKeyWord, false);
            RuntimeUtilities.EnableKeyword(ShaderIDs.Weather_RainEffectKeyWord, false);
            RuntimeUtilities.EnableKeyword(ShaderIDs.Weather_StarKeyWord, false);
            RuntimeUtilities.EnableKeyword(ShaderIDs.Weather_WeatherKeyWord, false);

            Shader.SetGlobalColor(ShaderIDs.Env_EffectParameter, Color.white);
            LoadRes();
            UpdateEnv();

            SceneData.editorSetRes = SetRes;
            if (shadowMapCb == null)
                shadowMapCb = new CommandBuffer { name = "Editor Shadow Map Cb" };

            shadowMat = AssetsConfig.GlobalAssetsConfig.ShadowCaster;
            UpdateShadowCaster();
        }

        void SetRes(System.Object obj, int type)
        {
            if (type == 0)
            {
                shadowMap = obj as RenderTexture;
            }
        }

        private void PrepareTransformGui(Light light, ref TransformRotationGUIWrapper wrapper)
        {
            if (light != null && (wrapper == null || wrapper.t != light.transform))
            {
                wrapper = EditorCommon.GetTransformRotatGUI(light.transform);
            }
        }

        
        private void UpdateShadowCaster()
        {
            if (lookTarget == null)
            {
                GameObject go = GameObject.Find("LookTarget");
                lookTarget = go != null ? go.transform : null;
            }
            if (lookTarget != null)
            {
                sceneData.currentEntityPos = lookTarget.position;
            }
            else
            {
                if (sceneData.CameraRef != null)
                    sceneData.currentEntityPos = cameraForward * 10 + sceneData.cameraPos;
            }
            shadowCasters.Clear();
            if (shadowCasterProxy == null)
            {
                shadowCasterProxy = GameObject.Find("ShadowCaster");
            }
            shadowRenderBatchs.Clear();
            bool first = true;
            Bounds shadowBound = new Bounds(Vector3.zero, Vector3.zero);
            if (shadowCasterProxy != null)
            {
                shadowCasterProxy.GetComponentsInChildren<Renderer>(false, shadowCasters);
                if (shadowCasters.Count > 0)
                {
                    for (int i = 0; i < shadowCasters.Count; ++i)
                    {
                        Renderer render = shadowCasters[i];
                        if (render != null &&
                            render.enabled &&
                            render.shadowCastingMode != ShadowCastingMode.Off &&
                            render.sharedMaterial != null)
                        {
                            RenderBatch rb = new RenderBatch();
                            rb.render = render;
                            rb.mat = render.sharedMaterial;
                            rb.mpbRef = null;
                            rb.passID = 0;
                            shadowRenderBatchs.Add(rb);
                            if (first)
                            {
                                shadowBound = render.bounds;
                                first = false;
                            }
                            else
                                shadowBound.Encapsulate(render.bounds);
                        }
                    }
                }
            }
            sceneData.shadowBound = shadowBound;
        }

        private void BuildShadowMap()
        {
            if (shadowMap != null && shadowMat != null)
            {
                shadowMapCb.Clear();
                shadowMapCb.ClearRenderTarget(true, true, Color.clear, 1.0f);
                shadowMapCb.SetViewProjectionMatrices(sceneData.shadowViewMatrix, sceneData.shadowProjMatrix);

                if (shadowCasters.Count > 0)
                {
                    for (int i = 0; i < shadowCasters.Count; ++i)
                    {
                        Renderer render = shadowCasters[i];
                        if (render != null &&
                            render.enabled &&
                            render.shadowCastingMode != ShadowCastingMode.Off)
                            shadowMapCb.DrawRenderer(render, shadowMat, 0, 0);
                    }
                    Graphics.SetRenderTarget(shadowMap);
                    Graphics.ExecuteCommandBuffer(shadowMapCb);
                    Graphics.SetRenderTarget(null);
                }
            }
        }

        void OnDrawGizmos()
        {
            Color color = Gizmos.color;
            if (drawShadowLighing)
            {
                Gizmos.color = Color.yellow;
                Gizmos.DrawWireSphere(translatePos, 0.3f);
                Vector3 targetPos = translatePos + lightProjectForward * 10;
                Gizmos.DrawWireSphere(targetPos, 0.3f);
                Vector3 leftUp = translatePos + lightProjectUp * shadowOrthoSize - lightProjectRight * shadowOrthoSize;
                Vector3 rightUp = translatePos + lightProjectUp * shadowOrthoSize + lightProjectRight * shadowOrthoSize;
                Vector3 leftBottom = translatePos - lightProjectUp * shadowOrthoSize - lightProjectRight * shadowOrthoSize;
                Vector3 rightBottom = translatePos - lightProjectUp * shadowOrthoSize + lightProjectRight * shadowOrthoSize;
                Gizmos.DrawLine(leftBottom, rightBottom);
                Gizmos.DrawLine(rightBottom, rightUp);
                Gizmos.DrawLine(rightUp, leftUp);
                Gizmos.DrawLine(leftUp, leftBottom);

                leftUp = targetPos + lightProjectUp * shadowOrthoSize - lightProjectRight * shadowOrthoSize;
                rightUp = targetPos + lightProjectUp * shadowOrthoSize + lightProjectRight * shadowOrthoSize;
                leftBottom = targetPos - lightProjectUp * shadowOrthoSize - lightProjectRight * shadowOrthoSize;
                rightBottom = targetPos - lightProjectUp * shadowOrthoSize + lightProjectRight * shadowOrthoSize;
                Gizmos.DrawLine(leftBottom, rightBottom);
                Gizmos.DrawLine(rightBottom, rightUp);
                Gizmos.DrawLine(rightUp, leftUp);
                Gizmos.DrawLine(leftUp, leftBottom);
                Handles.ArrowHandleCap(100, translatePos, Quaternion.LookRotation(lightProjectForward), 1, EventType.Repaint);
            }
            if (shadowBound)
            {
                Gizmos.DrawWireCube(sceneData.shadowBound.center, sceneData.shadowBound.size);
            }
            Gizmos.color = color;
        }

        public void UpdateSkyBox()
        {
            if (SkyBoxMat.obj is Material)
            {
                Material mat = SkyBoxMat.obj as Material;
                RenderSettings.skybox = mat;
                if (mat != null)
                {
                    SkyBox = mat.GetTexture(ShaderIDs.Env_SkyCubeTex) as Cubemap;
                    if (SkyBox != null)
                    {
                        Shader.SetGlobalTexture(ShaderIDs.Env_SkyCube, SkyBox);
                    }
                }
            }
        }

        public void UpdateEnv()
        {
            //IBL
            float maxMipmap = 1;
            if (EnveriomentCube.obj is Cubemap)
            {
                Cubemap envCube = EnveriomentCube.obj as Cubemap;
                Shader.SetGlobalTexture(ShaderIDs.Env_Cubemap, envCube);

                if (envCube != null)
                {
                    maxMipmap = envCube.mipmapCount;
                }
            }
            Shader.SetGlobalVector(ShaderIDs.Env_CubemapParam, new Vector4(hdrScale, hdrPow, hdrAlpha, maxMipmap));
            Shader.SetGlobalVector(ShaderIDs.Env_LightmapScale, new Vector4(1.0f / lightmapShadowMask, lightmapShadowMask, shadowIntensity, 0));

            UpdateSkyBox();

            SetLightInfo(ref lighting.roleLightInfo0, ShaderIDs.Env_DirectionalLightDir, ShaderIDs.Env_DirectionalLightColor);
            SetLightInfo(ref lighting.roleLightInfo1, ShaderIDs.Env_DirectionalLightDir1, ShaderIDs.Env_DirectionalLightColor1);

            Shader.SetGlobalColor(ShaderIDs.Env_AmbientParam, new Vector4(1.0f, 0, 0, 0));
            //Shadow
            Shader.SetGlobalVector(ShaderIDs.Env_ShadowBias, new Vector4(shadowDepthBias, shadowNormalBias, 0, 0));
            Shader.SetGlobalVector(ShaderIDs.Env_ShadowSmooth, new Vector4(shadowSmoothMin * -0.0001f, shadowSmoothMax * 0.0001f, shadowSampleSize, shadowPower));
            if (sceneData.ShadowRT != null)
            {
                int halfSize = (int)(sceneData.ShadowRT.width * 0.5f);
                Shader.SetGlobalVector(ShaderIDs.Env_ShadowMapSize, new Vector4(halfSize, 1.0f / halfSize, 0, 0));
                Shader.SetGlobalTexture(ShaderIDs.Env_ShadowMapTex, sceneData.ShadowRT);
            }
            //Fog
            Vector4 HeightFogParameters = new Vector4();
            HeightFogParameters.x = fog.Density;
            HeightFogParameters.y = fog.SkyboxHeight;
            HeightFogParameters.z = fog.EndHeight;
            HeightFogParameters.w = fog.StartDistance;

            Shader.SetGlobalVector(ShaderIDs.Env_HeightFogParameters, HeightFogParameters);
            Shader.SetGlobalVector(ShaderIDs.Env_HeighFogColorParameter0, fog.Color0.linear);
            Shader.SetGlobalVector(ShaderIDs.Env_HeighFogColorParameter1, fog.Color1.linear);
            Shader.SetGlobalVector(ShaderIDs.Env_HeighFogColorParameter2, fog.Color2.linear);
        }

        private void SetLightInfo(ref LightInfo li, int dirKey, int colorKey)
        {
            Shader.SetGlobalVector(dirKey, li.lightDir);
            Vector4 lightColorIntensity;
            lightColorIntensity = new Vector4(
                Mathf.Pow(li.lightColor.r * li.lightDir.w, 2.2f),
                Mathf.Pow(li.lightColor.g * li.lightDir.w, 2.2f),
                Mathf.Pow(li.lightColor.b * li.lightDir.w, 2.2f), shadowIntensity);
            Shader.SetGlobalVector(colorKey, lightColorIntensity);
        }

        public void SyncLightInfo()
        {
            PrepareTransformGui(roleLight0, ref roleLight0Rot);
            PrepareTransformGui(roleLight1, ref roleLight1Rot);
            SyncLight(roleLight0, ref lighting.roleLightInfo0);
            SyncLight(roleLight1, ref lighting.roleLightInfo1);
        }

        public void SyncLight(Light l, ref LightInfo li)
        {
            if (l != null)
            {
                li.lightDir = l.transform.rotation * -Vector3.forward;
                li.lightColor = l.color;
                li.lightDir.w = (l.enabled && l.gameObject.activeInHierarchy) ? l.intensity : 0;
            }
        }

    }
}