using UnityEngine;
using System.Collections;

[ExecuteInEditMode] //Allow mirror to update live when still in edit mode
public class MirrorReflection : MonoBehaviour
{
	public bool mirror_DisablePixelLights = true;
	public int mirror_TextureSize = 256;
	public float mirror_ClipPlaneOffset = 0.07f;
	
	public LayerMask mirror_ReflectLayers = -1;

	//Make hashtable for cameras
	private Hashtable mirror_ReflectionCameras = new Hashtable(); 
	
	private RenderTexture mirror_ReflectionTexture = null;
	private int mirror_OldReflectionTextureSize = 0;
	
	private static bool shader_InsideRendering = false;

	//When object is to be rendered by a camera
	//Render reflections
	public void OnWillRenderObject()
	{
		var rend = GetComponent<Renderer>();
		if (!enabled || !rend || !rend.sharedMaterial || !rend.enabled)
			return;
		//If there is not currently a camera
		Camera cam = Camera.current;
		if(!cam)
			return;
		
		//Check for recursive refl        
		if(shader_InsideRendering)
			return;
		shader_InsideRendering = true;
		
		Camera reflCamera;
		CreateMirrorObjects(cam, out reflCamera);
		
		//Position & normal of refl plane
		Vector3 pos = transform.position;
		Vector3 normal = transform.up;
		
		//Disable pixel lights for reflection
		int oldPxLightCount = QualitySettings.pixelLightCount;
		if(mirror_DisablePixelLights)
			QualitySettings.pixelLightCount = 0;
		
		UpdateCameraModes(cam, reflCamera);
		
		//Render reflection & reflect camera around reflection plane
		float d = -Vector3.Dot (normal, pos) - mirror_ClipPlaneOffset;
		Vector4 reflPlane = new Vector4 (normal.x, normal.y, normal.z, d);
		
		Matrix4x4 reflection = Matrix4x4.zero;
		CalculateReflectionMatrix (ref reflection, reflPlane);
		Vector3 old_Pos = cam.transform.position;
		Vector3 new_Pos = reflection.MultiplyPoint(old_Pos);
		reflCamera.worldToCameraMatrix = cam.worldToCameraMatrix * reflection;
		
		//Make projection matrix so the near plane is the reflection plane & below and above is clipped
		Vector4 clipPlane = CameraSpacePlane(reflCamera, pos, normal, 1.0f);
		Matrix4x4 projection = cam.CalculateObliqueMatrix(clipPlane);
		reflCamera.projectionMatrix = projection;
		
		reflCamera.cullingMask = ~(1<<4) & mirror_ReflectLayers.value;
		reflCamera.targetTexture = mirror_ReflectionTexture;
		GL.SetRevertBackfacing (true);
		reflCamera.transform.position = new_Pos;
		Vector3 euler = cam.transform.eulerAngles;
		reflCamera.transform.eulerAngles = new Vector3(0, euler.y, euler.z);
		reflCamera.Render();
		reflCamera.transform.position = old_Pos;
		GL.SetRevertBackfacing (false);

		//Set material texture to reflection texture
		Material[] materials = rend.sharedMaterials;
		foreach(Material mat in materials) {
			if(mat.HasProperty("_ReflectionTex"))
				mat.SetTexture("_ReflectionTex", mirror_ReflectionTexture);
		}
		
		//Reset to old pixel light count
		if(mirror_DisablePixelLights)
			QualitySettings.pixelLightCount = oldPxLightCount;
		
		shader_InsideRendering = false;
	}
	
	
	//Clean up any created objects
	void OnDisable()
	{
		if(mirror_ReflectionTexture) {
			DestroyImmediate(mirror_ReflectionTexture);
			mirror_ReflectionTexture = null;
		}
		foreach(DictionaryEntry kvp in mirror_ReflectionCameras)
			DestroyImmediate(((Camera)kvp.Value).gameObject);
		mirror_ReflectionCameras.Clear();
	}
	
	
	private void UpdateCameraModes(Camera src, Camera dest)
	{
		if(dest == null)
			return;
		//Set camera to clear
		dest.clearFlags = src.clearFlags;
		dest.backgroundColor = src.backgroundColor;        

		//Update to match current camera
		dest.farClipPlane = src.farClipPlane;
		dest.nearClipPlane = src.nearClipPlane;
		dest.orthographic = src.orthographic;
		dest.fieldOfView = src.fieldOfView;
		dest.aspect = src.aspect;
		dest.orthographicSize = src.orthographicSize;
	}
	
