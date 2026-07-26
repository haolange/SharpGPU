using System;
using SharpGPU;
using Xunit;

namespace SharpGPU.Conformance.Tests
{
    public sealed class VulkanBindingTablePlanTests
    {
        [Fact]
        public void Plan_ShouldMapLogicalNamespacesToDensePhysicalBindings()
        {
            VulkanBindingTablePlan plan = CreatePlan(
                3,
                AllFeatures(),
                GenerousLimits(),
                Element(41, ERHIBindType.Buffer),
                Element(41, ERHIBindType.Texture2D),
                Element(41, ERHIBindType.Sampler));

            Assert.Equal(3, plan.BindInfos.Length);
            Assert.Equal(new uint[] { 0, 1, 2 },
                Array.ConvertAll(
                    plan.BindInfos,
                    binding => binding.PhysicalBinding));
            Assert.Equal(0, plan.ResolveBinding(41, ERHIBindType.Buffer));
            Assert.Equal(1, plan.ResolveBinding(41, ERHIBindType.Texture2D));
            Assert.Equal(2, plan.ResolveBinding(41, ERHIBindType.Sampler));
            Assert.Equal(1u, plan.PoolRequirements.StorageBuffers);
            Assert.Equal(1u, plan.PoolRequirements.SampledImages);
            Assert.Equal(1u, plan.PoolRequirements.Samplers);
        }

