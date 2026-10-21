using System;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using UnityEditor;
using UnityEngine;
using Debug = UnityEngine.Debug;

namespace MiniGameStudio.Editor
{
    // Packages each game under Assets/Games (game.ts compiled to game.js, plus its Assets folder as an AssetBundle)
    // and publishes it to the live service, which assigns the version players will see.
    public static class LivePublisher
    {
        const string GamesRoot = "Assets/Games";
        const string ToolsDir = "Tools";
        const string DefaultLiveUrl = "https://aliameen.com/petapp";
        const string TokenKey = "MiniGameStudio.PublishToken";

        static string LiveUrl => Environment.GetEnvironmentVariable("PETAPP_LIVE_URL") ?? DefaultLiveUrl;

        [Serializable]
        class GameInfo
        {
            public string id;
            public string title;
            public int sdk;
        }

        [Serializable]
        class PublishRequest
        {
            public string id;
            public string title;
            public int sdk;
            public PackageRef[] packages;
        }

        [Serializable]
        class PackageRef
        {
            public string platform;
            public string sha256;
        }

        [Serializable]
        class PublishResponse
        {
            public int version;
        }

        static readonly (string platform, BuildTarget target)[] Platforms =
        {
            ("ios", BuildTarget.iOS),
            ("osx", BuildTarget.StandaloneOSX)
        };

        [MenuItem("Mini-game/Publish to live")]
        public static void PublishAll()
        {
            var dirs = Directory.GetDirectories(GamesRoot).Where(d => File.Exists(Path.Combine(d, "game.json"))).ToArray();
            try
            {
                if (dirs.Length == 0) throw new Exception($"No game.json found under {GamesRoot}");
                var token = Token();
                AssetDatabase.SaveAssets();
                var summary = string.Join("\n", dirs.Select(d => Publish(d, token)));
                Debug.Log(summary);
                if (!Application.isBatchMode) EditorUtility.DisplayDialog("Published", summary, "OK");
            }
            catch (OperationCanceledException)
            {
            }
            catch (Exception e) when (!Application.isBatchMode)
            {
                Debug.LogException(e);
                EditorUtility.DisplayDialog("Publish failed", e.Message, "OK");
            }
            finally
            {
                EditorUtility.ClearProgressBar();
            }
        }

        // Kept only in memory for this editor session (SessionState is cleared when Unity quits), never written to disk.
        static string Token()
        {
            var fromEnvironment = Environment.GetEnvironmentVariable("PETAPP_PUBLISH_TOKEN");
            if (!string.IsNullOrEmpty(fromEnvironment)) return fromEnvironment;
            var remembered = SessionState.GetString(TokenKey, "");
            if (remembered != "") return remembered;
            if (Application.isBatchMode) throw new Exception("Set PETAPP_PUBLISH_TOKEN to publish from the command line");
            var entered = PublishTokenWindow.Ask() ?? throw new OperationCanceledException();
            SessionState.SetString(TokenKey, entered);
            return entered;
        }

        static string Publish(string dir, string token)
        {
            var info = JsonUtility.FromJson<GameInfo>(File.ReadAllText(Path.Combine(dir, "game.json")));
            var build = Path.Combine("Library", "LiveBuild", info.id);
            if (Directory.Exists(build)) Directory.Delete(build, true);
            Directory.CreateDirectory(build);
            var steps = Platforms.Length * 2 + 2f;

            Progress(info, "Compiling game.ts", 0f);
            var assets = Directory.GetFiles(Path.Combine(dir, "Assets"))
                .Where(f => !f.EndsWith(".meta"))
                .Select(f => f.Replace('\\', '/'))
                .ToArray();
            WriteAssetKeys(dir, assets);
            var js = CompileTypeScript(dir, build);

            using var http = new HttpClient { Timeout = TimeSpan.FromMinutes(5) };
            http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

            var packages = Platforms.Select((p, i) =>
            {
                Progress(info, $"Building {p.platform} assets", (2 * i + 1) / steps);
                var zip = BuildPackage(build, p.platform, p.target, js, assets);
                var sha256 = Sha256(zip);

                Progress(info, $"Uploading {p.platform} package ({zip.Length / 1024f / 1024f:0.0} MB)", (2 * i + 2) / steps);
                var content = new ByteArrayContent(zip);
                content.Headers.ContentType = new MediaTypeHeaderValue("application/zip");
                Send(http, new HttpRequestMessage(HttpMethod.Put, $"{LiveUrl}/api/packages/{sha256}") { Content = content });
                return new PackageRef { platform = p.platform, sha256 = sha256 };
            }).ToArray();

            Progress(info, "Publishing", (steps - 1) / steps);
            var request = new PublishRequest { id = info.id, title = info.title, sdk = info.sdk, packages = packages };
            var body = new StringContent(JsonUtility.ToJson(request), Encoding.UTF8, "application/json");
            var response = Send(http, new HttpRequestMessage(HttpMethod.Post, $"{LiveUrl}/api/publish") { Content = body });
            return $"{info.title} v{JsonUtility.FromJson<PublishResponse>(response).version} is live.";
        }

