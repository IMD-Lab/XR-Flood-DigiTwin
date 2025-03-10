﻿using System;
using System.Collections;
using TriLibCore.General;
using TriLibCore.Utils;
using UnityEngine;

namespace TriLibCore.Mappers
{
    [Serializable]
    [CreateAssetMenu(menuName = "TriLib/Mappers/Material/Autodesk Interactive Standard Material Mapper", fileName = "AutodeskInteractiveStandardMaterialMapper")]
    public class AutodeskInteractiveStandardMaterialMapper : StandardMaterialMapper
    {
        public override bool UseShaderVariantCollection => true;

        public override Material MaterialPreset
        {
            get
            {
                if (GraphicsSettingsUtils.IsUsingHDRPPipeline)
                {
                    return Resources.Load<Material>("Materials/AutodeskInteractive/HDRP/AutodeskInteractive");
                }
                if (GraphicsSettingsUtils.IsUsingUniversalPipeline)
                {
                    return Resources.Load<Material>("Materials/AutodeskInteractive/UniversalRP/AutodeskInteractive");
                }
                return Resources.Load<Material>("Materials/AutodeskInteractive/Standard/AutodeskInteractive");
            }
        }

        public override Material LoadingMaterial => MaterialPreset;

        public override Material CutoutMaterialPreset => MaterialPreset;

        public override Material TransparentComposeMaterialPreset => MaterialPreset;

        public override Material TransparentMaterialPreset => MaterialPreset;

        public override Material MaterialPresetNoMetallicTexture => MaterialPreset;

        public override Material CutoutMaterialPresetNoMetallicTexture => MaterialPreset;

        public override Material TransparentMaterialPresetNoMetallicTexture => MaterialPreset;

        public override Material TransparentComposeMaterialPresetNoMetallicTexture => MaterialPreset;

        public override bool ExtractMetallicAndSmoothness => true;

        public override bool IsCompatible(MaterialMapperContext materialMapperContext)
        {
            return (materialMapperContext == null || materialMapperContext.Material?.UsesRoughnessSetup == true);
        }

        protected override IEnumerable ApplyGlossinessMapTexture(TextureLoadingContext textureLoadingContext)
        {
            if (textureLoadingContext.UnityTexture != null)
            {
                textureLoadingContext.Context.AddUsedTexture(textureLoadingContext.UnityTexture);
            }
            textureLoadingContext.MaterialMapperContext.VirtualMaterial.SetProperty("_SpecGlossMap", textureLoadingContext.UnityTexture, GenericMaterialProperty.MetallicMap);
            if (textureLoadingContext.UnityTexture != null)
            {
                textureLoadingContext.MaterialMapperContext.VirtualMaterial.EnableKeyword("_SPECGLOSSMAP");
            }
            else
            {
                textureLoadingContext.MaterialMapperContext.VirtualMaterial.DisableKeyword("_SPECGLOSSMAP");
            }
            yield break;
        }
    }
}