using System;
using System.IO;
using System.Text.Json;
using SharpGPU;

namespace SharpGPU.MLCook
{
    /// <summary>
    /// Offline ML binary cook. Metal: wrap official .mtlpackage (+ reflection) 鈫?.mtlmlbin.
    /// DX12: serialize RHIMLProgramIR / preset 鈫?.dmlbin (Windows DirectML host).
    /// Preferred Metal source path: CoreML .mlpackage 鈫?xcrun metal-package-builder -ml 鈫?.mtlpackage.
    /// </summary>
    internal static class Program
    {
        private static int Main(string[] args)
        {
            if (args.Length == 0 || HasFlag(args, "--help") || HasFlag(args, "-h"))
            {
                PrintUsage();
                return 0;
            }

            string? backend = GetOption(args, "--backend");
            string? input = GetOption(args, "--input");
            string? output = GetOption(args, "--output");
            string? reflectionPath = GetOption(args, "--reflection");
            string? preset = GetOption(args, "--preset");

            if (string.IsNullOrWhiteSpace(backend) || string.IsNullOrWhiteSpace(output))
            {
                Console.Error.WriteLine("error: --backend and --output are required.");
                PrintUsage();
                return 2;
            }

            Directory.CreateDirectory(output);

            try
            {
                if (string.Equals(backend, "metal", StringComparison.OrdinalIgnoreCase))
                {
                    return CookMetal(input, reflectionPath, preset, output);
                }

                if (string.Equals(backend, "dx12", StringComparison.OrdinalIgnoreCase))
                {
                    return CookDx12(preset, output);
                }

                Console.Error.WriteLine($"error: unsupported backend '{backend}'.");
                return 2;
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine("error: " + ex.Message);
                return 1;
            }
        }

        private static int CookMetal(string? input, string? reflectionPath, string? preset, string outputDir)
        {
            string packageDir = ResolveMetalPackageDirectory(input, preset);
            RHIMLBinaryReflection reflection = LoadOrBuildMetalReflection(packageDir, reflectionPath, preset);
            RHIMLBinary binary = MetalMlBinaryCodec.PackFromMtlPackageDirectory(packageDir, in reflection);
            string outFile = Path.Combine(outputDir, Path.GetFileNameWithoutExtension(packageDir.TrimEnd(Path.DirectorySeparatorChar)) + ".mtlmlbin");
            File.WriteAllBytes(outFile, binary.Payload.ToArray());
            string sidecar = Path.ChangeExtension(outFile, ".reflection.json");
            WriteReflectionSidecar(sidecar, in reflection);
            Console.WriteLine($"wrote {outFile}");
            Console.WriteLine($"wrote {sidecar}");
            return 0;
        }

        private static int CookDx12(string? preset, string outputDir)
        {
            if (!OperatingSystem.IsWindows())
            {
                Console.Error.WriteLine("error: dx12 cook requires Windows (DirectML).");
                return 3;
            }

            RHIMLProgramIR program = BuildDx12Preset(preset ?? "elementwise_add");
            RHIMLBinary binary = Dx12MlBinaryCodec.Pack(program);
            string outFile = Path.Combine(outputDir, (preset ?? "elementwise_add") + ".dmlbin");
            File.WriteAllBytes(outFile, binary.Payload.ToArray());
            Console.WriteLine($"wrote {outFile}");
            return 0;
        }

        private static string ResolveMetalPackageDirectory(string? input, string? preset)
        {
            if (!string.IsNullOrWhiteSpace(input))
            {
                if (Directory.Exists(input) && input.EndsWith(".mtlpackage", StringComparison.OrdinalIgnoreCase))
                {
                    return Path.GetFullPath(input);
                }

                if (Directory.Exists(input) && input.EndsWith(".mlpackage", StringComparison.OrdinalIgnoreCase))
                {
                    string tempOut = Path.Combine(Path.GetTempPath(), "sharpgpu-mlcook-" + Guid.NewGuid().ToString("N") + ".mtlpackage");
                    InvokeMetalPackageBuilder(input, tempOut);
                    return tempOut;
                }

                throw new InvalidOperationException($"Metal --input must be a .mtlpackage or .mlpackage directory: {input}");
            }

            string repoFixture = Path.GetFullPath(Path.Combine(
                AppContext.BaseDirectory,
                "..", "..", "..", "..", "..", "..",
                "Tests", "TestData", "SharpGPU.MLCook",
                (preset ?? "elementwise_add") + ".mtlpackage"));
            if (Directory.Exists(repoFixture))
            {
                return repoFixture;
            }

            throw new InvalidOperationException(
                "Metal cook requires --input <.mtlpackage|.mlpackage> or a TestData SharpGPU.MLCook preset package.");
        }

