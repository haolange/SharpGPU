# Verification

The independent GPU graph includes all desktop managed backends regardless of build host. Native calls remain platform-gated. These commands are TODO(UNVERIFIED) until recorded with current source and host evidence.

```powershell
dotnet build src/SharpGPU/SharpGPU.csproj -c Debug -p:StackReferenceMode=Source
dotnet build src/SharpGPU/SharpGPU.csproj -c Release -p:StackReferenceMode=Source
```

The vendored dependency packages use SharpGPU.Vortice.* identities and version 3.8.3-sharpgpu.1 to distinguish the DXR 1.2/OMM patched bindings. No upstream Vortice.Direct3D12 package may enter this graph alongside that implementation. Binding regeneration and matching-platform runtime gates remain pending migration acceptance.
