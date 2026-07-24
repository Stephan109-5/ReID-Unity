using System;
using System.Collections.Generic;
using System.Net;
using System.Threading.Tasks;
using System.Net.Sockets;
using System.Text;
using UnityEngine;
using UnityEngine.Rendering;

public class CameraTCPStream : MonoBehaviour
{
    [Serializable]
    private class CameraPacketMetadata
    {
        public string camera_id;
        public int frame_id;
        public long timestamp_utc_ms;
        public string[] object_ids;
    }

    public Camera targetCamera;
    public string cameraId = "cam_0";
    public string ip = "127.0.0.1";
    public int port = 5005;

    public int renderHeight = 720;
    public int renderWidth = 1280;

    TcpClient tcpClient;
    NetworkStream networkStream;
    RenderTexture rt;

    private volatile bool isProcessing = false;
    private float timer = 0f;
    private int frameId = 0;
    public float targetFPS = 30f;
    [Range(1, 100)] public int jpegQuality = 100;
    private GameObject[] cachedCharacters;

    string[] GetVisibleObjectIds()
    {
        Plane[] planes = GeometryUtility.CalculateFrustumPlanes(targetCamera);
        List<string> dataEntries = new List<string>();

        foreach (GameObject obj in cachedCharacters)
        {
            SkinnedMeshRenderer[] allParts = obj.GetComponentsInChildren<SkinnedMeshRenderer>();
            if (allParts.Length == 0) continue;

            // 1. Frustum Check (Is it in the camera's field of view?)
            bool potentiallyVisible = false;
            foreach (var smr in allParts)
            {
                if (GeometryUtility.TestPlanesAABB(planes, smr.bounds))
                {
                    potentiallyVisible = true;
                    break;
                }
            }

            if (!potentiallyVisible) continue;

            // 2. Occlusion Check (Is something blocking the view?)
            if (!IsObjectVisibleViaRaycast(obj)) continue;

            Rect rect = Get2DBoundingBox(targetCamera, obj);
            if (rect.width * rect.height <= 0.001f) continue;

            string entry = $"{obj.name}|{rect.x:F3}|{rect.y:F3}|{rect.width:F3}|{rect.height:F3}";
            dataEntries.Add(entry);
        }

        return dataEntries.ToArray();
    }

    Rect Get2DBoundingBox(Camera cam, GameObject targetObj)
    {
        float minX = float.MaxValue, maxX = float.MinValue;
        float minY = float.MaxValue, maxY = float.MinValue;

        Animator anim = targetObj.GetComponent<Animator>();

        if (anim != null && anim.isHuman)
        {
            // List of core bones required to define a body outline
            HumanBodyBones[] coreBones = new HumanBodyBones[]
            {
                HumanBodyBones.Head, HumanBodyBones.Hips,
                HumanBodyBones.LeftHand, HumanBodyBones.RightHand,
                HumanBodyBones.LeftFoot, HumanBodyBones.RightFoot,
                HumanBodyBones.LeftToes, HumanBodyBones.RightToes
            };

            foreach (HumanBodyBones boneType in coreBones)
            {
                Transform bone = anim.GetBoneTransform(boneType);
                if (bone == null) continue;

                Vector3 worldPos = bone.position;
                UpdateMinMax(worldPos, cam, ref minX, ref minY, ref maxX, ref maxY);

                // Add offset to head bone
                if (boneType == HumanBodyBones.Head)
                {
                    Vector3 headTop = worldPos + Vector3.up * 0.25f;
                    UpdateMinMax(headTop, cam, ref minX, ref minY, ref maxX, ref maxY);
                }
            }
        }

        if (minX == float.MaxValue) return new Rect(0, 0, 0, 0);

        float paddingX = 0.01f;
        float x = Mathf.Clamp01(minX - paddingX);
        float y = Mathf.Clamp01(minY);
        float w = Mathf.Clamp01(maxX + paddingX) - x;
        float h = Mathf.Clamp01(maxY) - y;

        return new Rect(x, y, w, h);
    }

    void UpdateMinMax(Vector3 worldPoint, Camera cam, ref float minX, ref float minY, ref float maxX, ref float maxY)
    {
        Vector3 screenPt = cam.WorldToViewportPoint(worldPoint);
        if (screenPt.z < 0) return; // Only consider points in front of the camera

        minX = Mathf.Min(minX, screenPt.x);
        minY = Mathf.Min(minY, screenPt.y);
        maxX = Mathf.Max(maxX, screenPt.x);
        maxY = Mathf.Max(maxY, screenPt.y);
    }

