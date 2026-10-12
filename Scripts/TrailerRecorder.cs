using System.Collections;
using System.IO;
using Unity.Collections;
using UnityEngine;

// Editor tool for the trailer: records the game at a fixed frame rate (game time steps exactly 1/fps per frame,
// however slowly the frames are actually rendered) into one MJPEG file + one raw float audio file per take.
//   Captures/trailer/<take>.mjpeg   - 1280x720 JPEG frames back to back (ffmpeg -f mjpeg reads it)
//   Captures/trailer/<take>.f32     - interleaved float samples from AudioRenderer (rate/channels in <take>.txt)
// A perFrame callback gets the take time in seconds every frame, so a take can move the camera and fire events.
public class TrailerRecorder : MonoBehaviour
{
    public static TrailerRecorder I;
    public static bool Busy;
    public static string Status = "";

    public static void Begin(string take, float seconds, System.Action<float> perFrame, int fps = 30, bool audio = true)
    {
        if (!I) { var g = new GameObject("TrailerRecorder"); DontDestroyOnLoad(g); I = g.AddComponent<TrailerRecorder>(); }
        I.StopAllCoroutines();
        I.StartCoroutine(I.Rec(take, seconds, perFrame, fps, audio));
    }

    IEnumerator Rec(string take, float seconds, System.Action<float> perFrame, int fps, bool audio)
    {
        Busy = true;
        Directory.CreateDirectory("Captures/trailer");
        string basePath = "Captures/trailer/" + take;
        int frames = Mathf.RoundToInt(seconds * fps);
        const int W = 1280, H = 720;
        var rt = new RenderTexture(W, H, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB);
        var tex = new Texture2D(W, H, TextureFormat.RGB24, false);
        Time.captureFramerate = fps;
        int channels = AudioSettings.speakerMode == AudioSpeakerMode.Mono ? 1 : 2;
        if (audio) AudioRenderer.Start();
        using (var video = new FileStream(basePath + ".mjpeg", FileMode.Create))
        using (var sound = new BinaryWriter(new FileStream(basePath + ".f32", FileMode.Create)))
        {
            for (int i = 0; i < frames; i++)
            {
                try { perFrame?.Invoke(i / (float)fps); }
                catch (System.Exception e) { Status = take + " callback error: " + e.Message; }
                yield return new WaitForEndOfFrame();
                var shot = ScreenCapture.CaptureScreenshotAsTexture();
                Graphics.Blit(shot, rt);
                var prev = RenderTexture.active; RenderTexture.active = rt;
                tex.ReadPixels(new Rect(0, 0, W, H), 0, 0); tex.Apply(false);
                RenderTexture.active = prev;
                Destroy(shot);
                var jpg = tex.EncodeToJPG(85);
                video.Write(jpg, 0, jpg.Length);
                if (audio)
                {
                    int n = AudioRenderer.GetSampleCountForCaptureFrame();
                    var buf = new NativeArray<float>(n * channels, Allocator.Temp);
                    AudioRenderer.Render(buf);
                    for (int k = 0; k < buf.Length; k++) sound.Write(buf[k]);
                    buf.Dispose();
                }
                Status = take + " " + (i + 1) + "/" + frames;
            }
        }
        if (audio) AudioRenderer.Stop();
        File.WriteAllText(basePath + ".txt", "fps=" + fps + "\nrate=" + AudioSettings.outputSampleRate + "\nchannels=" + channels + "\nframes=" + frames);
        Time.captureFramerate = 0;
        rt.Release(); Destroy(tex);
        Busy = false;
        Status = take + " done " + frames;
    }
}
