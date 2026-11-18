using System.Collections.Generic;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEngine;

namespace susaplay.SDK.Editor
{
    // Settings that make a WebGL build crash on phones. Phone GPUs cannot read DXT/BC textures, so
    // the browser expands every one of them to uncompressed RGBA in memory — several times its
    // size — and a phone's browser tab is killed once that pushes it over its memory limit. The
    // build itself works on a desktop, so nothing shows the problem until players report it.
    internal static class WebGLBuildCheck
    {
        internal enum Severity
        {
            Warning,
            Error,
        }

        internal readonly struct Finding
        {
            public readonly string Id;
            public readonly Severity Severity;
            public readonly string Message;

            public Finding(string id, Severity severity, string message)
            {
                Id = id;
                Severity = severity;
                Message = message;
            }

            public override string ToString() => $"[SusaPlay] {Id}: {Message}";
        }

        // Texture formats only desktop GPUs decode: DXT (S3TC) and the BC family.
        internal static readonly HashSet<TextureImporterFormat> DesktopOnlyFormats = new HashSet<TextureImporterFormat>
        {
            TextureImporterFormat.DXT1,
            TextureImporterFormat.DXT5,
            TextureImporterFormat.DXT1Crunched,
            TextureImporterFormat.DXT5Crunched,
            TextureImporterFormat.BC4,
            TextureImporterFormat.BC5,
            TextureImporterFormat.BC6H,
            TextureImporterFormat.BC7,
        };

        private const int ListedTextures = 10;

        internal static List<Finding> Evaluate(WebGLTextureSubtarget subtarget, IReadOnlyList<string> desktopOnlyTextures)
        {
            var findings = new List<Finding>();

            if (subtarget == WebGLTextureSubtarget.DXT)
            {
                findings.Add(new Finding(
                    "WG001",
                    Severity.Error,
                    "Texture Compression is DXT. Phones cannot use DXT, so every texture is expanded to "
                    + "uncompressed RGBA in memory and the browser tab can crash on load. Set Texture "
                    + "Compression to ASTC in Build Settings (or the WebGL Build Profile)."));
            }
            else if (subtarget == WebGLTextureSubtarget.Generic)
            {
                findings.Add(new Finding(
                    "WG002",
                    Severity.Warning,
                    "Texture Compression is \"Use Player Settings\", which for WebGL means DXT unless each "
                    + "texture overrides it. Phones cannot use DXT. Set Texture Compression to ASTC in "
                    + "Build Settings (or the WebGL Build Profile)."));
            }

            if (desktopOnlyTextures.Count > 0)
            {
                var listed = string.Join(", ", desktopOnlyTextures.Take(ListedTextures));
                var more = desktopOnlyTextures.Count > ListedTextures
                    ? $" and {desktopOnlyTextures.Count - ListedTextures} more"
                    : string.Empty;
                findings.Add(new Finding(
                    "WG003",
                    Severity.Warning,
                    $"{desktopOnlyTextures.Count} texture(s) override the WebGL format with DXT/BC, which "
                    + $"phones expand to uncompressed RGBA: {listed}{more}. Remove the WebGL override or "
                    + "choose an ASTC format."));
            }

            return findings;
        }

        internal static List<string> FindDesktopOnlyTextures()
        {
            var paths = new List<string>();
            foreach (var guid in AssetDatabase.FindAssets("t:Texture", new[] { "Assets" }))
            {
                var path = AssetDatabase.GUIDToAssetPath(guid);
                if (!(AssetImporter.GetAtPath(path) is TextureImporter importer))
                {
                    continue;
                }

                var settings = importer.GetPlatformTextureSettings("WebGL");
                if (settings.overridden && DesktopOnlyFormats.Contains(settings.format))
                {
                    paths.Add(path);
                }
            }

            return paths;
        }

        internal static List<Finding> Run()
        {
            return Evaluate(EditorUserBuildSettings.webGLBuildSubtarget, FindDesktopOnlyTextures());
        }

        internal static void Log(IEnumerable<Finding> findings)
        {
            foreach (var finding in findings)
            {
                if (finding.Severity == Severity.Error)
                {
                    Debug.LogError(finding.ToString());
                }
                else
                {
                    Debug.LogWarning(finding.ToString());
                }
            }
        }

        [MenuItem("SusaPlay/Check WebGL Build")]
        private static void CheckFromMenu()
        {
            var findings = Run();
            Log(findings);
            var message = findings.Count == 0
                ? "No problems found."
                : string.Join("\n\n", findings.Select(finding => $"{finding.Id}: {finding.Message}"));
            EditorUtility.DisplayDialog("SusaPlay WebGL build check", message, "OK");
        }
    }

    // Runs the check before every WebGL build. An error asks before building in the Editor; batch
    // builds (CI) only log, so a pipeline is never stopped by a dialog nobody can answer.
    internal sealed class WebGLBuildCheckPreprocess : IPreprocessBuildWithReport
    {
        public int callbackOrder => 0;

        public void OnPreprocessBuild(BuildReport report)
        {
            if (report.summary.platform != BuildTarget.WebGL)
            {
                return;
            }

            var findings = WebGLBuildCheck.Run();
            WebGLBuildCheck.Log(findings);

            var errors = findings.Where(finding => finding.Severity == WebGLBuildCheck.Severity.Error).ToList();
            if (errors.Count == 0 || Application.isBatchMode)
            {
                return;
            }

            var message = new StringBuilder();
            foreach (var error in errors)
            {
                message.Append(error.Id).Append(": ").Append(error.Message).Append("\n\n");
            }
            message.Append("Build anyway?");

            if (!EditorUtility.DisplayDialog(
                    "SusaPlay: this build may crash on phones",
                    message.ToString(),
                    "Build anyway",
                    "Cancel build"))
            {
                throw new BuildFailedException("Cancelled by the SusaPlay WebGL build check.");
            }
        }
    }
}
