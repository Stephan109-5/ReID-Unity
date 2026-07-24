using System.Collections.Generic;
using System.Net;
using System.Net.Sockets;
using UnityEngine;
using UnityEngine.Rendering;

public class CameraTCPStream1 : MonoBehaviour
{
    public Camera targetCamera;
    public string ip = "127.0.0.1";
    public int port = 5005;

    TcpClient tcpClient;
    NetworkStream networkStream;
    RenderTexture rt;

    private bool isProcessing = false;
    private float timer = 0f;
    public float targetFPS = 15f;
    [Range(1, 100)] public int jpegQuality = 75; // Lower = smaller packets

    string GetVisibleObjectIds()
    {
        Plane[] planes = GeometryUtility.CalculateFrustumPlanes(targetCamera);
        GameObject[] allObjects = GameObject.FindGameObjectsWithTag("Character");
        List<string> dataEntries = new List<string>();

        foreach (GameObject obj in allObjects)
        {
            // 1. Get all skinned parts (body, arms, etc.)
            SkinnedMeshRenderer[] allParts = obj.GetComponentsInChildren<SkinnedMeshRenderer>();
            //Debug.Log(allParts);
            if (allParts.Length == 0) continue;

            // 2. Quick Frustum Check 
            // We check if at least one part's broad bounds is in the camera view
            bool potentiallyVisible = false;
            foreach (var smr in allParts)
            {
                if (GeometryUtility.TestPlanesAABB(planes, smr.bounds))
                {
                    potentiallyVisible = true;
                    break;
                }
            }

            if (!potentiallyVisible || !IsVisibleToCamera(obj)) continue;

            // 3. Calculate the Tight Rect by looking at all bones in all parts
            Rect rect = GetSnugRect(targetCamera, allParts);

            if (rect.width * rect.height <= 0.001) continue; // Skip if the box is degenerate (e.g., all bones behind camera)

            // 4. Format and Add
            string entry = $"{obj.name}|{rect.x:F3}|{rect.y:F3}|{rect.width:F3}|{rect.height:F3}";
            dataEntries.Add(entry);
        }
        return string.Join(",", dataEntries);
    }

    Rect GetTightRectFromAllParts(Camera cam, SkinnedMeshRenderer[] parts)
    {
        float minX = float.MaxValue;
        float minY = float.MaxValue;
        float maxX = float.MinValue;
        float maxY = float.MinValue;

        foreach (var smr in parts)
        {
            foreach (Transform bone in smr.bones)
            {
                // Convert bone world position to Viewport space
                Vector3 screenPt = cam.WorldToViewportPoint(bone.position);

                // If the bone is behind the camera, skip it to avoid inverted boxes
                if (screenPt.z < 0) continue;

                minX = Mathf.Min(minX, screenPt.x);
                minY = Mathf.Min(minY, screenPt.y);
                maxX = Mathf.Max(maxX, screenPt.x);
                maxY = Mathf.Max(maxY, screenPt.y);
            }
        }

        // Fallback: If no bones were found or visible, return a zero rect
        if (minX == float.MaxValue) return new Rect(0, 0, 0, 0);

        // Clamp values to 0-1 range so the box doesn't go "off-screen" in your data
        float x = Mathf.Clamp01(minX);
        float y = Mathf.Clamp01(minY);
        float w = Mathf.Clamp01(maxX) - x;
        float h = Mathf.Clamp01(maxY) - y;

        return new Rect(x, y, w, h);
    }

    Rect GetSnugRect(Camera cam, SkinnedMeshRenderer[] parts)
    {
        float minX = float.MaxValue, maxX = float.MinValue;
        float minY = float.MaxValue, maxY = float.MinValue;

        foreach (var smr in parts)
        {
            foreach (Transform bone in smr.bones)
            {
                Vector3 worldPos = bone.position;

                // 1. Process the bone itself
                UpdateMinMax(worldPos, cam, ref minX, ref minY, ref maxX, ref maxY);

                // 2. If this is the head, project a point upwards
                // Most character rigs use "Head" or "Neck" in the bone name
                if (bone.name.ToLower().Contains("head"))
                {
                    // Add a point roughly 25cm above the head bone to catch the top of the head
                    // Vector3.up is world-space; if your character can hang upside down, 
                    // use bone.up or similar logic.
                    Vector3 headTop = worldPos + Vector3.up * 0.25f;
                    UpdateMinMax(headTop, cam, ref minX, ref minY, ref maxX, ref maxY);
                }

                // 3. Optional: Catch the width of the shoulders/hips
                // If the box is TOO thin, you can project points left/right of the bones
            }
        }

        if (minX == float.MaxValue) return new Rect(0, 0, 0, 0);

        // Add a tiny bit of horizontal "meat" padding (e.g., 2% of screen width)
        // so the box isn't literally a line through the arm bones.
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
        if (screenPt.z < 0) return;

        minX = Mathf.Min(minX, screenPt.x);
        minY = Mathf.Min(minY, screenPt.y);
        maxX = Mathf.Max(maxX, screenPt.x);
        maxY = Mathf.Max(maxY, screenPt.y);
    }

