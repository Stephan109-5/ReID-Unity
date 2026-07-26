using System;
using System.Collections.Generic;
using System.Collections;
using System.Text;
using UnityEngine;
using Unity.WebRTC;
using UnityEngine.Networking;

public class WebRTCCameraStreamer : MonoBehaviour
{
    [Serializable]
    private class CameraPacketMetadata
    {
        public string camera_id;
        public int frame_id;
        public long timestamp_utc_ms;
        public string[] object_ids;
    }

    [Header("Camera & Identity Configuration")]
    public Camera targetCamera;
    public string cameraId = "cam_0";
    public int resolutionWidth = 1280;
    public int resolutionHeight = 720;

    [Header("Dynamic Network Signaling")]
    [Tooltip("The IP address where the Python signaling server is running.")]
    public string signalingHost = "127.0.0.1";
    [Tooltip("The unique port assigned to this specific camera worker's instance.")]
    public int signalingPort = 8080;

    // Constructed at runtime
    private string calculatedSignalingUrl;

    // WebRTC Pipeline Components
    private RTCPeerConnection peerConnection;
    private VideoStreamTrack videoTrack;
    private RTCDataChannel dataChannel;
    private RenderTexture streamRT;

    private bool isHandshakeComplete = false;
    private int frameId = 0;

    void Start()
    {
        // Programmatically generate the unique endpoint for this component instance
        calculatedSignalingUrl = $"http://{signalingHost}:{signalingPort}/offer";
        Debug.Log($"[{cameraId}] Initialized WebRTC client targeting: {calculatedSignalingUrl}");

        // 1. Start the native WebRTC frame pump loop
        StartCoroutine(WebRTC.Update());
        StartWebRTC();
    }

    void StartWebRTC()
    {
        RTCConfiguration config = default;
        peerConnection = new RTCPeerConnection(ref config);

        // 2. Setup the Metadata Data Channel
        var channelOptions = new RTCDataChannelInit();
        dataChannel = peerConnection.CreateDataChannel("metadata-stream", channelOptions);

        dataChannel.OnOpen = () => Debug.Log($"[{cameraId}] Metadata Data Channel connected with Python!");
        dataChannel.OnClose = () => { isHandshakeComplete = false; Debug.Log($"[{cameraId}] Data channel closed."); };

        // 3. Configure RenderTexture with WebRTC supported format
        var gfxType = SystemInfo.graphicsDeviceType;
        var format = WebRTC.GetSupportedRenderTextureFormat(gfxType);

        streamRT = new RenderTexture(resolutionWidth, resolutionHeight, 24, format);
        streamRT.Create();
        targetCamera.targetTexture = streamRT;

        // 4. Create Video Track and register to the peer
        videoTrack = new VideoStreamTrack(streamRT);
        RTCRtpSender sender = peerConnection.AddTrack(videoTrack);

        //var parameters = sender.GetParameters();
        //foreach (var encoding in parameters.encodings)
        //{
        //    // Set the bitrates in bits-per-second (bps)
        //    encoding.minBitrate = 1_000_000; // Force a minimum of 5 Mbps
        //    encoding.maxBitrate = 5_000_000; // Allow up to 20 Mbps crisp quality
        //    encoding.maxFramerate = 30;       // Lock it to smooth 60fps if desired
        //}

        // 5. Negotiate Network Handshake via Dynamic HTTP Signaling
        StartCoroutine(CreateOffer());
    }

    void Update()
    {
        if (Time.timeScale <= 0) return;

        // 6. Push Ground-Truth Metadata side-by-side over UDP
        if (dataChannel != null && dataChannel.ReadyState == RTCDataChannelState.Open)
        {
            string[] objectIds = GetVisibleObjectIds();

            CameraPacketMetadata metadata = new CameraPacketMetadata
            {
                camera_id = cameraId,
                frame_id = frameId++,
                timestamp_utc_ms = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
                object_ids = objectIds
            };

            string jsonString = JsonUtility.ToJson(metadata);
            dataChannel.Send(jsonString);
        }
    }

