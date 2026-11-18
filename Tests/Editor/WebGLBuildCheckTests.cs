using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using UnityEditor;

namespace susaplay.SDK.Editor.Tests
{
    public sealed class WebGLBuildCheckTests
    {
        private static readonly string[] None = new string[0];

        [Test]
        public void DxtIsAnError()
        {
            var findings = WebGLBuildCheck.Evaluate(WebGLTextureSubtarget.DXT, None);

            Assert.AreEqual("WG001", findings.Single().Id);
            Assert.AreEqual(WebGLBuildCheck.Severity.Error, findings.Single().Severity);
        }

        [Test]
        public void PlayerSettingsCompressionIsAWarning()
        {
            var findings = WebGLBuildCheck.Evaluate(WebGLTextureSubtarget.Generic, None);

            Assert.AreEqual("WG002", findings.Single().Id);
            Assert.AreEqual(WebGLBuildCheck.Severity.Warning, findings.Single().Severity);
        }

        [Test]
        public void MobileFormatsPass()
        {
            Assert.IsEmpty(WebGLBuildCheck.Evaluate(WebGLTextureSubtarget.ASTC, None));
            Assert.IsEmpty(WebGLBuildCheck.Evaluate(WebGLTextureSubtarget.ETC2, None));
        }

        [Test]
        public void DesktopOnlyOverridesAreListed()
        {
            var textures = Enumerable.Range(1, 12).Select(index => $"Assets/t{index}.png").ToList();

            var finding = WebGLBuildCheck.Evaluate(WebGLTextureSubtarget.ASTC, textures).Single();

            Assert.AreEqual("WG003", finding.Id);
            StringAssert.Contains("12 texture(s)", finding.Message);
            StringAssert.Contains("Assets/t10.png", finding.Message);
            StringAssert.DoesNotContain("Assets/t11.png", finding.Message);
            StringAssert.Contains("and 2 more", finding.Message);
        }

        [Test]
        public void OnlyDxtAndBcFormatsCount()
        {
            Assert.IsTrue(WebGLBuildCheck.DesktopOnlyFormats.Contains(TextureImporterFormat.DXT5));
            Assert.IsTrue(WebGLBuildCheck.DesktopOnlyFormats.Contains(TextureImporterFormat.BC7));
            Assert.IsFalse(WebGLBuildCheck.DesktopOnlyFormats.Contains(TextureImporterFormat.ASTC_6x6));
            Assert.IsFalse(WebGLBuildCheck.DesktopOnlyFormats.Contains(TextureImporterFormat.ETC2_RGBA8));
        }
    }
}