        static byte[] BuildPackage(string build, string platform, BuildTarget target, string js, string[] assets)
        {
            var bundleDir = Path.Combine(build, platform);
            Directory.CreateDirectory(bundleDir);
            var bundles = new[] { new AssetBundleBuild { assetBundleName = "assets.bundle", assetNames = assets } };
            if (BuildPipeline.BuildAssetBundles(bundleDir, bundles, BuildAssetBundleOptions.ChunkBasedCompression, target) == null)
                throw new Exception($"Asset bundle build failed ({platform})");

            using var zipBytes = new MemoryStream();
            using (var zip = new ZipArchive(zipBytes, ZipArchiveMode.Create, true))
            {
                zip.CreateEntryFromFile(js, "game.js");
                zip.CreateEntryFromFile(Path.Combine(bundleDir, "assets.bundle"), "assets.bundle");
            }
            return zipBytes.ToArray();
        }

        static string Send(HttpClient http, HttpRequestMessage request)
        {
            HttpResponseMessage response;
            try
            {
                response = http.SendAsync(request).GetAwaiter().GetResult();
            }
            catch (HttpRequestException e)
            {
                throw new Exception($"Live service not reachable at {LiveUrl}: {e.Message}");
            }
            var text = response.Content.ReadAsStringAsync().GetAwaiter().GetResult();
            if (response.StatusCode == HttpStatusCode.Unauthorized)
            {
                SessionState.EraseString(TokenKey);
                throw new Exception("The publish token was rejected. Publish again to enter it.");
            }
            if (!response.IsSuccessStatusCode) throw new Exception($"Live service refused {request.Method} {request.RequestUri.AbsolutePath} ({(int)response.StatusCode}): {text}");
            return text;
        }

        static string Sha256(byte[] bytes)
        {
            using var sha = SHA256.Create();
            return BitConverter.ToString(sha.ComputeHash(bytes)).Replace("-", "").ToLowerInvariant();
        }

        // Generates the AssetKey union so a typo like sdk.spawn("bmob") fails at compile time.
        static void WriteAssetKeys(string dir, string[] assets)
        {
            var keys = assets.Select(a => $"\"{Path.GetFileNameWithoutExtension(a)}\"").Distinct();
            File.WriteAllText(Path.Combine(dir, "assets.d.ts"), $"type AssetKey = {string.Join(" | ", keys)};\n");
        }

        static string CompileTypeScript(string dir, string outDir)
        {
            var node = new[] { "/opt/homebrew/bin/node", "/usr/local/bin/node" }.FirstOrDefault(File.Exists)
                       ?? throw new Exception("Node.js not found; install it with `brew install node`");
            if (!Directory.Exists(Path.Combine(ToolsDir, "node_modules")))
                Run(Path.Combine(Path.GetDirectoryName(node), "npm"), "install --no-audit --no-fund", ToolsDir, node);

            var tsc = Path.GetFullPath(Path.Combine(ToolsDir, "node_modules", "typescript", "bin", "tsc"));
            var project = Path.GetFullPath(Path.Combine(dir, "tsconfig.json"));
            // Same tsconfig the IDE uses, so what type-checks in the editor is exactly what ships.
            Run(node, $"\"{tsc}\" -p \"{project}\" --noEmit false --noEmitOnError --outDir \"{Path.GetFullPath(outDir)}\"", ".", node);
            return Path.Combine(outDir, "game.js");
        }

        static void Run(string file, string args, string workDir, string node)
        {
            var info = new ProcessStartInfo(file, args)
            {
                WorkingDirectory = workDir,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true
            };
            info.EnvironmentVariables["PATH"] = Path.GetDirectoryName(node) + ":/usr/bin:/bin";
            using var p = Process.Start(info);
            var output = p.StandardOutput.ReadToEnd() + p.StandardError.ReadToEnd();
            p.WaitForExit();
            if (p.ExitCode != 0) throw new Exception($"{Path.GetFileName(file)} failed:\n{output}");
        }

        static void Progress(GameInfo info, string step, float t) =>
            EditorUtility.DisplayProgressBar($"Publishing {info.title}", step, t);
    }

    sealed class PublishTokenWindow : EditorWindow
    {
        string token = "";
        bool submitted;

        public static string Ask()
        {
            var window = CreateInstance<PublishTokenWindow>();
            window.titleContent = new GUIContent("Publish to live");
            window.minSize = window.maxSize = new Vector2(440, 120);
            window.ShowModalUtility();
            return window.submitted ? window.token.Trim() : null;
        }

        void OnGUI()
        {
            var submit = Event.current.type == EventType.KeyDown && Event.current.keyCode is KeyCode.Return or KeyCode.KeypadEnter;
            EditorGUILayout.LabelField("Paste the PUBLISH_TOKEN set on the petapp-live Worker. It is kept until Unity quits.", EditorStyles.wordWrappedLabel);
            GUI.SetNextControlName("token");
            token = EditorGUILayout.PasswordField(token);
            EditorGUI.FocusTextInControl("token");
            GUILayout.FlexibleSpace();
            using (new EditorGUILayout.HorizontalScope())
            {
                GUILayout.FlexibleSpace();
                if (GUILayout.Button("Cancel", GUILayout.Width(90))) Close();
                using (new EditorGUI.DisabledScope(string.IsNullOrWhiteSpace(token)))
                    submit |= GUILayout.Button("Publish", GUILayout.Width(90));
            }
            if (submit && !string.IsNullOrWhiteSpace(token))
            {
                submitted = true;
                Close();
            }
        }
    }
}
