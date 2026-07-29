using System;
using Vortice.Direct3D12;

namespace SharpGPU
{
    internal sealed class Dx12IndirectCommandLayout : RHIIndirectCommandLayout
    {
        public ID3D12CommandSignature NativeCommandSignature { get; }
        public Dx12Device Device { get; }

        internal Dx12IndirectCommandLayout(
            Dx12Device device,
            in RHIIndirectCommandLayoutDescriptor descriptor)
            : base(descriptor)
        {
            ArgumentNullException.ThrowIfNull(device);
            ValidateForDevice(device);
            Device = device;

            CommandSignatureDescription nativeDescriptor = new()
            {
                NodeMask = 0,
                ByteStride = checked((int)RecordStride),
                IndirectArguments = CreateIndirectArguments()
            };
            SharpGen.Runtime.Result result = device.NativeDevice.CreateCommandSignature(
                nativeDescriptor,
                null,
                out ID3D12CommandSignature? nativeCommandSignature);
            NativeCommandSignature = Dx12Utility.RequireCreatedObject(
                nativeCommandSignature,
                result,
                "ID3D12Device.CreateCommandSignature(indirect layout)");
        }

        private IndirectArgumentDescription[] CreateIndirectArguments()
        {
            IndirectArgumentDescription[] nativeArguments =
                new IndirectArgumentDescription[TokenCount];
            ReadOnlySpan<RHIIndirectTokenDescriptor> tokens = Tokens.Span;
            for (int i = 0; i < tokens.Length; ++i)
            {
                nativeArguments[i] = tokens[i].Type switch
                {
                    ERHIIndirectTokenType.VertexBuffer =>
                        new IndirectArgumentDescription
                        {
                            Type = IndirectArgumentType.VertexBufferView,
                            VertexBuffer =
                                new IndirectArgumentDescription.VertexBufferArgument
                                {
                                    Slot = tokens[i].VertexBufferSlot
                                }
                        },
                    ERHIIndirectTokenType.IndexBuffer =>
                        new IndirectArgumentDescription
                        {
                            Type = IndirectArgumentType.IndexBufferView
                        },
                    ERHIIndirectTokenType.Draw => new IndirectArgumentDescription
                    {
                        Type = IndirectArgumentType.Draw
                    },
                    ERHIIndirectTokenType.DrawIndexed => new IndirectArgumentDescription
                    {
                        Type = IndirectArgumentType.DrawIndexed
                    },
                    ERHIIndirectTokenType.Dispatch => new IndirectArgumentDescription
                    {
                        Type = IndirectArgumentType.Dispatch
                    },
                    ERHIIndirectTokenType.DispatchMesh => new IndirectArgumentDescription
                    {
                        Type = IndirectArgumentType.DispatchMesh
                    },
                    _ => throw new ArgumentOutOfRangeException(nameof(tokens))
                };
            }
            return nativeArguments;
        }

        protected override void Release()
        {
            NativeCommandSignature.Dispose();
        }
    }
}