        private static void InvokeMetalPackageBuilder(string mlPackage, string mtlPackage)
        {
            System.Diagnostics.ProcessStartInfo psi = new System.Diagnostics.ProcessStartInfo
            {
                FileName = "xcrun",
                ArgumentList = { "metal-package-builder", "-ml", mlPackage, "-o", mtlPackage },
                RedirectStandardOutput = true,
                RedirectStandardError = true,
            };
            using System.Diagnostics.Process process = System.Diagnostics.Process.Start(psi)
                ?? throw new InvalidOperationException("Failed to start metal-package-builder.");
            string stdout = process.StandardOutput.ReadToEnd();
            string stderr = process.StandardError.ReadToEnd();
            process.WaitForExit();
            if (process.ExitCode != 0)
            {
                throw new InvalidOperationException(
                    $"metal-package-builder failed ({process.ExitCode}): {stderr}\n{stdout}");
            }
        }

        private static RHIMLBinaryReflection LoadOrBuildMetalReflection(string packageDir, string? reflectionPath, string? preset)
        {
            if (!string.IsNullOrWhiteSpace(reflectionPath) && File.Exists(reflectionPath))
            {
                return ReadReflectionJson(reflectionPath);
            }

            string sibling = Path.ChangeExtension(packageDir.TrimEnd(Path.DirectorySeparatorChar), null) + ".reflection.json";
            if (File.Exists(sibling))
            {
                return ReadReflectionJson(sibling);
            }

            return BuildMetalPresetReflection(preset ?? "elementwise_add");
        }

        private static RHIMLBinaryReflection BuildMetalPresetReflection(string preset)
        {
            RHIMLTensorDescriptor t23 = FloatTensor(2, 3);
            RHIMLTensorDescriptor t32 = FloatTensor(3, 2);
            RHIMLTensorDescriptor t22 = FloatTensor(2, 2);
            return preset switch
            {
                "elementwise_add" => new RHIMLBinaryReflection
                {
                    EntryName = "main",
                    IntermediateHeapSizeHint = 0,
                    Bindings =
                    [
                        Binding("arg0", 0, ERHIMLTensorBindingKind.Input, t23),
                        Binding("arg1", 1, ERHIMLTensorBindingKind.Input, t23),
                        Binding("out0", 0, ERHIMLTensorBindingKind.Output, t23),
                    ],
                },
                "matmul" => new RHIMLBinaryReflection
                {
                    EntryName = "main",
                    IntermediateHeapSizeHint = 0,
                    Bindings =
                    [
                        Binding("arg0", 0, ERHIMLTensorBindingKind.Input, t23),
                        Binding("arg1", 1, ERHIMLTensorBindingKind.Input, t32),
                        Binding("out0", 0, ERHIMLTensorBindingKind.Output, t22),
                    ],
                },
                _ => throw new InvalidOperationException($"Unknown Metal reflection preset '{preset}'."),
            };
        }

        private static RHIMLProgramIR BuildDx12Preset(string preset)
        {
            RHIMLTensorDescriptor t23 = FloatTensor(2, 3);
            if (preset == "elementwise_add")
            {
                RHIMLOpDescriptor add = RHIMLOpDescriptor.Create(
                    ERHIMLOpKind.ElementWiseAdd,
                    [RHIMLOpTensorRef.FromInput(0), RHIMLOpTensorRef.FromInput(1)],
                    t23,
                    "Add");
                return RHIMLProgramIR.Create("elementwise_add", [t23, t23], [t23], [add]);
            }

            throw new InvalidOperationException($"Unknown DX12 preset '{preset}'.");
        }

