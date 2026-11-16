using UnityEditor;
using UnityEngine;

// Pengaturan import otomatis untuk aset game stealth.
public class StealthAssetPostprocessor : AssetPostprocessor
{
    private static readonly string[] LoopingClips = { "Idle", "Run", "Run_Gun", "Idle_Shoot", "Duck" };

    private void OnPreprocessTexture()
    {
        TextureImporter importer = (TextureImporter)assetImporter;

        if (assetPath.StartsWith("Assets/Resources/UI/"))
        {
            importer.textureType = TextureImporterType.Default;
            importer.alphaIsTransparency = true;
            importer.mipmapEnabled = false;
            importer.wrapMode = TextureWrapMode.Clamp;
            importer.filterMode = FilterMode.Bilinear;
            importer.npotScale = TextureImporterNPOTScale.None;
            importer.textureCompression = TextureImporterCompression.Uncompressed;
        }
        else if (assetPath.StartsWith("Assets/Textures/Environment/"))
        {
            importer.wrapMode = TextureWrapMode.Repeat;
            importer.anisoLevel = 4;

            if (assetPath.EndsWith("_n.png"))
                importer.textureType = TextureImporterType.NormalMap;
        }
    }

    private void OnPreprocessModel()
    {
        if (!assetPath.Contains("/ToonShooter/Characters/"))
            return;

        ModelImporter importer = (ModelImporter)assetImporter;
        importer.animationType = ModelImporterAnimationType.Generic;
        importer.importAnimation = true;
    }

    // Set loop untuk clip idle/lari agar animasi patroli tidak berhenti.
    private void OnPreprocessAnimation()
    {
        if (!assetPath.Contains("/ToonShooter/Characters/"))
            return;

        ModelImporter importer = (ModelImporter)assetImporter;
        ModelImporterClipAnimation[] clips = importer.defaultClipAnimations;

        foreach (ModelImporterClipAnimation clip in clips)
        {
            string shortName = clip.name.Contains("|") ? clip.name.Substring(clip.name.LastIndexOf('|') + 1) : clip.name;
            clip.name = shortName;
            clip.loopTime = System.Array.IndexOf(LoopingClips, shortName) >= 0;
        }

        importer.clipAnimations = clips;
    }
}
