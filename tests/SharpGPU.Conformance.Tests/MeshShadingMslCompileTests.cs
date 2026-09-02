using Xunit;
using System;
using SharpShader.HLSLCrossCompiler;

namespace SharpGPU.Conformance.Tests
{
    [Trait("Category", "SharpGpuPortable")]
    public sealed class MeshShadingMslCompileTests
    {
        [Fact]
        public void MeshShader_MslTranslate_IsCompileLevelOnly()
        {
            ShaderCompileResult result = HLSLCrossCompiler.Compile(
                new ShaderCompileRequest
                {
                    Source = """
struct MeshVertex
{
    float4 Position : SV_Position;
};

[outputtopology("triangle")]
[numthreads(1, 1, 1)]
void ms_main(out vertices MeshVertex verticesOut[3], out indices uint3 primitives[1])
{
    SetMeshOutputCounts(3, 1);
    verticesOut[0].Position = float4(-1.0, -1.0, 0.0, 1.0);
    verticesOut[1].Position = float4(-1.0, 3.0, 0.0, 1.0);
    verticesOut[2].Position = float4(3.0, -1.0, 0.0, 1.0);
    primitives[0] = uint3(0, 1, 2);
}
""",
                    SourceName = "MeshShadingMslCompileTests.hlsl",
                    EntryPoint = "ms_main",
                    Stage = ShaderStageKind.Mesh,
                    ShaderModel = new ShaderModelVersion(6, 5),
                    Target = ShaderTargetKind.Msl,
                    SpirvOptions = new SpirvCompileOptions
                    {
                        TargetEnvironment = "vulkan1.2",
                    },
                    MslOptions = new MslCompileOptions
                    {
                        Platform = MslTargetPlatform.MacOS,
                    },
                });
            Assert.NotEmpty(result.Bytecode);
        }
    }
}
