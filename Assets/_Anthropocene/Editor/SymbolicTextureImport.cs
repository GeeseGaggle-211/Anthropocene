using UnityEditor;

namespace Anthropocene.Editor
{
    public sealed class SymbolicTextureImport : AssetPostprocessor
    {
        private void OnPreprocessTexture()
        {
            if (!assetPath.StartsWith("Assets/_Anthropocene/Resources/MapArt/") || !assetPath.EndsWith(".png")) return;
            var importer = (TextureImporter)assetImporter;
            importer.textureType = TextureImporterType.Sprite; importer.spriteImportMode = SpriteImportMode.Single;
            importer.alphaIsTransparency = true; importer.mipmapEnabled = false; importer.maxTextureSize = 256;
            importer.textureCompression = TextureImporterCompression.Uncompressed;
        }
    }
}