    Rect GetHybridRect(Camera cam, SkinnedMeshRenderer[] parts)
    {
        float minX = float.MaxValue, maxX = float.MinValue;
        float minY = float.MaxValue, maxY = float.MinValue;

        foreach (var smr in parts)
        {
            // 1. Get the vertical limits from the actual mesh bounds (includes hair/hats)
            Bounds b = smr.bounds;
            Vector3[] corners = new Vector3[] {
            b.center + new Vector3(0,  b.extents.y, 0), // Top center
            b.center + new Vector3(0, -b.extents.y, 0)  // Bottom center
        };

            foreach (var p in corners)
            {
                Vector3 viewport = cam.WorldToViewportPoint(p);
                minY = Mathf.Min(minY, viewport.y);
                maxY = Mathf.Max(maxY, viewport.y);
            }

            // 2. Get the horizontal limits from the bones (keeps the box narrow)
            foreach (Transform bone in smr.bones)
            {
                Vector3 screenPt = cam.WorldToViewportPoint(bone.position);
                if (screenPt.z < 0) continue;

                minX = Mathf.Min(minX, screenPt.x);
                maxX = Mathf.Max(maxX, screenPt.x);
                // We also update Y here just in case a foot bone is lower than the center-bottom
                minY = Mathf.Min(minY, screenPt.y);
                maxY = Mathf.Max(maxY, screenPt.y);
            }
        }

        // Final Rect construction with clamping
        float x = Mathf.Clamp01(minX);
        float y = Mathf.Clamp01(minY);
        float w = Mathf.Clamp01(maxX) - x;
        float h = Mathf.Clamp01(maxY) - y;

        return new Rect(x, y, w, h);
    }

    bool IsVisibleToCamera(GameObject target)
    {
        // Get the bounds of the object
        Bounds bounds = GetFullBounds(target);

        // Check if the bounds are within the camera's frustum
        Plane[] planes = GeometryUtility.CalculateFrustumPlanes(targetCamera);
        if (!GeometryUtility.TestPlanesAABB(planes, bounds))
        {
            return false; // Object is completely outside the camera's view
        }

        // Check visibility using raycasts
        Vector3[] pointsToCheck = new Vector3[]
        {
        bounds.center,
        bounds.min,
        bounds.max,
        bounds.center + new Vector3(bounds.extents.x, 0, 0), // Right
        bounds.center - new Vector3(bounds.extents.x, 0, 0), // Left
        bounds.center + new Vector3(0, bounds.extents.y, 0), // Top
        bounds.center - new Vector3(0, bounds.extents.y, 0), // Bottom
        };

        foreach (Vector3 point in pointsToCheck)
        {
            Vector3 direction = point - targetCamera.transform.position;
            float distance = direction.magnitude;

            // Perform a raycast
            if (Physics.Raycast(targetCamera.transform.position, direction, out RaycastHit hit, distance))
            {
                // Check if the hit object is the target or its child
                if (hit.transform.IsChildOf(target.transform) || hit.transform == target.transform)
                {
                    return true; // At least one point is visible
                }
            }
        }

        return false; // No points are visible
    }

    Rect GetScreenRectFromBounds(Camera cam, Bounds bounds)
    {
        Vector3[] corners = new Vector3[8];
        Vector3 center = bounds.center;
        Vector3 ext = bounds.extents;

        corners[0] = center + new Vector3(-ext.x, -ext.y, -ext.z);
        corners[1] = center + new Vector3(-ext.x, -ext.y, ext.z);
        corners[2] = center + new Vector3(-ext.x, ext.y, -ext.z);
        corners[3] = center + new Vector3(-ext.x, ext.y, ext.z);
        corners[4] = center + new Vector3(ext.x, -ext.y, -ext.z);
        corners[5] = center + new Vector3(ext.x, -ext.y, ext.z);
        corners[6] = center + new Vector3(ext.x, ext.y, -ext.z);
        corners[7] = center + new Vector3(ext.x, ext.y, ext.z);

        float minX = float.MaxValue, minY = float.MaxValue;
        float maxX = float.MinValue, maxY = float.MinValue;

        foreach (Vector3 corner in corners)
        {
            Vector3 screenPoint = cam.WorldToViewportPoint(corner);
            minX = Mathf.Min(minX, screenPoint.x);
            minY = Mathf.Min(minY, screenPoint.y);
            maxX = Mathf.Max(maxX, screenPoint.x);
            maxY = Mathf.Max(maxY, screenPoint.y);
        }
        return new Rect(minX, minY, maxX - minX, maxY - minY);
    }

