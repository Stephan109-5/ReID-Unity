using System.Collections.Generic;
using System.Net;
using System.Net.Sockets;
using UnityEngine;
using UnityEngine.Rendering;

public class CameraStreamUDP : MonoBehaviour
{
    public Camera targetCamera;
    public string ip = "127.0.0.1";
    public int port = 5005;

    UdpClient udpClient;
    IPEndPoint remoteEndPoint;
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
            // 1. Get the "Mega-Bounds" of the entire prefab hierarchy
            Bounds combinedBounds = GetFullBounds(obj);

            // 2. Check if that combined volume is visible
            if (GeometryUtility.TestPlanesAABB(planes, combinedBounds))
            {
                //if (IsVisibleToCamera(obj))
                //{
                    // 3. Project that volume onto the screen
                    Rect rect = GetScreenRectFromBounds(targetCamera, combinedBounds);

                    // Format: Name|x|y|w|h
                    string entry = $"{obj.name}|{rect.x:F3}|{rect.y:F3}|{rect.width:F3}|{rect.height:F3}";
                    dataEntries.Add(entry);
                //}
            }
        }
        return string.Join(",", dataEntries);
    }

    bool IsVisibleToCamera(GameObject target)
    {
        // 1. Get the center of the object's combined bounds
        Bounds bounds = GetFullBounds(target);
        Vector3 targetCenter = bounds.center;
        Vector3 cameraPos = targetCamera.transform.position;

        // 2. Calculate direction and distance
        Vector3 direction = targetCenter - cameraPos;
        float distance = direction.magnitude;

        RaycastHit hit;

        // 3. Cast the ray
        // We use a layer mask or check the hit object's identity
        if (Physics.Raycast(cameraPos, direction, out hit, distance))
        {
            // If the ray hits our target (or a child of our target), it's visible
            if (hit.transform.IsChildOf(target.transform) || hit.transform == target.transform)
            {
                return true;
            }
        }

        // If the ray hit a wall or something else first, it's occluded
        return false;
    }

    Rect GetScreenRect(Camera cam, Renderer renderer)
    {
        Bounds bounds = renderer.bounds;
        Vector3[] corners = new Vector3[8];

        // Define the 8 corners of the 3D bounding box
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
            // Convert world to viewport (0 to 1)
            Vector3 screenPoint = cam.WorldToViewportPoint(corner);
            minX = Mathf.Min(minX, screenPoint.x);
            minY = Mathf.Min(minY, screenPoint.y);
            maxX = Mathf.Max(maxX, screenPoint.x);
            maxY = Mathf.Max(maxY, screenPoint.y);
        }

        // Return as a Rect (x, y, width, height)
        return new Rect(minX, minY, maxX - minX, maxY - minY);
    }

    Rect GetScreenRectFromBounds(Camera cam, Bounds bounds)
    {
        Vector3[] corners = new Vector3[8];

        // Define the 8 corners of the 3D bounding box volume
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
            // Convert world to viewport (0 to 1)
            Vector3 screenPoint = cam.WorldToViewportPoint(corner);

            // We use Mathf.Clamp to keep the box within the camera frame 
            // even if the object is partially off-screen
            minX = Mathf.Min(minX, screenPoint.x);
            minY = Mathf.Min(minY, screenPoint.y);
            maxX = Mathf.Max(maxX, screenPoint.x);
            maxY = Mathf.Max(maxY, screenPoint.y);
        }

        // Return as a Rect (x, y, width, height) in 0-1 coordinate space
        return new Rect(minX, minY, maxX - minX, maxY - minY);
    }

    void Start()
    {
        // 1. Setup Render Texture
        rt = new RenderTexture(640, 480, 24, RenderTextureFormat.ARGB32);
        rt.Create();
        targetCamera.targetTexture = rt;

        // 2. Setup UDP
        udpClient = new UdpClient();
        remoteEndPoint = new IPEndPoint(IPAddress.Parse(ip), port);

        Debug.Log($"UDP Stream initialized to {ip}:{port}");
    }

    void Update()
    {
        timer += Time.deltaTime;
        if (timer >= (1f / targetFPS) && !isProcessing)
        {
            timer = 0f;
            isProcessing = true;
            string ids = GetVisibleObjectIds();

            Debug.Log(ids);
            AsyncGPUReadback.Request(rt, 0, TextureFormat.RGBA32, (request) =>
            {
                OnCompleteReadback(request, ids);
            });
        }

        // Testing
        //if (timer >= (1f / targetFPS))
        //{
        //    timer = 0f;
        //    isProcessing = true;
        //    string ids = GetVisibleObjectIds();

        //    Debug.Log("ids: " + ids);
        //}
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

        // 3. Create the final buffer
        byte[] finalPacket = new byte[4 + idBytes.Length + encodedBytes.Length];

        // 4. Copy data manually to avoid .ToArray() deallocation issues
        System.Buffer.BlockCopy(idLengthPrefix, 0, finalPacket, 0, 4);
        System.Buffer.BlockCopy(idBytes, 0, finalPacket, 4, idBytes.Length);

        // Use NativeArray's built-in CopyTo for the image data
        // This is faster and avoids the ObjectDisposedException
        byte[] managedEncoded = encodedBytes.ToArray();
        System.Buffer.BlockCopy(managedEncoded, 0, finalPacket, 4 + idBytes.Length, managedEncoded.Length);

        // 5. Clean up the NativeArray immediately after copying
        encodedBytes.Dispose();

        if (finalPacket.Length > 65000)
        {
            Debug.LogWarning($"Packet too large ({finalPacket.Length} bytes). Reduce quality or resolution.");
        }

        udpClient.BeginSend(finalPacket, finalPacket.Length, remoteEndPoint, (ar) =>
        {
            try
            {
                udpClient.EndSend(ar);
            }
            catch (System.Exception e)
            {
                Debug.LogError("UDP Send Error: " + e.Message);
            }
            finally
            {
                isProcessing = false;
            }

        }, null);

        //udpClient.BeginSend(bytesToSend, bytesToSend.Length, remoteEndPoint, (ar) => {
        //    try
        //    {
        //        udpClient.EndSend(ar);
        //    }
        //    catch (System.Exception e)
        //    {
        //        Debug.LogError("UDP Send Error: " + e.Message);
        //    }
        //    finally
        //    {
        //        isProcessing = false;
        //    }
        //}, null);
    }

    private static int GetLength(byte[] idBytes)
    {
        return idBytes.Length;
    }

    Bounds GetFullBounds(GameObject obj)
    {
        // Get all renderers in this object and all its children
        Renderer[] renderers = obj.GetComponentsInChildren<Renderer>();

        if (renderers.Length == 0) return new Bounds(obj.transform.position, Vector3.zero);

        // Start with the first renderer's bounds
        Bounds fullBounds = renderers[0].bounds;

        // Expand the bounds to include every other renderer found
        for (int i = 1; i < renderers.Length; i++)
        {
            fullBounds.Encapsulate(renderers[i].bounds);
        }

        return fullBounds;
    }

    Bounds GetAgentBounds(UnityEngine.AI.NavMeshAgent agent)
    {
        // Create a box based on the agent's cylinder height and radius
        return new Bounds(
            agent.transform.position + Vector3.up * (agent.height / 2f),
            new Vector3(agent.radius * 2, agent.height, agent.radius * 2)
        );
    }

    void OnDestroy()
    {
        udpClient?.Close();
    }
}