        [Fact]
        public void Plan_ShouldRejectUnknownInvalidAndAmbiguousBindings()
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => CreatePlan(
                8,
                AllFeatures(),
                GenerousLimits(),
                Element(0, ERHIBindType.Buffer)));
            Assert.Throws<ArgumentOutOfRangeException>(() => CreatePlan(
                0,
                AllFeatures(),
                GenerousLimits(),
                Element(0, ERHIBindType.Buffer, count: 0)));
            Assert.Throws<ArgumentOutOfRangeException>(() => CreatePlan(
                0,
                AllFeatures(),
                GenerousLimits(),
                Element(
                    0,
                    ERHIBindType.Buffer,
                    stages: ERHIShaderStageMask.None)));
            Assert.Throws<ArgumentOutOfRangeException>(() => CreatePlan(
                0,
                AllFeatures(),
                GenerousLimits(),
                Element(0, ERHIBindType.Pending)));
            Assert.Throws<ArgumentOutOfRangeException>(() => CreatePlan(
                0,
                AllFeatures(),
                GenerousLimits(),
                Element(
                    0,
                    ERHIBindType.Buffer,
                    requirement: (ERHIBindingRequirement)byte.MaxValue)));
            Assert.Throws<ArgumentException>(() => CreatePlan(
                0,
                AllFeatures(),
                GenerousLimits(),
                Element(7, ERHIBindType.Buffer),
                Element(7, ERHIBindType.Buffer)));
            Assert.Throws<ArgumentOutOfRangeException>(() =>
                CreatePlan(0, AllFeatures(), GenerousLimits())
                    .ResolveBinding(-1, ERHIBindType.Buffer));
            Assert.Throws<ArgumentException>(() =>
                CreatePlan(
                        0,
                        AllFeatures(),
                        GenerousLimits(),
                        Element(0, ERHIBindType.Buffer))
                    .ResolveBinding(0, ERHIBindType.Texture2D));
        }

        [Fact]
        public void DescriptorArrays_ShouldRequireExactIndexingFeaturePerClass()
        {
            VulkanDescriptorFeatures noIndexing = new(
                descriptorIndexing: false,
                nullDescriptor: true,
                accelerationStructure: true,
                sampledImageArrayNonUniformIndexing: true,
                storageImageArrayNonUniformIndexing: true,
                uniformBufferArrayNonUniformIndexing: true,
                storageBufferArrayNonUniformIndexing: true);
            Assert.Throws<NotSupportedException>(() => CreatePlan(
                0,
                noIndexing,
                GenerousLimits(),
                Element(0, ERHIBindType.Texture2D, count: 2)));

            VulkanDescriptorFeatures noSampledImageIndexing = new(
                descriptorIndexing: true,
                nullDescriptor: true,
                accelerationStructure: true,
                sampledImageArrayNonUniformIndexing: false,
                storageImageArrayNonUniformIndexing: true,
                uniformBufferArrayNonUniformIndexing: true,
                storageBufferArrayNonUniformIndexing: true);
            Assert.Throws<NotSupportedException>(() => CreatePlan(
                0,
                noSampledImageIndexing,
                GenerousLimits(),
                Element(0, ERHIBindType.Texture2D, count: 2)));

            VulkanBindingTablePlan supported = CreatePlan(
                0,
                AllFeatures(),
                GenerousLimits(),
                Element(0, ERHIBindType.Texture2D, count: 2));
            Assert.Equal(2, supported.DescriptorCount);
        }

        [Fact]
        public void OptionalBinding_ShouldRequireExactNativeNullDescriptorSupport()
        {
            VulkanDescriptorFeatures noNullDescriptor = new(
                descriptorIndexing: true,
                nullDescriptor: false,
                accelerationStructure: true,
                sampledImageArrayNonUniformIndexing: true,
                storageImageArrayNonUniformIndexing: true,
                uniformBufferArrayNonUniformIndexing: true,
                storageBufferArrayNonUniformIndexing: true);
            Assert.Throws<NotSupportedException>(() => CreatePlan(
                0,
                noNullDescriptor,
                GenerousLimits(),
                Element(
                    0,
                    ERHIBindType.Buffer,
                    requirement: ERHIBindingRequirement.Optional)));

            VulkanBindingTablePlan supported = CreatePlan(
                0,
                AllFeatures(),
                GenerousLimits(),
                Element(
                    0,
                    ERHIBindType.Buffer,
                    requirement: ERHIBindingRequirement.Optional));
            Assert.Equal(
                ERHIBindingRequirement.Optional,
                supported.BindInfos[0].Requirement);
        }

        [Fact]
        public void Plan_ShouldEnforcePerSetAndPerStageDescriptorLimits()
        {
            VulkanDescriptorLimits sampledSetLimit = new(
                maximumBoundSets: 8,
                maximumSamplersPerSet: 64,
                maximumSampledImagesPerSet: 1,
                maximumStorageImagesPerSet: 64,
                maximumUniformBuffersPerSet: 64,
                maximumStorageBuffersPerSet: 64,
                maximumSamplersPerStage: 64,
                maximumSampledImagesPerStage: 64,
                maximumStorageImagesPerStage: 64,
                maximumUniformBuffersPerStage: 64,
                maximumStorageBuffersPerStage: 64);
            Assert.Throws<ArgumentOutOfRangeException>(() => CreatePlan(
                0,
                AllFeatures(),
                sampledSetLimit,
                Element(0, ERHIBindType.Texture2D, count: 2)));

            VulkanDescriptorLimits storageStageLimit = new(
                maximumBoundSets: 8,
                maximumSamplersPerSet: 64,
                maximumSampledImagesPerSet: 64,
                maximumStorageImagesPerSet: 64,
                maximumUniformBuffersPerSet: 64,
                maximumStorageBuffersPerSet: 64,
                maximumSamplersPerStage: 64,
                maximumSampledImagesPerStage: 64,
                maximumStorageImagesPerStage: 64,
                maximumUniformBuffersPerStage: 64,
                maximumStorageBuffersPerStage: 1);
            Assert.Throws<ArgumentOutOfRangeException>(() => CreatePlan(
                0,
                AllFeatures(),
                storageStageLimit,
                Element(
                    0,
                    ERHIBindType.StorageBuffer,
                    stages: ERHIShaderStageMask.Compute),
                Element(
                    1,
                    ERHIBindType.StorageBuffer,
                    stages: ERHIShaderStageMask.Compute)));
        }

        [Fact]
        public void StructuralIdentity_ShouldIncludeCountStageTypeAndRequirement()
        {
            VulkanBindingTablePlan expected = CreatePlan(
                2,
                AllFeatures(),
                GenerousLimits(),
                Element(5, ERHIBindType.Buffer));
            VulkanBindingTablePlan equivalent = CreatePlan(
                2,
                AllFeatures(),
                GenerousLimits(),
                Element(5, ERHIBindType.Buffer));
            VulkanBindingTablePlan optional = CreatePlan(
                2,
                AllFeatures(),
                GenerousLimits(),
                Element(
                    5,
                    ERHIBindType.Buffer,
                    requirement: ERHIBindingRequirement.Optional));
            VulkanBindingTablePlan differentStage = CreatePlan(
                2,
                AllFeatures(),
                GenerousLimits(),
                Element(
                    5,
                    ERHIBindType.Buffer,
                    stages: ERHIShaderStageMask.Fragment));
            VulkanBindingTablePlan differentType = CreatePlan(
                2,
                AllFeatures(),
                GenerousLimits(),
                Element(5, ERHIBindType.Texture2D));

            Assert.True(expected.StructurallyEquals(equivalent));
            Assert.False(expected.StructurallyEquals(optional));
            Assert.False(expected.StructurallyEquals(differentStage));
            Assert.False(expected.StructurallyEquals(differentType));
            Assert.False(expected.StructurallyEquals(null));
        }

        private static VulkanBindingTablePlan CreatePlan(
            uint index,
            VulkanDescriptorFeatures features,
            VulkanDescriptorLimits limits,
            params RHIBindingTableLayoutElement[] elements)
        {
            return new VulkanBindingTablePlan(
                new RHIBindingTableLayoutDescriptor
                {
                    Index = index,
                    Elements = elements,
                },
                features,
                limits);
        }

        private static VulkanDescriptorFeatures AllFeatures()
        {
            return new VulkanDescriptorFeatures(
                descriptorIndexing: true,
                nullDescriptor: true,
                accelerationStructure: true,
                sampledImageArrayNonUniformIndexing: true,
                storageImageArrayNonUniformIndexing: true,
                uniformBufferArrayNonUniformIndexing: true,
                storageBufferArrayNonUniformIndexing: true);
        }

        private static VulkanDescriptorLimits GenerousLimits()
        {
            return new VulkanDescriptorLimits(
                maximumBoundSets: 8,
                maximumSamplersPerSet: 64,
                maximumSampledImagesPerSet: 64,
                maximumStorageImagesPerSet: 64,
                maximumUniformBuffersPerSet: 64,
                maximumStorageBuffersPerSet: 64,
                maximumSamplersPerStage: 64,
                maximumSampledImagesPerStage: 64,
                maximumStorageImagesPerStage: 64,
                maximumUniformBuffersPerStage: 64,
                maximumStorageBuffersPerStage: 64);
        }

        private static RHIBindingTableLayoutElement Element(
            uint slot,
            ERHIBindType type,
            uint count = 1,
            ERHIShaderStageMask stages = ERHIShaderStageMask.Compute,
            ERHIBindingRequirement requirement =
                ERHIBindingRequirement.Required)
        {
            return new RHIBindingTableLayoutElement
            {
                Slot = slot,
                Count = count,
                Type = type,
                Stages = stages,
                Requirement = requirement,
            };
        }
    }
}