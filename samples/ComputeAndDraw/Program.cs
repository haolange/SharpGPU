using System;

namespace SharpGPU.Examples
{
    internal static class Program
    {
        private static void Main()
        {
            ComputeWorkload.Run();
            foreach (ERHIBackend backend in new[] { ERHIBackend.DirectX12, ERHIBackend.Vulkan })
            {
                if (RHIInstance.IsBackendSupported(backend, out _)) RasterWorkload.Run(backend);
            }
            Console.WriteLine("Compute and draw resources disposed.");
        }
    }
}