    bool IsObjectVisibleViaRaycast(GameObject targetObj)
    {
        // Start the ray slightly in front of the camera to avoid hitting the camera itself
        Vector3 startPos = targetCamera.transform.position;

        // Target a reliable center point. If your character has a head bone, use that. 
        // Otherwise, use the center of its Collider or Renderer bounds.
        Vector3 targetPos = targetObj.transform.position + Vector3.up * 1.0f; // Adjust offset to target the chest/head

        Vector3 direction = targetPos - startPos;
        float distance = direction.magnitude;

        RaycastHit hit;
        // Shoot the ray. 
        if (Physics.Raycast(startPos, direction, out hit, distance))
        {
            // If the ray hits something else BEFORE reaching the target, it's occluded.
            // We check if the hit object is the target itself or a child of it.
            if (hit.transform != targetObj.transform && !hit.transform.IsChildOf(targetObj.transform))
            {
                return false; // Hidden behind something!
            }
        }

        return true; // Clear line of sight
    }

    void Start()
    {
        rt = new RenderTexture(renderWidth, renderHeight, 24, RenderTextureFormat.ARGB32);
        rt.Create();
        targetCamera.targetTexture = rt;
        cachedCharacters = GameObject.FindGameObjectsWithTag("Character");

        tcpClient = new TcpClient();
        tcpClient.Connect(ip, port);
        networkStream = tcpClient.GetStream();

        Debug.Log($"TCP Stream initialized to {ip}:{port}");
    }

    void Update()
    {
        // 1. EXIT EARLY IF THE GAME IS PAUSED
        // If timeScale is 0, we don't want to process any new frames.
        if (Time.timeScale <= 0) return;

        timer += Time.deltaTime;

        if (timer >= (1f / targetFPS))
        {
            if (!isProcessing)
            {
                timer -= (1f / targetFPS);
                isProcessing = true;

                string[] objectIds = GetVisibleObjectIds();

                AsyncGPUReadback.Request(rt, 0, TextureFormat.RGBA32, (request) =>
                {
                    // 2. DOUBLE CHECK: If the game paused while the GPU was thinking, 
                    // we can choose to drop the frame here.
                    if (Time.timeScale <= 0)
                    {
                        isProcessing = false;
                        return;
                    }

                    OnCompleteReadback(request, objectIds);
                });
            }
        }
    }

    void OnCompleteReadback(AsyncGPUReadbackRequest request, string[] objectIds)
    {
        if (request.hasError)
        {
            isProcessing = false;
            return;
        }

        // Check if the RenderTexture is still valid
        if (rt == null || !rt.IsCreated())
        {
            isProcessing = false;
            return;
        }

        // 1. Get the raw data and encode to JPG
        var encodedNativeBytes = ImageConversion.EncodeNativeArrayToJPG(
            request.GetData<byte>(),
            rt.graphicsFormat,
            (uint)rt.width,
            (uint)rt.height,
            0,
            jpegQuality
        );

        // Copy to a managed array so the background thread can own the data
        byte[] managedJpgBytes = encodedNativeBytes.ToArray();
        encodedNativeBytes.Dispose(); // Clean up native memory immediately

        // Prepare metadata
        CameraPacketMetadata metadata = new CameraPacketMetadata
        {
            camera_id = cameraId,
            frame_id = frameId++,
            timestamp_utc_ms = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
            object_ids = objectIds
        };

        // 2. OFFLOAD TO BACKGROUND THREAD: JSON and Networking
        // This prevents the "Network Wait" from freezing your game.
        Task.Run(() =>
        {
            try
            {
                SendPacketOverNetwork(managedJpgBytes, metadata);
            }
            catch (Exception e)
            {
                Debug.LogError($"Network Send Error: {e.Message}");
            }
            finally
            {
                isProcessing = false;
            }
        });
    }

    private void SendPacketOverNetwork(byte[] jpgBytes, CameraPacketMetadata metadata)
    {
        // Serialize JSON
        string metadataJson = JsonUtility.ToJson(metadata);
        byte[] metadataBytes = Encoding.UTF8.GetBytes(metadataJson);

        // Prepare prefixes
        byte[] metadataLengthPrefix = BitConverter.GetBytes(metadataBytes.Length);
        byte[] imageLengthPrefix = BitConverter.GetBytes(jpgBytes.Length);

        // Combine into final packet
        byte[] finalPacket = new byte[4 + metadataBytes.Length + 4 + jpgBytes.Length];
        int offset = 0;

        Buffer.BlockCopy(metadataLengthPrefix, 0, finalPacket, offset, 4);
        offset += 4;
        Buffer.BlockCopy(metadataBytes, 0, finalPacket, offset, metadataBytes.Length);
        offset += metadataBytes.Length;
        Buffer.BlockCopy(imageLengthPrefix, 0, finalPacket, offset, 4);
        offset += 4;
        Buffer.BlockCopy(jpgBytes, 0, finalPacket, offset, jpgBytes.Length);

        // Write to stream
        if (networkStream != null && networkStream.CanWrite)
        {
            networkStream.Write(finalPacket, 0, finalPacket.Length);
            // We don't necessarily need Flush() every frame; TCP will handle it.
        }
    }
    void OnDestroy()
    {
        if (networkStream != null)
        {
            networkStream.Close();
            networkStream = null;
        }

        if (tcpClient != null)
        {
            tcpClient.Close();
            tcpClient = null;
        }
    }
}