    // --- Legacy Bounding Box Engine (Kept untouched for logic accuracy) ---
    string[] GetVisibleObjectIds()
    {
        Plane[] planes = GeometryUtility.CalculateFrustumPlanes(targetCamera);
        GameObject[] allObjects = GameObject.FindGameObjectsWithTag("Character");
        List<string> dataEntries = new List<string>();

        foreach (GameObject obj in allObjects)
        {
            SkinnedMeshRenderer[] allParts = obj.GetComponentsInChildren<SkinnedMeshRenderer>();
            if (allParts.Length == 0) continue;

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
            if (!IsObjectVisibleViaRaycast(obj)) continue;

            Rect rect = Get2DBoundingBox(targetCamera, allParts);
            if (rect.width * rect.height <= 0.001f) continue;

            string entry = $"{obj.name}|{rect.x:F3}|{rect.y:F3}|{rect.width:F3}|{rect.height:F3}";
            dataEntries.Add(entry);
        }

        return dataEntries.ToArray();
    }

    Rect Get2DBoundingBox(Camera cam, SkinnedMeshRenderer[] parts)
    {
        float minX = float.MaxValue, maxX = float.MinValue;
        float minY = float.MaxValue, maxY = float.MinValue;

        foreach (var smr in parts)
        {
            foreach (Transform bone in smr.bones)
            {
                Vector3 worldPos = bone.position;
                UpdateMinMax(worldPos, cam, ref minX, ref minY, ref maxX, ref maxY);

                if (bone.name.ToLower().Contains("head"))
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
        if (screenPt.z < 0) return;

        minX = Mathf.Min(minX, screenPt.x);
        minY = Mathf.Min(minY, screenPt.y);
        maxX = Mathf.Max(maxX, screenPt.x);
        maxY = Mathf.Max(maxY, screenPt.y);
    }

    bool IsObjectVisibleViaRaycast(GameObject targetObj)
    {
        Vector3 startPos = targetCamera.transform.position;
        Vector3 targetPos = targetObj.transform.position + Vector3.up * 1.0f;

        Vector3 direction = targetPos - startPos;
        float distance = direction.magnitude;

        RaycastHit hit;
        if (Physics.Raycast(startPos, direction, out hit, distance))
        {
            if (hit.transform != targetObj.transform && !hit.transform.IsChildOf(targetObj.transform))
            {
                return false;
            }
        }
        return true;
    }

    private IEnumerator CreateOffer()
    {
        var op = peerConnection.CreateOffer();
        yield return op;

        if (!op.IsError)
        {
            var offer = op.Desc;
            var opLocal = peerConnection.SetLocalDescription(ref offer);
            yield return opLocal;

            StartCoroutine(PostSignalingOffer(offer.sdp));
        }
    }

    private IEnumerator PostSignalingOffer(string sdpString)
    {
        string jsonPayload = $"{{\"sdp\": \"{sdpString.Replace("\r\n", "\\r\\n")}\", \"type\": \"offer\"}}";

        // Using the dynamically calculated configuration URL string
        using (UnityWebRequest request = new UnityWebRequest(calculatedSignalingUrl, "POST"))
        {
            byte[] bodyRaw = Encoding.UTF8.GetBytes(jsonPayload);
            request.uploadHandler = new UploadHandlerRaw(bodyRaw);
            request.downloadHandler = new DownloadHandlerBuffer();
            request.SetRequestHeader("Content-Type", "application/json");

            yield return request.SendWebRequest();

            if (request.result == UnityWebRequest.Result.Success)
            {
                SignalingResponse response = JsonUtility.FromJson<SignalingResponse>(request.downloadHandler.text);

                RTCSessionDescription answer = new RTCSessionDescription
                {
                    type = RTCSdpType.Answer,
                    sdp = response.sdp
                };

                var opRemote = peerConnection.SetRemoteDescription(ref answer);
                yield return opRemote;

                isHandshakeComplete = true;
            }
            else
            {
                Debug.LogError($"[{cameraId}] Signaling error on {calculatedSignalingUrl}: {request.error}");
            }
        }
    }

    [System.Serializable]
    private class SignalingResponse { public string sdp; public string type; }

    private void OnDestroy()
    {
        if (targetCamera != null) targetCamera.targetTexture = null;
        if (streamRT != null) { streamRT.Release(); Destroy(streamRT); }

        dataChannel?.Dispose();
        videoTrack?.Dispose();
        peerConnection?.Close();
        peerConnection?.Dispose();
    }
}