    Rect GetTightScreenRect(Camera cam, GameObject obj)
    {
        // Use the mesh filter or a predefined local size
        MeshFilter mf = obj.GetComponentInChildren<MeshFilter>();
        if (mf == null) return Rect.zero;

        Bounds localBounds = mf.sharedMesh.bounds;
        Vector3[] localCorners = new Vector3[8];

        // Get local corners
        Vector3 lCenter = localBounds.center;
        Vector3 lExt = localBounds.extents;
        // ... (Fill localCorners like you did with 'corners' but using lCenter/lExt)

        float minX = 1f, minY = 1f, maxX = 0f, maxY = 0f;

        foreach (Vector3 lp in localCorners)
        {
            // Transform local point to World, then World to Screen
            Vector3 worldPt = obj.transform.TransformPoint(lp);
            Vector3 screenPt = cam.WorldToViewportPoint(worldPt);

            minX = Mathf.Min(minX, screenPt.x);
            minY = Mathf.Min(minY, screenPt.y);
            maxX = Mathf.Max(maxX, screenPt.x);
            maxY = Mathf.Max(maxY, screenPt.y);
        }
        return new Rect(minX, minY, maxX - minX, maxY - minY);
    }

    void Start()
    {
        // 1. Setup Render Texture
        rt = new RenderTexture(1280, 720, 24, RenderTextureFormat.ARGB32);
        rt.Create();
        targetCamera.targetTexture = rt;

        // 2. Setup TCP
        tcpClient = new TcpClient();
        tcpClient.Connect(ip, port);
        networkStream = tcpClient.GetStream();

        Debug.Log($"TCP Stream initialized to {ip}:{port}");
    }

    void Update()
    {
        timer += Time.deltaTime;
        if (timer >= (1f / targetFPS) && !isProcessing)
        {
            timer = 0f;
            isProcessing = true;
            string ids = GetVisibleObjectIds();

            //Debug.Log(ids);
            AsyncGPUReadback.Request(rt, 0, TextureFormat.RGBA32, (request) =>
            {
                OnCompleteReadback(request, ids);
            });
        }
    }

    void OnCompleteReadback(AsyncGPUReadbackRequest request, string ids)
    {
        if (request.hasError) { isProcessing = false; return; }

        // 1. Encode the frame
        var encodedBytes = ImageConversion.EncodeNativeArrayToJPG(
            request.GetData<byte>(),
            rt.graphicsFormat,
            (uint)rt.width,
            (uint)rt.height,
            0, jpegQuality
        );

        // 2. Prepare metadata
        byte[] idBytes = System.Text.Encoding.UTF8.GetBytes(ids);
        byte[] idLengthPrefix = System.BitConverter.GetBytes(idBytes.Length);

        // 3. Prepare image length
        byte[] managedEncoded = encodedBytes.ToArray();
        byte[] imgLengthPrefix = System.BitConverter.GetBytes(managedEncoded.Length);

        // 4. Create the final buffer: [idLen][id][imgLen][img]
        byte[] finalPacket = new byte[4 + idBytes.Length + 4 + managedEncoded.Length];
        int offset = 0;
        System.Buffer.BlockCopy(idLengthPrefix, 0, finalPacket, offset, 4);
        offset += 4;
        System.Buffer.BlockCopy(idBytes, 0, finalPacket, offset, idBytes.Length);
        offset += idBytes.Length;
        System.Buffer.BlockCopy(imgLengthPrefix, 0, finalPacket, offset, 4);
        offset += 4;
        System.Buffer.BlockCopy(managedEncoded, 0, finalPacket, offset, managedEncoded.Length);

        //Debug.Log($"idBytes.Length: {idBytes.Length}, managedEncoded.Length: {managedEncoded.Length}, finalPacket.Length: {finalPacket.Length}");
        //Debug.Log($"idLengthPrefix: {System.BitConverter.ToString(idLengthPrefix)}");
        //Debug.Log($"imgLengthPrefix: {System.BitConverter.ToString(imgLengthPrefix)}");
        //Debug.Log($"First 16 bytes of finalPacket: {System.BitConverter.ToString(finalPacket, 0, Mathf.Min(16, finalPacket.Length))}");

        encodedBytes.Dispose();

        // 5. Send over TCP
        try
        {
            if (networkStream != null && networkStream.CanWrite)
            {
                networkStream.Write(finalPacket, 0, finalPacket.Length);
                networkStream.Flush();
            }
        }
        catch (System.Exception e)
        {
            Debug.LogError($"TCP Send Error on Host {ip} and port {port}: " + e.Message);
        }
        finally
        {
            isProcessing = false;
        }
    }

    private static int GetLength(byte[] idBytes)
    {
        return idBytes.Length;
    }

    Bounds GetFullBounds(GameObject obj)
    {
        Renderer[] renderers = obj.GetComponentsInChildren<Renderer>();
        if (renderers.Length == 0) return new Bounds(obj.transform.position, Vector3.zero);
        Bounds fullBounds = renderers[0].bounds;
        for (int i = 1; i < renderers.Length; i++)
        {
            fullBounds.Encapsulate(renderers[i].bounds);
        }
        return fullBounds;
    }

    Bounds GetAgentBounds(UnityEngine.AI.NavMeshAgent agent)
    {
        return new Bounds(
            agent.transform.position + Vector3.up * (agent.height / 2f),
            new Vector3(agent.radius * 2, agent.height, agent.radius * 2)
        );
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