	//Create any objects needed
	private void CreateMirrorObjects(Camera currentCamera, out Camera reflectionCamera)
	{
		reflectionCamera = null;
		
		//Render refl texture
		if(!mirror_ReflectionTexture || mirror_OldReflectionTextureSize != mirror_TextureSize)
		{
			if(mirror_ReflectionTexture)
				DestroyImmediate(mirror_ReflectionTexture);
			mirror_ReflectionTexture = new RenderTexture(mirror_TextureSize, mirror_TextureSize, 16);
			mirror_ReflectionTexture.name = "__MirrorReflection" + GetInstanceID();
			mirror_ReflectionTexture.isPowerOfTwo = true;
			mirror_ReflectionTexture.hideFlags = HideFlags.DontSave;
			mirror_OldReflectionTextureSize = mirror_TextureSize;
		}
		
		// Camera for reflection
		reflectionCamera = mirror_ReflectionCameras[currentCamera] as Camera;
		if(!reflectionCamera)
		{
			GameObject go = new GameObject("Mirror Refl Camera id" + GetInstanceID() + " for " + currentCamera.GetInstanceID(), typeof(Camera), typeof(Skybox));
			reflectionCamera = go.GetComponent<Camera>();
			reflectionCamera.enabled = false;
			reflectionCamera.transform.position = transform.position;
			reflectionCamera.transform.rotation = transform.rotation;
			reflectionCamera.gameObject.AddComponent<FlareLayer>();
			go.hideFlags = HideFlags.HideAndDontSave;
			mirror_ReflectionCameras[currentCamera] = reflectionCamera;
		}        
	}
	
	// Extended sign: returns -1, 0 or 1 based on sign of a
	private static float sgn(float a)
	{
		if (a > 0.0f) return 1.0f;
		if (a < 0.0f) return -1.0f;
		return 0.0f;
	}
	
	// Given position/normal of the plane, calculates plane in camera space.
	private Vector4 CameraSpacePlane (Camera cam, Vector3 pos, Vector3 normal, float sideSign)
	{
		Vector3 offsetPos = pos + normal * mirror_ClipPlaneOffset;
		Matrix4x4 m = cam.worldToCameraMatrix;
		Vector3 cpos = m.MultiplyPoint(offsetPos);
		Vector3 cnormal = m.MultiplyVector(normal).normalized * sideSign;
		return new Vector4(cnormal.x, cnormal.y, cnormal.z, -Vector3.Dot(cpos,cnormal));
	}
	
	// Calculates reflection matrix around the given plane
	private static void CalculateReflectionMatrix (ref Matrix4x4 reflectionMat, Vector4 plane)
	{
		reflectionMat.m00 = (1F - 2F*plane[0]*plane[0]);
		reflectionMat.m01 = (   - 2F*plane[0]*plane[1]);
		reflectionMat.m02 = (   - 2F*plane[0]*plane[2]);
		reflectionMat.m03 = (   - 2F*plane[3]*plane[0]);
		
		reflectionMat.m10 = (   - 2F*plane[1]*plane[0]);
		reflectionMat.m11 = (1F - 2F*plane[1]*plane[1]);
		reflectionMat.m12 = (   - 2F*plane[1]*plane[2]);
		reflectionMat.m13 = (   - 2F*plane[3]*plane[1]);
		
		reflectionMat.m20 = (   - 2F*plane[2]*plane[0]);
		reflectionMat.m21 = (   - 2F*plane[2]*plane[1]);
		reflectionMat.m22 = (1F - 2F*plane[2]*plane[2]);
		reflectionMat.m23 = (   - 2F*plane[3]*plane[2]);
		
		reflectionMat.m30 = 0F;
		reflectionMat.m31 = 0F;
		reflectionMat.m32 = 0F;
		reflectionMat.m33 = 1F;
	}
}