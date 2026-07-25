using System;

namespace SharpGPU
{
    /// <summary>
    /// Builds the backend-private root-signature variant used only by raster
    /// pipelines whose attachment interface contains local reads or ROV access.
    /// The caller-visible pipeline layout and its root-parameter numbering are
    /// left unchanged.
    /// </summary>
    internal static class Dx12RasterAttachmentRootSignature
    {
        internal static Vortice.Direct3D12.ID3D12RootSignature Create(
            Dx12Device device,
            Dx12PipelineLayout pipelineLayout,
            out uint attachmentRootParameterIndex)
        {
            ArgumentNullException.ThrowIfNull(device);
            ArgumentNullException.ThrowIfNull(pipelineLayout);
            if (pipelineLayout.IsLocalSignature)
            {
                throw new NotSupportedException(
                    "A DX12 local root signature cannot be used by a raster attachment interface.");
            }

            Dx12PipelineLayoutPlan plan = pipelineLayout.Plan;
            ulong rootDwordCost =
                (ulong)plan.DescriptorTableParameterCount +
                plan.PushConstantSize / 4u +
                1u;
            if (rootDwordCost > 64u)
            {
                throw new ArgumentException(
                    $"DX12 raster attachment root signature costs {rootDwordCost} DWORDs, exceeding the 64-DWORD limit.",
                    nameof(pipelineLayout));
            }

            attachmentRootParameterIndex =
                checked((uint)plan.TotalRootParameterCount);
            Vortice.Direct3D12.RootParameter1[] rootParameters =
                new Vortice.Direct3D12.RootParameter1[
                    checked(plan.TotalRootParameterCount + 1)];
            PopulateCallerParameters(plan, rootParameters);
            rootParameters[attachmentRootParameterIndex] =
                CreateAttachmentParameter();

            Vortice.Direct3D12.RootSignatureFlags flags =
                Vortice.Direct3D12.RootSignatureFlags.DenyHullShaderRootAccess |
                Vortice.Direct3D12.RootSignatureFlags.DenyDomainShaderRootAccess |
                Vortice.Direct3D12.RootSignatureFlags.DenyGeometryShaderRootAccess;
            if (pipelineLayout.UsesVertexLayout)
            {
                flags |=
                    Vortice.Direct3D12.RootSignatureFlags.AllowInputAssemblerInputLayout;
            }

            Vortice.Direct3D12.VersionedRootSignatureDescription description =
                new Vortice.Direct3D12.VersionedRootSignatureDescription(
                    new Vortice.Direct3D12.RootSignatureDescription1(
                        flags,
                        rootParameters,
                        Array.Empty<Vortice.Direct3D12.StaticSamplerDescription>()));

            Vortice.Direct3D.Blob? signature = null;
            try
            {
                string error =
                    Vortice.Direct3D12.D3D12.D3D12SerializeVersionedRootSignature(
                        description,
                        out signature);
                if (!string.IsNullOrWhiteSpace(error))
                {
                    throw new InvalidOperationException(
                        $"DX12 raster attachment root-signature serialization failed: {error}");
                }
                if (signature == null)
                {
                    throw new InvalidOperationException(
                        "DX12 raster attachment root-signature serialization returned no signature blob.");
                }

                SharpGen.Runtime.Result result =
                    device.NativeDevice.CreateRootSignature(
                        0,
                        signature.BufferPointer,
                        signature.BufferSize,
                        out Vortice.Direct3D12.ID3D12RootSignature? rootSignature);
                return Dx12Utility.RequireCreatedObject(
                    rootSignature,
                    result,
                    "ID3D12Device.CreateRootSignature(raster attachment interface)");
            }
            finally
            {
                signature?.Release();
            }
        }

        private static void PopulateCallerParameters(
            Dx12PipelineLayoutPlan plan,
            Vortice.Direct3D12.RootParameter1[] rootParameters)
        {
            foreach (Dx12PipelineArgumentTablePlan tablePlan in plan.TablePlans)
            {
                Dx12ArgumentTableLayout layout = tablePlan.Layout;
                for (int groupIndex = 0;
                     groupIndex < layout.Groups.Length;
                     ++groupIndex)
                {
                    Dx12ArgumentTableGroupPlan group = layout.Groups[groupIndex];
                    Vortice.Direct3D12.DescriptorRange1[] ranges =
                        new Vortice.Direct3D12.DescriptorRange1[
                            group.BindingIndices.Length];
                    for (int rangeIndex = 0;
                         rangeIndex < group.BindingIndices.Length;
                         ++rangeIndex)
                    {
                        ref readonly Dx12BindInfo bindInfo =
                            ref layout.BindInfos[
                                group.BindingIndices[rangeIndex]];
                        ranges[rangeIndex] =
                            new Vortice.Direct3D12.DescriptorRange1
                            {
                                RangeType = bindInfo.NativeRangeType,
                                NumDescriptors = bindInfo.Count,
                                BaseShaderRegister = bindInfo.Slot,
                                RegisterSpace = layout.Index,
                                Flags =
                                    Dx12Utility.GetDx12DescriptorRangeFlags(
                                        bindInfo.Type),
                                OffsetInDescriptorsFromTableStart =
                                    checked((uint)bindInfo.DescriptorOffset),
                            };
                    }

                    rootParameters[
                        tablePlan.RootParameterIndices[groupIndex]] =
                        new Vortice.Direct3D12.RootParameter1(
                            new Vortice.Direct3D12.RootDescriptorTable1(ranges),
                            group.Visibility);
                }
            }

            if (plan.PushConstantSize != 0)
            {
                rootParameters[plan.PushConstantRootParameterIndex] =
                    new Vortice.Direct3D12.RootParameter1(
                        new Vortice.Direct3D12.RootConstants(
                            0,
                            0,
                            plan.PushConstantSize / 4u),
                        Vortice.Direct3D12.ShaderVisibility.All);
            }
        }

        private static Vortice.Direct3D12.RootParameter1
            CreateAttachmentParameter()
        {
            Vortice.Direct3D12.DescriptorRange1[] ranges =
            {
                new Vortice.Direct3D12.DescriptorRange1
                {
                    RangeType =
                        Vortice.Direct3D12.DescriptorRangeType.ShaderResourceView,
                    NumDescriptors = RHIAttachmentIndexArray.MaxAttachments,
                    BaseShaderRegister = 0,
                    RegisterSpace =
                        Dx12PipelineLayoutPlan.AttachmentRegisterSpace,
                    Flags =
                        Vortice.Direct3D12.DescriptorRangeFlags
                            .DataStaticWhileSetAtExecute,
                    OffsetInDescriptorsFromTableStart = 0,
                },
                new Vortice.Direct3D12.DescriptorRange1
                {
                    RangeType =
                        Vortice.Direct3D12.DescriptorRangeType.UnorderedAccessView,
                    NumDescriptors = RHIAttachmentIndexArray.MaxAttachments,
                    BaseShaderRegister = 0,
                    RegisterSpace =
                        Dx12PipelineLayoutPlan.AttachmentRegisterSpace,
                    Flags =
                        Vortice.Direct3D12.DescriptorRangeFlags.DataVolatile,
                    OffsetInDescriptorsFromTableStart =
                        RHIAttachmentIndexArray.MaxAttachments,
                },
            };
            return new Vortice.Direct3D12.RootParameter1(
                new Vortice.Direct3D12.RootDescriptorTable1(ranges),
                Vortice.Direct3D12.ShaderVisibility.Pixel);
        }
    }
}