        private static RHIMLBinaryReflection ReadReflectionJson(string path)
        {
            using FileStream stream = File.OpenRead(path);
            using JsonDocument doc = JsonDocument.Parse(stream);
            JsonElement root = doc.RootElement;
            string entry = root.TryGetProperty("entryName", out JsonElement entryEl) ? entryEl.GetString() ?? "main" : "main";
            ulong heapHint = root.TryGetProperty("intermediateHeapSizeHint", out JsonElement heapEl) ? heapEl.GetUInt64() : 0UL;
            JsonElement bindingsEl = root.GetProperty("bindings");
            RHIMLTensorBindingInfo[] bindings = new RHIMLTensorBindingInfo[bindingsEl.GetArrayLength()];
            int i = 0;
            foreach (JsonElement binding in bindingsEl.EnumerateArray())
            {
                string name = binding.GetProperty("name").GetString() ?? string.Empty;
                uint index = binding.GetProperty("index").GetUInt32();
                string kindText = binding.GetProperty("kind").GetString() ?? "Input";
                ERHIMLTensorBindingKind kind = Enum.Parse<ERHIMLTensorBindingKind>(kindText, ignoreCase: true);
                uint[] dims = binding.GetProperty("dimensions").EnumerateArray().Select(e => e.GetUInt32()).ToArray();
                bindings[i++] = Binding(name, index, kind, FloatTensor(dims));
            }

            return new RHIMLBinaryReflection
            {
                EntryName = entry,
                Bindings = bindings,
                IntermediateHeapSizeHint = heapHint,
            };
        }

        private static void WriteReflectionSidecar(string path, in RHIMLBinaryReflection reflection)
        {
            var payload = new
            {
                entryName = reflection.EntryName,
                intermediateHeapSizeHint = reflection.IntermediateHeapSizeHint,
                bindings = reflection.Bindings.Select(b => new
                {
                    name = b.Name,
                    index = b.Index,
                    kind = b.Kind.ToString(),
                    dataType = b.Descriptor.DataType.ToString(),
                    dimensions = b.Descriptor.Dimensions.ToArray(),
                }).ToArray(),
            };
            File.WriteAllText(path, JsonSerializer.Serialize(payload, new JsonSerializerOptions { WriteIndented = true }) + "\n");
        }

        private static RHIMLTensorDescriptor FloatTensor(params uint[] dims)
        {
            return new RHIMLTensorDescriptor
            {
                DataType = ERHIMLDataType.Float32,
                UsageFlag = ERHITensorUsage.MachineLearning | ERHITensorUsage.Read | ERHITensorUsage.Write,
                StorageMode = ERHIStorageMode.GPULocal,
                Dimensions = dims,
            };
        }

        private static RHIMLTensorBindingInfo Binding(string name, uint index, ERHIMLTensorBindingKind kind, RHIMLTensorDescriptor descriptor)
        {
            return new RHIMLTensorBindingInfo
            {
                Name = name,
                Index = index,
                Kind = kind,
                Descriptor = descriptor,
            };
        }

        private static string? GetOption(string[] args, string name)
        {
            for (int i = 0; i < args.Length - 1; ++i)
            {
                if (string.Equals(args[i], name, StringComparison.OrdinalIgnoreCase))
                {
                    return args[i + 1];
                }
            }

            return null;
        }

        private static bool HasFlag(string[] args, string name)
        {
            for (int i = 0; i < args.Length; ++i)
            {
                if (string.Equals(args[i], name, StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }

            return false;
        }

        private static void PrintUsage()
        {
            Console.WriteLine("""
                SharpGPU.MLCook 鈥?offline ML binary cook (ADR-0052 / ADR-0053)

                Usage:
                  SharpGPU.MLCook --backend metal --input <dir.mtlpackage|.mlpackage> --output <dir> [--reflection <json>] [--preset name]
                  SharpGPU.MLCook --backend dx12 --preset elementwise_add --output <dir>   # Windows only

                Metal preferred source path:
                  CoreML .mlpackage 鈫?xcrun metal-package-builder -ml 鈫?.mtlpackage 鈫?.mtlmlbin
                """);
        }
    }
}
