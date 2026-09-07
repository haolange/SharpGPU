using System;
using System.IO;
using System.Reflection;
using System.Runtime.ExceptionServices;
using System.Runtime.Loader;
using Xunit;

namespace SharpGPU.Conformance.Tests
{
    public sealed class NativeLibraryConfigurationTests
    {
        [Fact]
        public void ExplicitLocations_ResolveCanonicalDirectoriesAndFreezeAfterUse()
        {
            string root = Path.Combine(Path.GetTempPath(), "SharpGPU.NativePaths." + Guid.NewGuid().ToString("N"));
            string[] directories = { Path.Combine(root, "ML 目录"), Path.Combine(root, "Storage"), Path.Combine(root, "PIX") };
            foreach (string directory in directories)
            {
                Directory.CreateDirectory(directory);
            }
            try
            {
                using IsolatedConfiguration configuration = new IsolatedConfiguration();
                configuration.Configure(Path.Combine(directories[0], "."), directories[1], directories[2]);
                Assert.Equal(Path.Combine(directories[0], "DirectML.dll"), configuration.Resolve("DirectML", "DirectML.dll"));
                Assert.Equal(Path.Combine(directories[1], "dstorage.dll"), configuration.Resolve("DirectStorage", "dstorage.dll"));
                Assert.Equal(Path.Combine(directories[2], "WinPixEventRuntime.dll"), configuration.Resolve("PIX", "WinPixEventRuntime.dll"));
                Assert.Throws<InvalidOperationException>(() => configuration.Configure(directories[0], directories[1], directories[2]));
            }
            finally
            {
                foreach (string directory in directories)
                {
                    Directory.Delete(directory);
                }
                Directory.Delete(root);
            }
        }

        [Fact]
        public void InvalidExplicitLocations_FailWithoutChangingDefaultResolution()
        {
            using IsolatedConfiguration configuration = new IsolatedConfiguration();
            string valid = Path.GetFullPath(Path.GetTempPath());
            Assert.Throws<ArgumentException>(() => configuration.Configure("relative", valid, valid));
            string missing = Path.Combine(valid, "SharpGPU.Missing." + Guid.NewGuid().ToString("N"));
            Assert.Throws<DirectoryNotFoundException>(() => configuration.Configure(missing, valid, valid));
            string resolved = configuration.Resolve("PIX", "WinPixEventRuntime.dll");
            Assert.True(Path.IsPathFullyQualified(resolved));
            Assert.StartsWith(AppContext.BaseDirectory, resolved, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("SharpGPU.Missing.", resolved, StringComparison.Ordinal);
            Assert.Throws<InvalidOperationException>(() => configuration.Configure(valid, valid, valid));
        }

        // Each test loads the actual product assembly with independent static state.
        // No backend or native runtime is loaded, and no test reset API is shipped.
        private sealed class IsolatedConfiguration : IDisposable
        {
            private readonly AssemblyLoadContext m_Context = new AssemblyLoadContext("SharpGPU.NativeConfiguration", isCollectible: true);
            private readonly Type m_Type;

            public IsolatedConfiguration()
            {
                Assembly assembly = m_Context.LoadFromAssemblyPath(typeof(SharpGpuNativeLibraries).Assembly.Location);
                m_Type = assembly.GetType(typeof(SharpGpuNativeLibraries).FullName!, throwOnError: true)!;
            }

            public void Configure(string directML, string storage, string pix)
            {
                Invoke("Configure", new object[] { directML, storage, pix });
            }

            public string Resolve(string library, string file)
            {
                return (string)Invoke("Resolve", new object[] { library, file })!;
            }

            private object? Invoke(string method, object[] arguments)
            {
                try
                {
                    return m_Type.GetMethod(method, BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic)!.Invoke(null, arguments);
                }
                catch (TargetInvocationException exception) when (exception.InnerException is not null)
                {
                    ExceptionDispatchInfo.Capture(exception.InnerException).Throw();
                    throw;
                }
            }

            public void Dispose()
            {
                m_Context.Unload();
            }
        }
    }
}
